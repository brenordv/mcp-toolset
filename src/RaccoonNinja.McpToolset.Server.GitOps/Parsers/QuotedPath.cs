using System.Text;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Reads a path git wrote C-quoted: wrapped in double quotes, with a backslash escape for each double quote, backslash,
/// and control character. git quotes a path this way in a patch header and in its <c>rename</c> and <c>copy</c> lines
/// whenever the path holds one of those characters, whatever <c>core.quotePath</c> says; under the
/// <c>core.quotePath=false</c> every call passes, bytes from 0x80 up stay as they are.
/// </summary>
internal static class QuotedPath
{
    /// <summary>The letters git writes after a backslash, for the characters at the same index in <see cref="EscapedChars"/>.</summary>
    private const string EscapeLetters = "abtnvfr\"\\";

    /// <summary>The characters the letters in <see cref="EscapeLetters"/> stand for, index for index.</summary>
    private const string EscapedChars = "\a\b\t\n\v\f\r\"\\";

    /// <summary>
    /// Unquote the quoted path at the start of <paramref name="text"/>, reading escapes as git's <c>unquote_c_style</c>
    /// does: a letter from <see cref="EscapeLetters"/>, or three octal digits. The first octal digit must be 0 or 1, so
    /// the value is at most 0x7F; git writes a larger one only under <c>core.quotePath=true</c>, as one byte of a
    /// multi-byte UTF-8 character, which a character-by-character reader cannot rebuild.
    /// </summary>
    /// <param name="text">Text that starts with the opening double quote.</param>
    /// <param name="path">The unquoted path, or <c>null</c> when the text does not start with a well-formed quoted path.</param>
    /// <param name="end">The index just past the closing double quote.</param>
    /// <returns><c>true</c> when <paramref name="text"/> starts with a well-formed quoted path.</returns>
    public static bool TryUnquote(string text, out string path, out int end)
    {
        path = null;
        end = 0;
        if (!text.StartsWith('"'))
        {
            return false;
        }

        var builder = new StringBuilder();
        var index = 1;
        while (index < text.Length)
        {
            var current = text[index++];
            if (current == '"')
            {
                path = builder.ToString();
                end = index;
                return true;
            }

            if (current != '\\')
            {
                builder.Append(current);
            }
            else if (TryReadEscape(text, ref index, out var unescaped))
            {
                builder.Append(unescaped);
            }
            else
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// The path in <paramref name="text"/>: unquoted when the whole text is one well-formed quoted path, and as it is
    /// otherwise. A path git leaves unquoted never starts with a double quote.
    /// </summary>
    /// <param name="text">A header side, or the text after a <c>rename from</c>, <c>rename to</c>, <c>copy from</c>, or <c>copy to</c> keyword.</param>
    /// <returns>The unquoted path, or <paramref name="text"/> itself.</returns>
    public static string UnquoteOrKeep(string text)
        => TryUnquote(text, out var path, out var end) && end == text.Length ? path : text;

    /// <summary>Read the escape that follows a backslash and move <paramref name="index"/> past it.</summary>
    /// <param name="text">The quoted text.</param>
    /// <param name="index">The index just after the backslash; moved past the escape when it is well formed.</param>
    /// <param name="unescaped">The character the escape stands for.</param>
    /// <returns><c>true</c> when a well-formed escape starts at <paramref name="index"/>.</returns>
    private static bool TryReadEscape(string text, ref int index, out char unescaped)
    {
        unescaped = default;
        if (index >= text.Length)
        {
            return false;
        }

        var letterIndex = EscapeLetters.IndexOf(text[index]);
        if (letterIndex >= 0)
        {
            unescaped = EscapedChars[letterIndex];
            index++;
            return true;
        }

        if (index + 2 >= text.Length
            || text[index] is not ('0' or '1')
            || !IsOctalDigit(text[index + 1])
            || !IsOctalDigit(text[index + 2]))
        {
            return false;
        }

        unescaped = (char)(((text[index] - '0') << 6) | ((text[index + 1] - '0') << 3) | (text[index + 2] - '0'));
        index += 3;
        return true;
    }

    /// <summary>True when <paramref name="value"/> is one of the digits 0 to 7.</summary>
    /// <param name="value">The character to test.</param>
    /// <returns><c>true</c> for <c>'0'</c> to <c>'7'</c>.</returns>
    private static bool IsOctalDigit(char value) => value is >= '0' and <= '7';
}