using RaccoonNinja.McpToolset.Server.GitOps.Models;

namespace RaccoonNinja.McpToolset.Server.GitOps.Parsers;

/// <summary>Parser for <c>git status --porcelain=v2 -z --branch --ahead-behind</c>.</summary>
public static class StatusParser
{
    /// <summary>Parse porcelain v2 -z output. Spec: https://git-scm.com/docs/git-status#_porcelain_format_version_2</summary>
    public static StatusResult Parse(byte[] raw)
    {
        var tokens = TextDecoding.Decode(raw).Split('\0');
        var state = new ParseState();

        var i = 0;
        while (i < tokens.Length)
        {
            var line = tokens[i++];
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("# ", StringComparison.Ordinal))
                ApplyBranchHeader(line[2..], state);
            else
                ApplyEntry(line, tokens, ref i, state);
        }

        return BuildResult(state);
    }

    /// <summary>Apply a <c>branch.*</c> header (head, upstream, or ahead/behind) to <paramref name="state"/>.</summary>
    private static void ApplyBranchHeader(string header, ParseState state)
    {
        if (header.StartsWith("branch.head ", StringComparison.Ordinal))
        {
            var value = header["branch.head ".Length..];
            state.Branch = value == "(detached)" ? null : value;
        }
        else if (header.StartsWith("branch.upstream ", StringComparison.Ordinal))
        {
            state.Upstream = header["branch.upstream ".Length..];
        }
        else if (header.StartsWith("branch.ab ", StringComparison.Ordinal))
        {
            ApplyAheadBehind(header["branch.ab ".Length..], state);
        }
    }

    /// <summary>Parse the <c>+N -M</c> payload of a <c>branch.ab</c> header into ahead/behind counts.</summary>
    private static void ApplyAheadBehind(string value, ParseState state)
    {
        var parts = value.Split(' ');
        if (parts.Length != 2) return;
        _ = int.TryParse(parts[0].TrimStart('+'), out var ahead);
        _ = int.TryParse(parts[1].TrimStart('-'), out var behind);
        state.Ahead = ahead;
        state.Behind = behind;
    }

    /// <summary>
    /// Dispatch a working-tree entry line by its leading kind code. The rename case (<c>2</c>)
    /// consumes a trailing token for the original path, so the token cursor is passed by ref.
    /// </summary>
    private static void ApplyEntry(string line, string[] tokens, ref int i, ParseState state)
    {
        switch (line[0])
        {
            case '?':
                state.Untracked.Add(ParseUntracked(line));
                break;
            case '1':
                AddTrackedChange(line, fieldCount: 9, pathIndex: 8, origPath: null, state);
                break;
            case '2':
                var origPath = i < tokens.Length ? tokens[i++] : string.Empty;
                AddTrackedChange(line, fieldCount: 10, pathIndex: 9, origPath, state);
                break;
            case 'u':
                AddUnmergedChange(line, state);
                break;
        }
    }

    /// <summary>Build an untracked entry from a <c>? &lt;path&gt;</c> line.</summary>
    private static FileStatus ParseUntracked(string line) => new()
    {
        Path = line.Length > 2 ? line[2..] : string.Empty,
        StagedStatus = "?",
        UnstagedStatus = "?",
        IsUntracked = true,
    };

    /// <summary>
    /// Parse an ordinary (<c>1</c>) or renamed/copied (<c>2</c>) change line and classify it into
    /// the staged and/or unstaged buckets based on its XY status field. A non-null
    /// <paramref name="origPath"/> marks the entry as a rename.
    /// </summary>
    private static void AddTrackedChange(string line, int fieldCount, int pathIndex, string origPath, ParseState state)
    {
        var parts = line.Split(' ', fieldCount);
        if (parts.Length < fieldCount) return;

        var xy = parts[1];
        var entry = new FileStatus
        {
            Path = parts[pathIndex],
            StagedStatus = xy[0].ToString(),
            UnstagedStatus = xy[1].ToString(),
            IsRenamed = origPath != null,
            OrigPath = string.IsNullOrWhiteSpace(origPath) ? null : origPath,
        };
        Classify(entry, xy[0], xy[1], state);
    }

    /// <summary>Parse an unmerged (<c>u</c>) change line; such entries are always reported as unstaged.</summary>
    private static void AddUnmergedChange(string line, ParseState state)
    {
        var parts = line.Split(' ', 11);
        if (parts.Length < 11) return;

        var xy = parts[1];
        state.Unstaged.Add(new FileStatus
        {
            Path = parts[10],
            StagedStatus = xy[0].ToString(),
            UnstagedStatus = xy[1].ToString(),
        });
    }

    /// <summary>Add <paramref name="entry"/> to the staged and/or unstaged buckets per its XY status codes.</summary>
    private static void Classify(FileStatus entry, char stagedCode, char unstagedCode, ParseState state)
    {
        if (IsChanged(stagedCode)) state.Staged.Add(entry);
        if (IsChanged(unstagedCode)) state.Unstaged.Add(entry);
    }

    /// <summary>A porcelain status code denotes a change unless it is unmodified (<c>.</c>) or blank.</summary>
    private static bool IsChanged(char code) => code != '.' && code != ' ';

    /// <summary>Assemble the final immutable result from accumulated parse state.</summary>
    private static StatusResult BuildResult(ParseState state) => new()
    {
        Branch = state.Branch,
        Upstream = state.Upstream,
        Ahead = state.Ahead,
        Behind = state.Behind,
        Staged = state.Staged,
        Unstaged = state.Unstaged,
        Untracked = state.Untracked,
        IsClean = state.Staged.Count == 0 && state.Unstaged.Count == 0 && state.Untracked.Count == 0,
    };

    /// <summary>Mutable scratch state accumulated while scanning the token stream.</summary>
    private sealed class ParseState
    {
        public string Branch;
        public string Upstream;
        public int Ahead;
        public int Behind;
        public List<FileStatus> Staged { get; } = [];
        public List<FileStatus> Unstaged { get; } = [];
        public List<FileStatus> Untracked { get; } = [];
    }
}