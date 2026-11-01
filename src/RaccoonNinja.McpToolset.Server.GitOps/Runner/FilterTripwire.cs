using System.Text;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Runner;

/// <summary>
/// Layer 3b residual hardening: inspects stderr for git's
/// <c>external filter '&lt;command&gt;' failed</c> message. On a match, emits a single
/// WARN log line naming the tool and the call, never the command, which is repository
/// config text and can hold an absolute path. Forensic only, never blocks.
/// </summary>
public static partial class FilterTripwire
{
    private const string FailedFilterPrefix = "external filter '";
    private const string FailedFilterSuffix = "' failed";

    public static void Inspect(byte[] stderrBytes, IList<string> argv, int? callId, ILogger logger)
    {
        if (stderrBytes == null || stderrBytes.Length == 0) return;

        var text = Encoding.UTF8.GetString(stderrBytes);
        if (!NamesFailedFilter(text)) return;

        var tool = InferTool(argv);

        using (logger.BeginScope(new Dictionary<string, object>
        {
            [LogFields.Event] = "suspicious_filter_invocation",
            [LogFields.Tool] = tool,
            [LogFields.CallId] = callId ?? 0,
        }))
        {
            SuspiciousFilterInvocation(logger);
        }
    }

    /// <summary>
    /// Whether <paramref name="stderr"/> holds git's failed-filter message. git prints the
    /// filter's command between the quotes, and the command can itself contain <c>'</c>
    /// or a newline, so the message counts as found when <c>' failed</c> occurs anywhere
    /// after the first <c>external filter '</c>. Two ordinal searches keep the check
    /// linear in the length of stderr, which can echo repository config text.
    /// </summary>
    /// <param name="stderr">git's stderr, decoded as UTF-8.</param>
    /// <returns><c>true</c> when the message is present.</returns>
    private static bool NamesFailedFilter(string stderr)
    {
        var start = stderr.IndexOf(FailedFilterPrefix, StringComparison.OrdinalIgnoreCase);
        return start >= 0
            && stderr.IndexOf(FailedFilterSuffix, start + FailedFilterPrefix.Length, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string InferTool(IList<string> argv)
    {
        if (argv == null) return "git";
        var skipNext = false;
        for (var i = 1; i < argv.Count; i++)
        {
            var token = argv[i];
            if (skipNext)
            {
                skipNext = false;
                continue;
            }
            if (token is "-c" or "-C")
            {
                skipNext = true;
                continue;
            }
            if (token.StartsWith('-')) continue;
            return token;
        }
        return "git";
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Warning,
        Message = "external filter invocation detected in git stderr")]
    private static partial void SuspiciousFilterInvocation(ILogger logger);
}