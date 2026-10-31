using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>A per-file diff entry inside a <see cref="DiffResult"/>.</summary>
public sealed record FileDiff
{
    [JsonPropertyName("path")]
    public string Path { get; init; }

    [JsonPropertyName("change_type")]
    public ChangeType ChangeType { get; init; } = ChangeType.Modified;

    [JsonPropertyName("old_path")]
    public string OldPath { get; init; }

    /// <summary>
    /// The file mode before the change, as git prints it in the patch header: six octal digits, such as <c>100644</c>
    /// for a regular file, <c>100755</c> for an executable one, <c>120000</c> for a symlink, or <c>160000</c> for a
    /// submodule. <c>null</c> for an added file, and when the patch states no mode for the entry (a rename or copy whose
    /// content and mode are both unchanged, or an entry built from the <c>--numstat</c> pass alone).
    /// </summary>
    [JsonPropertyName("old_mode")]
    public string OldMode { get; init; }

    /// <summary>
    /// The file mode after the change, in the same form as <see cref="OldMode"/>. <c>null</c> for a deleted file, and
    /// when the patch states no mode for the entry.
    /// </summary>
    [JsonPropertyName("new_mode")]
    public string NewMode { get; init; }

    [JsonPropertyName("additions")]
    public int Additions { get; init; }

    [JsonPropertyName("deletions")]
    public int Deletions { get; init; }

    [JsonPropertyName("is_binary")]
    public bool IsBinary { get; init; }

    [JsonPropertyName("hunks")]
    public IReadOnlyList<DiffHunk> Hunks { get; init; } = [];
}