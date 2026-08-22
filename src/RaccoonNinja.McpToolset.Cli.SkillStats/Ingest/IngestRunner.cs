using System.Text.Json;
using Microsoft.Data.Sqlite;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;
using RaccoonNinja.McpToolset.Common.SkillStats.Domain;
using RaccoonNinja.McpToolset.Common.SkillStats.Storage;

namespace RaccoonNinja.McpToolset.Cli.SkillStats.Ingest;

/// <summary>
/// The ingest-pipeline. Reads the hook JSON from stdin, extracts the skill name and args, and appends
/// one store row. Every stage runs inside a guarded region, so no expected failure escapes: a failure
/// logs one line, names the log path on stderr, and exits 2; a benign skip (no skill, oversize input)
/// exits 0. It is exercised in-process with injected streams and config loader.
/// </summary>
internal static class IngestRunner
{
    /// <summary>Exit code for a recording failure; on PostToolUse this surfaces stderr to Claude.</summary>
    public const int ExitFailure = 2;

    /// <summary>Exit code for a recorded row or a benign skip.</summary>
    private const int ExitSuccess = 0;
    private const int StdinCapBytes = 4 * 1024 * 1024;
    private const string DefaultLogHint = "~/.skill-stats/ingest-errors.log";

    /// <summary>Run the ingest-pipeline once.</summary>
    /// <param name="stdin">The hook payload stream (stdin in production).</param>
    /// <param name="stderr">The stream to name the log path on when recording fails.</param>
    /// <param name="loadConfig">The config loader (<see cref="SkillStatsConfig.Load"/> in production).</param>
    /// <returns><see cref="ExitSuccess"/> or <see cref="ExitFailure"/>.</returns>
    public static int Run(Stream stdin, TextWriter stderr, Func<SkillStatsConfig> loadConfig)
    {
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(loadConfig);

        SkillStatsConfig config;
        try
        {
            config = loadConfig();
        }
        catch (SkillStatsStartupException)
        {
            // No resolved home means there is nowhere to log; name the default path and stop.
            stderr.WriteLine($"skill-usage: failed to record skill usage; see {DefaultLogHint}");
            return ExitFailure;
        }

        var stage = "read-stdin";
        try
        {
            var (bytes, oversize) = BoundedRead(stdin);
            if (oversize)
            {
                ErrorLog.AppendSkipped(config.ErrorLogPath, "oversize", null);
                return ExitSuccess;
            }

            stage = "parse";
            using var document = JsonDocument.Parse(bytes);

            stage = "extract";
            var payload = HookPayload.Extract(document.RootElement);
            if (!SkillName.TryNormalize(payload.RawSkill, out var skill))
            {
                ErrorLog.AppendSkipped(config.ErrorLogPath, "no_skill", payload.ToolInputKeys);
                return ExitSuccess;
            }

            stage = "open-store";
            config.EnsureDirs();
            var factory = new SqliteConnectionFactory(config);
            Migrator.Run(factory);
            var repository = new SkillUsageRepository(factory);

            stage = "insert";
            repository.Insert(new SkillUsageRecord
            {
                UsedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Skill = skill,
                Args = payload.Args,
                SessionId = payload.SessionId,
                Cwd = payload.Cwd,
            });

            return ExitSuccess;
        }
        catch (Exception ex) when (ex is IOException or JsonException or SkillStatsStartupException or SqliteException or UnauthorizedAccessException)
        {
            ErrorLog.AppendFailure(config.ErrorLogPath, stage, ex);
            stderr.WriteLine($"skill-usage: failed to record skill usage; see {config.ErrorLogPath}");
            return ExitFailure;
        }
    }

    private static (byte[] Bytes, bool Oversize) BoundedRead(Stream stdin)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stdin.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > StdinCapBytes)
            {
                return (null, true);
            }
        }

        return (buffer.ToArray(), false);
    }
}