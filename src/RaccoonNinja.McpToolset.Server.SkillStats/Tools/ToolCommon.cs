using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.SkillStats.Envelope;
using RaccoonNinja.McpToolset.Server.SkillStats.Errors;
using RaccoonNinja.McpToolset.Server.SkillStats.Logging;
using RaccoonNinja.McpToolset.Server.SkillStats.Metrics;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tools;

/// <summary>
/// Shared per-tool helpers: per-call context, the store-availability gate, argument validation,
/// timestamp formatting, and envelope wrapping. <see cref="WrapAsync"/> is the single owner of the
/// per-call outcome metric and the one place any escaping exception becomes a failure envelope, so a
/// raw .NET exception message, which can carry an absolute path, never reaches model context.
/// </summary>
public sealed class ToolCommon(
    SessionMetrics metrics,
    ILoggerFactory loggerFactory,
    SkillStatsConfig config,
    SqliteConnectionFactory factory)
{
    /// <summary>The largest accepted <c>days_back</c> window, in days (about ten years).</summary>
    public const int MaxDaysBack = 3650;

    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private static readonly string StoreMissingMessage =
        "skill usage database not found; install the skill-usage PostToolUse hook first";

    private static readonly string StoreUnreadableMessage =
        "skill usage database is not readable or has an incompatible schema";

    /// <summary>Create a per-call correlation context for <paramref name="tool"/>.</summary>
    /// <param name="tool">The tool name.</param>
    /// <returns>The call context.</returns>
    public CallContext MakeContext(string tool)
        => new(tool, loggerFactory.CreateLogger(tool));

    /// <summary>
    /// Assert that the store exists and carries a schema this build understands, throwing
    /// <see cref="ErrorCodes.StoreUnavailable"/> otherwise. A <see cref="SqliteException"/> from the
    /// probe open (for example the DB vanished between the file check and the open) is left to
    /// <see cref="WrapAsync"/>, which also maps it to <see cref="ErrorCodes.StoreUnavailable"/>.
    /// </summary>
    /// <exception cref="SkillStatsException">Thrown when the store is missing, empty, or of an incompatible schema.</exception>
    public void GuardStore()
    {
        if (!File.Exists(config.DbPath))
        {
            throw SkillStatsException.StoreUnavailable(StoreMissingMessage);
        }

        long schemaVersion;
        using (var connection = factory.OpenReader())
        {
            schemaVersion = Migrator.CurrentVersion(connection);
        }

        if (schemaVersion is 0 || schemaVersion > Migrator.LatestVersion)
        {
            throw SkillStatsException.StoreUnavailable(StoreUnreadableMessage);
        }
    }

    /// <summary>Validate an optional <c>days_back</c> window and convert it to an inclusive epoch cutoff.</summary>
    /// <param name="daysBack">The window in days, or <c>null</c> for all time.</param>
    /// <returns>The inclusive lower bound on <c>used_at</c> (Unix seconds), or <c>null</c> for all time.</returns>
    /// <exception cref="SkillStatsException">Thrown when <paramref name="daysBack"/> is outside 1..<see cref="MaxDaysBack"/>.</exception>
    public static long? ResolveCutoff(int? daysBack)
    {
        if (daysBack is null)
        {
            return null;
        }

        if (daysBack < 1 || daysBack > MaxDaysBack)
        {
            throw SkillStatsException.InvalidArgument($"days_back must be between 1 and {MaxDaysBack}");
        }

        return DateTimeOffset.UtcNow.AddDays(-daysBack.Value).ToUnixTimeSeconds();
    }

    /// <summary>Validate a limit against its inclusive 1..<paramref name="max"/> range.</summary>
    /// <param name="limit">The requested limit.</param>
    /// <param name="max">The inclusive maximum for this tool.</param>
    /// <exception cref="SkillStatsException">Thrown when <paramref name="limit"/> is outside the range.</exception>
    public static void ValidateLimit(int limit, int max)
    {
        if (limit < 1 || limit > max)
        {
            throw SkillStatsException.InvalidArgument($"limit must be between 1 and {max}");
        }
    }

    /// <summary>Format a Unix-seconds timestamp as <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    /// <param name="epochSeconds">The timestamp in Unix seconds (UTC).</param>
    /// <returns>The formatted UTC timestamp.</returns>
    public static string FormatTimestamp(long epochSeconds)
        => DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>Format an optional Unix-seconds timestamp, returning <c>null</c> when absent.</summary>
    /// <param name="epochSeconds">The timestamp in Unix seconds (UTC), or <c>null</c>.</param>
    /// <returns>The formatted UTC timestamp, or <c>null</c>.</returns>
    public static string FormatTimestamp(long? epochSeconds)
        => epochSeconds is long value ? FormatTimestamp(value) : null;

    /// <summary>
    /// Run <paramref name="body"/> and translate any escaping exception into a failure envelope. A
    /// <see cref="SkillStatsException"/> maps to its code and safe message; a <see cref="SqliteException"/>
    /// maps to <see cref="ErrorCodes.StoreUnavailable"/>; any other exception maps to a generic internal
    /// error carrying only the exception type name in the log, never its message.
    /// </summary>
    /// <param name="ctx">The call context.</param>
    /// <param name="body">The tool body.</param>
    /// <returns>The tool's envelope, or a failure envelope.</returns>
    public async Task<ResultEnvelope> WrapAsync(CallContext ctx, Func<Task<ResultEnvelope>> body)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var envelope = await body();
            metrics.RecordToolCall(ctx.Tool, "ok");
            return envelope;
        }
        catch (SkillStatsException ex)
        {
            metrics.RecordToolCall(ctx.Tool, OutcomeFor(ex.Code));
            LogToolError(ctx, LogLevel.Warning, ex.Message, ex.Code);
            return ResultEnvelope.Failure(ex);
        }
        catch (SqliteException ex)
        {
            var wrapped = SkillStatsException.StoreUnavailable(StoreUnreadableMessage);
            metrics.RecordToolCall(ctx.Tool, "store_unavailable");
            LogToolError(ctx, LogLevel.Warning, $"sqlite error: {ex.GetType().Name}", wrapped.Code);
            return ResultEnvelope.Failure(wrapped);
        }
        catch (Exception ex)
        {
            var wrapped = new SkillStatsException(ErrorCodes.InternalError, "unexpected error inside tool; see server logs");
            metrics.RecordToolCall(ctx.Tool, "internal_error");
            LogToolError(ctx, LogLevel.Error, $"unexpected error: {ex.GetType().FullName}: {ex.Message}", wrapped.Code);
            return ResultEnvelope.Failure(wrapped);
        }
    }

    private static void LogToolError(CallContext ctx, LogLevel level, string message, string code)
        => ctx.Log(
            level,
            "tool_error",
            message,
            new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.ErrorCode] = code });

    private static string OutcomeFor(string code)
        => code switch
        {
            ErrorCodes.InvalidArgument => "invalid_argument",
            ErrorCodes.StoreUnavailable => "store_unavailable",
            _ => "internal_error",
        };
}