using System.Globalization;
using System.Text;

namespace RaccoonNinja.McpToolset.Cli.SkillUsage.Ingest;

/// <summary>
/// Append-only ingest error log. Every line is one event and carries no payload content. The file
/// self-rotates to <c>ingest-errors.old</c> once it passes 5 MiB, so a persistently wedged store
/// cannot grow it without bounds. All writes are best-effort: a failure here never propagates, since
/// the caller still emits stderr and a non-zero exit.
/// </summary>
internal static class ErrorLog
{
    private const long RotateThresholdBytes = 5L * 1024 * 1024;
    private const int MaxKeys = 16;
    private const int MaxKeyLength = 64;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Append one failure line: timestamp, stage, exception type, and message.</summary>
    /// <param name="path">The error log path.</param>
    /// <param name="stage">The pipeline stage that failed.</param>
    /// <param name="exception">The exception that aborted the stage.</param>
    public static void AppendFailure(string path, string stage, Exception exception)
        => Append(path, $"{Timestamp()} {stage} {exception.GetType().Name}: {exception.Message}");

    /// <summary>Append one skip line: timestamp, reason, and the sanitized <c>tool_input</c> key names.</summary>
    /// <param name="path">The error log path.</param>
    /// <param name="reason">The skip reason (for example <c>no_skill</c> or <c>oversize</c>).</param>
    /// <param name="toolInputKeys">The raw <c>tool_input</c> key names, or <c>null</c>; only names, never values.</param>
    public static void AppendSkipped(string path, string reason, IReadOnlyList<string> toolInputKeys)
    {
        var line = new StringBuilder();
        line.Append(Timestamp()).Append(" skipped reason=").Append(reason);
        if (toolInputKeys is { Count: > 0 })
        {
            line.Append(" tool_input_keys=[").Append(FormatKeys(toolInputKeys)).Append(']');
        }

        Append(path, line.ToString());
    }

    private static string FormatKeys(IReadOnlyList<string> keys)
        => string.Join(",", keys.Take(MaxKeys).Select(Sanitize));

    private static string Sanitize(string key)
    {
        var builder = new StringBuilder(Math.Min(key.Length, MaxKeyLength));
        foreach (var ch in key)
        {
            if (builder.Length == MaxKeyLength)
            {
                break;
            }

            builder.Append(char.IsControl(ch) ? '?' : ch);
        }

        return builder.ToString();
    }

    private static void Append(string path, string line)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Rotate(path);
            File.AppendAllText(path, line + "\n", Utf8NoBom);
        }
        catch (IOException)
        {
            // Best-effort: the caller still emits stderr and a non-zero exit.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort: the caller still emits stderr and a non-zero exit.
        }
    }

    private static void Rotate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > RotateThresholdBytes)
            {
                File.Move(path, Path.ChangeExtension(path, ".old"), overwrite: true);
            }
        }
        catch (IOException)
        {
            // Best-effort rotation; the log just keeps growing this once.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort rotation; the log just keeps growing this once.
        }
    }

    private static string Timestamp()
        => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}