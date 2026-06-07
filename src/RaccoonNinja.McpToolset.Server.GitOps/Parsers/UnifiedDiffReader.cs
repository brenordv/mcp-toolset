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
    private const string CopyFromPrefix = "copy from ";
    private const string BinaryFilesPrefix = "Binary files";
    private const string HunkHeaderPrefix = "@@";
    private const string NewPathMarker = " b/";
    private const string OldPathMarker = "a/";

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
            _oldPath = line[RenameFromPrefix.Length..];
        }
        else if (line.StartsWith(CopyFromPrefix, StringComparison.Ordinal))
        {
            _changeType = ChangeType.Copied;
            _oldPath = line[CopyFromPrefix.Length..];
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

    /// <summary>Begin a new file entry from a <c>diff --git a/&lt;old&gt; b/&lt;new&gt;</c> header.</summary>
    private void StartFile(string line)
    {
        FlushFile();

        var paths = line[DiffHeaderPrefix.Length..];
        var split = paths.IndexOf(NewPathMarker, StringComparison.Ordinal);
        if (split > 0)
        {
            var oldSegment = paths[..split];
            _oldPath = oldSegment.StartsWith(OldPathMarker, StringComparison.Ordinal)
                ? oldSegment[OldPathMarker.Length..]
                : oldSegment;
            _path = paths[(split + NewPathMarker.Length)..];
        }
        else
        {
            _path = line;
        }
        _changeType = ChangeType.Modified;
        _isBinary = false;
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