using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>
/// Stateful, single-use reader that converts a textual unified diff (the output of
/// <c>git diff</c>) into a sequence of <see cref="FileDiff"/> records. The per-file cursor
/// state lives in instance fields rather than captured locals, which keeps the line-dispatch
/// loop small and the flush logic explicit. Create one instance per diff.
/// </summary>
internal sealed class UnifiedDiffReader
{
    private const string DiffHeaderPrefix = "diff --git ";
    private const string NewFileModePrefix = "new file mode";
    private const string DeletedFileModePrefix = "deleted file mode";
    private const string RenameFromPrefix = "rename from ";
    private const string RenameToPrefix = "rename to ";
    private const string CopyFromPrefix = "copy from ";
    private const string CopyToPrefix = "copy to ";
    private const string BinaryFilesPrefix = "Binary files";
    private const string HunkHeaderPrefix = "@@";
    private const string OldSidePrefix = "a/";
    private const string NewSidePrefix = "b/";
    private const string NewSideMarker = " b/";

    private readonly List<FileDiff> _files = [];
    private readonly List<DiffHunk> _hunks = [];

    private string _path;
    private string _oldPath;
    private ChangeType _changeType = ChangeType.Modified;
    private bool _isBinary;
    private List<string> _hunkLines;
    private DiffHunk _hunkHeader;

    /// <summary>Parse <paramref name="text"/> into per-file diff records.</summary>
    /// <param name="text">The decoded unified diff body.</param>
    /// <returns>One <see cref="FileDiff"/> per file section in the diff.</returns>
    public IReadOnlyList<FileDiff> Read(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            DispatchLine(line);
        }
        FlushFile();
        return _files;
    }

    /// <summary>Route a diff line to the handler matching its leading marker.</summary>
    private void DispatchLine(string line)
    {
        if (line.StartsWith(DiffHeaderPrefix, StringComparison.Ordinal))
        {
            StartFile(line);
        }
        else if (line.StartsWith(NewFileModePrefix, StringComparison.Ordinal))
        {
            _changeType = ChangeType.Added;
            _oldPath = null;
        }
        else if (line.StartsWith(DeletedFileModePrefix, StringComparison.Ordinal))
        {
            _changeType = ChangeType.Deleted;
        }
        else if (line.StartsWith(RenameFromPrefix, StringComparison.Ordinal))
        {
            _changeType = ChangeType.Renamed;
            _oldPath = QuotedPath.UnquoteOrKeep(line[RenameFromPrefix.Length..]);
        }
        else if (line.StartsWith(RenameToPrefix, StringComparison.Ordinal))
        {
            _path = QuotedPath.UnquoteOrKeep(line[RenameToPrefix.Length..]);
        }
        else if (line.StartsWith(CopyFromPrefix, StringComparison.Ordinal))
        {
            _changeType = ChangeType.Copied;
            _oldPath = QuotedPath.UnquoteOrKeep(line[CopyFromPrefix.Length..]);
        }
        else if (line.StartsWith(CopyToPrefix, StringComparison.Ordinal))
        {
            _path = QuotedPath.UnquoteOrKeep(line[CopyToPrefix.Length..]);
        }
        else if (line.StartsWith(BinaryFilesPrefix, StringComparison.Ordinal))
        {
            _isBinary = true;
        }
        else if (line.StartsWith(HunkHeaderPrefix, StringComparison.Ordinal))
        {
            StartHunk(line);
        }
        else if (IsHunkBodyLine(line))
        {
            _hunkLines.Add(line);
        }
    }

    /// <summary>
    /// Begin a new file entry from a <c>diff --git</c> header. Both paths come from <see cref="TrySplitHeader"/> without their
    /// <c>a/</c> and <c>b/</c> prefixes; a header it cannot read, or whose sides lack those prefixes, leaves the whole line
    /// as the path.
    /// </summary>
    /// <param name="line">The whole <c>diff --git</c> header line.</param>
    private void StartFile(string line)
    {
        FlushFile();

        if (TrySplitHeader(line[DiffHeaderPrefix.Length..], out var oldSide, out var newSide)
            && oldSide.StartsWith(OldSidePrefix, StringComparison.Ordinal)
            && newSide.StartsWith(NewSidePrefix, StringComparison.Ordinal))
        {
            _oldPath = oldSide[OldSidePrefix.Length..];
            _path = newSide[NewSidePrefix.Length..];
        }
        else
        {
            _path = line;
        }
        _changeType = ChangeType.Modified;
        _isBinary = false;
    }

    /// <summary>
    /// Split the text after <c>diff --git </c> into its old and new side, each still carrying its prefix. git writes each
    /// side as the prefix plus the path, C-quoted as one string when the path holds a double quote, a backslash, or a
    /// control character (see <see cref="QuotedPath"/>), and as it is otherwise, so an unquoted side never holds a double
    /// quote. With neither side quoted, the two sides name the same path on every entry but a rename or copy, which
    /// <see cref="TrySplitSamePath"/> splits exactly even when the path holds <c> b/</c>; a rename or copy splits at its
    /// first <c> b/</c>, and its <c>rename</c> or <c>copy</c> lines then name both paths.
    /// </summary>
    /// <param name="paths">The header text after <c>diff --git </c>.</param>
    /// <param name="oldSide">The old side, unquoted, with its prefix.</param>
    /// <param name="newSide">The new side, unquoted when it was a well-formed quoted path, with its prefix.</param>
    /// <returns><c>true</c> when the text splits into two sides.</returns>
    private static bool TrySplitHeader(string paths, out string oldSide, out string newSide)
    {
        oldSide = null;
        newSide = null;
        if (paths.StartsWith('"'))
        {
            if (!QuotedPath.TryUnquote(paths, out oldSide, out var end) || end >= paths.Length || paths[end] != ' ')
            {
                return false;
            }

            newSide = QuotedPath.UnquoteOrKeep(paths[(end + 1)..]);
            return true;
        }

        var quote = paths.IndexOf('"');
        if (quote >= 0)
        {
            if (paths[quote - 1] != ' ')
            {
                return false;
            }

            oldSide = paths[..(quote - 1)];
            newSide = QuotedPath.UnquoteOrKeep(paths[quote..]);
            return true;
        }

        if (TrySplitSamePath(paths, out oldSide, out newSide))
        {
            return true;
        }

        var split = paths.IndexOf(NewSideMarker, StringComparison.Ordinal);
        if (split < 0)
        {
            return false;
        }

        oldSide = paths[..split];
        newSide = paths[(split + 1)..];
        return true;
    }

    /// <summary>
    /// Split unquoted header text whose two sides name the same path: the old prefix, the path, a space, the new prefix, and
    /// the path again. Both prefixes are two characters long, so the sides are equally long and the space sits at the middle
    /// index; the split is then the only one that gives the same path twice.
    /// </summary>
    /// <param name="paths">The header text after <c>diff --git </c>, with no double quote in it.</param>
    /// <param name="oldSide">The old side with its prefix, or <c>null</c> when the sides do not name the same path.</param>
    /// <param name="newSide">The new side with its prefix, or <c>null</c> when the sides do not name the same path.</param>
    /// <returns><c>true</c> when both sides name the same path.</returns>
    private static bool TrySplitSamePath(string paths, out string oldSide, out string newSide)
    {
        oldSide = null;
        newSide = null;
        var middle = paths.Length / 2;
        if (paths.Length % 2 == 0 || middle <= OldSidePrefix.Length || paths[middle] != ' ')
        {
            return false;
        }

        var pathLength = middle - OldSidePrefix.Length;
        if (!paths.AsSpan(OldSidePrefix.Length, pathLength).SequenceEqual(paths.AsSpan(middle + 1 + NewSidePrefix.Length)))
        {
            return false;
        }

        oldSide = paths[..middle];
        newSide = paths[(middle + 1)..];
        return true;
    }

    /// <summary>Begin a new hunk from an <c>@@ ... @@</c> header line.</summary>
    private void StartHunk(string line)
    {
        FlushHunk();
        ParseHunkHeader(line, out var oldStart, out var oldLines, out var newStart, out var newLines);
        _hunkHeader = new DiffHunk
        {
            Header = line,
            OldStart = oldStart,
            OldLines = oldLines,
            NewStart = newStart,
            NewLines = newLines,
        };
        _hunkLines = [];
    }

    /// <summary>True when <paramref name="line"/> is a body line of the hunk currently being read.</summary>
    private bool IsHunkBodyLine(string line)
    {
        if (_hunkLines is null)
        {
            return false;
        }
        return line.Length == 0
               || line.StartsWith('+')
               || line.StartsWith('-')
               || line.StartsWith(' ');
    }

    /// <summary>Commit the in-progress hunk (header plus body lines) to the current file.</summary>
    private void FlushHunk()
    {
        if (_hunkHeader != null && _hunkLines != null)
        {
            _hunks.Add(_hunkHeader with { Lines = _hunkLines });
        }
        _hunkHeader = null;
        _hunkLines = null;
    }

    /// <summary>Commit the in-progress file (with its hunks and derived counts) to the result set.</summary>
    private void FlushFile()
    {
        FlushHunk();
        if (_path == null)
        {
            return;
        }

        var (additions, deletions) = CountChangedLines(_hunks);
        _files.Add(new FileDiff
        {
            Path = _path,
            ChangeType = _changeType,
            OldPath = _oldPath,
            Additions = additions,
            Deletions = deletions,
            IsBinary = _isBinary,
            Hunks = [.. _hunks],
        });

        ResetFileState();
    }

    /// <summary>Clear the per-file cursor so the next <c>diff --git</c> header starts clean.</summary>
    private void ResetFileState()
    {
        _path = null;
        _oldPath = null;
        _changeType = ChangeType.Modified;
        _isBinary = false;
        _hunks.Clear();
    }

    /// <summary>
    /// Count added/removed content lines across <paramref name="hunks"/>, excluding the
    /// <c>+++</c>/<c>---</c> file markers that share the same leading character.
    /// </summary>
    private static (int Additions, int Deletions) CountChangedLines(IReadOnlyList<DiffHunk> hunks)
    {
        var additions = 0;
        var deletions = 0;
        foreach (var hunk in hunks)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
                {
                    additions++;
                }
                else if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
                {
                    deletions++;
                }
            }
        }
        return (additions, deletions);
    }

    /// <summary>
    /// Parse an <c>@@ -oldStart,oldLines +newStart,newLines @@</c> header into its ranges.
    /// Malformed headers are tolerated and leave the out values at zero.
    /// </summary>
    private static void ParseHunkHeader(string line, out int oldStart, out int oldLines, out int newStart, out int newLines)
    {
        oldStart = newStart = 0;
        oldLines = newLines = 0;
        try
        {
            var spec = line.Split("@@", StringSplitOptions.None)[1].Trim();
            var space = spec.IndexOf(' ');
            if (space <= 0)
            {
                return;
            }
            var left = spec[..space].TrimStart('-');
            var right = spec[(space + 1)..].TrimStart('+');
            SplitRange(left, out oldStart, out oldLines);
            SplitRange(right, out newStart, out newLines);
        }
        catch
        {
            // Tolerated. Header malformed, leave zeros.
        }
    }

    /// <summary>Split a <c>start[,lines]</c> range spec, defaulting the line count to 1 when omitted.</summary>
    private static void SplitRange(string spec, out int start, out int lines)
    {
        var comma = spec.IndexOf(',');
        if (comma < 0)
        {
            start = int.TryParse(spec, out var s) ? s : 0;
            lines = 1;
        }
        else
        {
            start = int.TryParse(spec.AsSpan(0, comma), out var s) ? s : 0;
            lines = int.TryParse(spec.AsSpan(comma + 1), out var l) ? l : 0;
        }
    }
}