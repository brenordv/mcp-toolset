using System.Text;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Content;

/// <summary>
/// Applies <see cref="NormalizeOptions"/> to a file's decoded text. It works on the raw string, walking it
/// terminator by terminator, so under <see cref="LineEndingMode.Preserve"/> every physical terminator is
/// written back byte-for-byte and a mixed-ending file is never silently unified. Only when the caller asks
/// for <see cref="LineEndingMode.Lf"/> or <see cref="LineEndingMode.Crlf"/> are terminators rewritten. The
/// byte-order-mark decision is reported through <see cref="TransformResult.BomOverride"/> and applied by the
/// codec, not by touching the text.
/// </summary>
public sealed class Normalizer(NormalizeOptions options) : ITextTransform
{
    /// <inheritdoc />
    public TransformResult Transform(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var target = ResolveTarget(text);
        var body = NeedsLineWork() ? ApplyLineWork(text, target) : text;
        var result = ApplyFinalNewline(body, target);
        var changed = !string.Equals(result, text, StringComparison.Ordinal);
        bool? bomOverride = options.Bom == BomMode.Strip ? false : null;

        return new TransformResult
        {
            NewText = result,
            BomOverride = bomOverride,
            MatchCount = changed ? 1 : 0,
        };
    }

    private bool NeedsLineWork()
        => options.TrimTrailingWhitespace || options.LineEndings != LineEndingMode.Preserve;

    private string ApplyLineWork(string text, string target)
    {
        var builder = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            while (i < text.Length && text[i] != '\r' && text[i] != '\n')
            {
                i++;
            }

            var content = text[start..i];
            if (options.TrimTrailingWhitespace)
            {
                content = content.TrimEnd(' ', '\t');
            }

            builder.Append(content);

            if (i >= text.Length)
            {
                break;
            }

            var original = ReadTerminator(text, ref i);
            builder.Append(options.LineEndings switch
            {
                LineEndingMode.Lf => "\n",
                LineEndingMode.Crlf => "\r\n",
                _ => original,
            });
        }

        return builder.ToString();
    }

    private static string ReadTerminator(string text, ref int i)
    {
        if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
        {
            i += 2;
            return "\r\n";
        }

        var terminator = text[i] == '\r' ? "\r" : "\n";
        i += 1;
        return terminator;
    }

    private string ApplyFinalNewline(string text, string target)
        => options.FinalNewline switch
        {
            FinalNewlineMode.Ensure => text.Length == 0 || EndsWithNewline(text) ? text : text + target,
            FinalNewlineMode.Trim => text.TrimEnd('\r', '\n'),
            _ => text,
        };

    private static bool EndsWithNewline(string text)
        => text.Length > 0 && (text[^1] == '\n' || text[^1] == '\r');

    private string ResolveTarget(string text)
        => options.LineEndings switch
        {
            LineEndingMode.Lf => "\n",
            LineEndingMode.Crlf => "\r\n",
            _ => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n",
        };
}