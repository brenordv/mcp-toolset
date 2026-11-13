using System.Globalization;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Parser for <c>git grep -z</c> output. With <c>-z</c>, git prints each path as stored, unquoted, and ends it with
/// NUL, so a path can hold a newline but never a NUL. A match (<c>-n</c>) is the path, the line number, each ended by
/// NUL, then the line, which holds no newline but can hold a NUL, ended by a newline; a count (<c>-c</c>) is the path,
/// ended by NUL, then the count, ended by a newline. The reader takes each field up to the byte that ends it, in that
/// order, so no path or line can add or shift a record. When grepping a ref, git prefixes each path with
/// <c>&lt;ref&gt;:</c>, which this parser strips.
/// </summary>
public static class GrepParser
{
    /// <summary>
    /// The <c>-c</c> pair that keeps <c>git grep -z</c> output in the record shape this parser reads:
    /// <c>grep.column=false</c>, so a <c>grep.column</c> setting in the user's or the repository's config cannot add a
    /// column field between a match's line number and its line. A <c>--no-column</c> flag would do the same on current
    /// git, but git before 2.19.0 has no <c>--column</c> option and rejects its negation, while it ignores this key.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Value)> OutputShapeConfig = [("grep.column", "false")];

    /// <summary>Parse <c>git grep -n -z</c> output into ordered <see cref="GrepMatch"/> records.</summary>
    /// <param name="raw">Raw stdout bytes from <c>git grep</c>.</param>
    /// <param name="reference">The ref grepped at, if any; used to strip the <c>ref:</c> path prefix.</param>
    /// <param name="truncated">
    /// Whether the output hit the output cap. A last match cut inside its line comes back with the line cut short; one cut
    /// inside its path or line number is left out.
    /// </param>
    /// <returns>The parsed matches, or an empty list when there is nothing to parse.</returns>
    /// <exception cref="GitCommandException">
    /// A match is malformed (an empty path, a line number that is not digits), or the output ends inside a match and was
    /// not cut by the cap.
    /// </exception>
    public static IReadOnlyList<GrepMatch> Parse(byte[] raw, string reference, bool truncated)
    {
        var text = TextDecoding.Decode(raw);
        var hasRef = !string.IsNullOrWhiteSpace(reference);
        var matches = new List<GrepMatch>();
        var position = 0;
        while (position < text.Length)
        {
            var pathEnd = text.IndexOf('\0', position);
            var lineNumberEnd = pathEnd < 0 ? -1 : text.IndexOf('\0', pathEnd + 1);
            if (lineNumberEnd < 0)
            {
                ThrowUnlessTruncated(truncated);
                break;
            }

            var lineEnd = text.IndexOf('\n', lineNumberEnd + 1);
            if (lineEnd < 0)
            {
                ThrowUnlessTruncated(truncated);
                lineEnd = text.Length;
            }

            matches.Add(new GrepMatch
            {
                Path = StripRefPrefix(ReadPath(text, position, pathEnd), hasRef),
                Line = ReadNumber(text[(pathEnd + 1)..lineNumberEnd]),
                Content = text[(lineNumberEnd + 1)..lineEnd].TrimEnd('\r'),
            });
            position = lineEnd + 1;
        }

        return matches;
    }

    /// <summary>Parse <c>git grep -c -z</c> output: one <see cref="GrepFileCount"/> per file with a matching line.</summary>
    /// <param name="raw">Raw stdout bytes from <c>git grep -c -z</c>.</param>
    /// <param name="reference">The ref grepped at, if any; used to strip the <c>ref:</c> path prefix.</param>
    /// <param name="truncated">Whether the output hit the output cap; a last count cut before its newline is left out.</param>
    /// <returns>The parsed counts in git's order, or an empty list.</returns>
    /// <exception cref="GitCommandException">
    /// A count is malformed (an empty path, a count that is not digits), or the output ends inside a count and was not
    /// cut by the cap.
    /// </exception>
    public static IReadOnlyList<GrepFileCount> ParseCounts(byte[] raw, string reference, bool truncated)
    {
        var text = TextDecoding.Decode(raw);
        var hasRef = !string.IsNullOrWhiteSpace(reference);
        var counts = new List<GrepFileCount>();
        var position = 0;
        while (position < text.Length)
        {
            var pathEnd = text.IndexOf('\0', position);
            var countEnd = pathEnd < 0 ? -1 : text.IndexOf('\n', pathEnd + 1);
            if (countEnd < 0)
            {
                ThrowUnlessTruncated(truncated);
                break;
            }

            counts.Add(new GrepFileCount
            {
                Path = StripRefPrefix(ReadPath(text, position, pathEnd), hasRef),
                MatchCount = ReadNumber(text[(pathEnd + 1)..countEnd]),
            });
            position = countEnd + 1;
        }

        return counts;
    }

    /// <summary>Throw unless the output was cut by the cap, which is the one way a record can end early.</summary>
    /// <param name="truncated">Whether the output hit the output cap.</param>
    /// <exception cref="GitCommandException"><paramref name="truncated"/> is <c>false</c>.</exception>
    private static void ThrowUnlessTruncated(bool truncated)
    {
        if (!truncated)
        {
            throw new GitCommandException(NulRecords.NotWholeRecordsMessage);
        }
    }

    /// <summary>Read the path that runs from <paramref name="start"/> to the NUL at <paramref name="end"/>; git prints no empty path.</summary>
    /// <param name="text">The decoded output.</param>
    /// <param name="start">Where the path starts.</param>
    /// <param name="end">The index of the NUL that ends it.</param>
    /// <returns>The path.</returns>
    /// <exception cref="GitCommandException">The path is empty.</exception>
    private static string ReadPath(string text, int start, int end)
        => end > start ? text[start..end] : throw new GitCommandException(NulRecords.NotWholeRecordsMessage);

    /// <summary>Read a line number or a count: one or more ASCII digits that fit an <see cref="int"/>.</summary>
    /// <param name="digits">The field's text.</param>
    /// <returns>The number.</returns>
    /// <exception cref="GitCommandException">The text is anything else.</exception>
    private static int ReadNumber(string digits)
        => int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new GitCommandException(NulRecords.NotWholeRecordsMessage);

    /// <summary>Strip the <c>&lt;ref&gt;:</c> prefix git puts on every path when grepping a ref.</summary>
    /// <param name="path">The path as git printed it.</param>
    /// <param name="hasRef">Whether the grep ran at a ref.</param>
    /// <returns>The path without the prefix, or <paramref name="path"/> unchanged when no ref was grepped.</returns>
    private static string StripRefPrefix(string path, bool hasRef)
    {
        if (!hasRef)
        {
            return path;
        }

        var colon = path.IndexOf(':');
        return colon >= 0 ? path[(colon + 1)..] : path;
    }
}