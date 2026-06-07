using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;

namespace RaccoonNinja.McpToolset.Server.GitOps.Runner;

/// <summary>
/// Layer 3b residual hardening: inspects stderr for the canonical
/// <c>external filter '&lt;name&gt;' failed</c> pattern. On a match, emits a single
/// WARN log line with a scrubbed driver name. Forensic only, never blocks.
/// </summary>
public static partial class FilterTripwire
{
    private static readonly Regex FilterRegex = FilterRegexPattern();

    public static void Inspect(byte[] stderrBytes, IList<string> argv, int? callId, ILogger logger)
    {
        if (stderrBytes == null || stderrBytes.Length == 0) return;

        var text = Encoding.UTF8.GetString(stderrBytes);
        var match = FilterRegex.Match(text);
        if (!match.Success) return;

        var driver = LogScrubbing.ScrubDriverName(match.Groups[1].Value);
        var tool = InferTool(argv);

        using (logger.BeginScope(new Dictionary<string, object>
        {
            [LogFields.Event] = "suspicious_filter_invocation",
            [LogFields.Tool] = tool,
            [LogFields.DriverName] = driver,
            [LogFields.CallId] = callId ?? 0,
        }))
        {
            SuspiciousFilterInvocation(logger);
        }
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

    [GeneratedRegex(@"external filter '([^']*)' failed", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex FilterRegexPattern();
}