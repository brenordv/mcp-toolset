using System.Text.Json.Serialization;

namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>One tracked file's content and index metadata as of a ref, returned by <c>git_file_at_ref</c>.</summary>
public sealed record FileAtRef
{
    /// <summary>The confined repo-relative POSIX path.</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; }

    /// <summary>The commit-ish the caller named, resolved to an object name.</summary>
    [JsonPropertyName("ref_sha")]
    public string RefSha { get; init; }

    /// <summary>The six octal mode digits (<c>100644</c>, <c>100755</c>, or <c>120000</c>, whose content is the symlink target text).</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; }

    /// <summary>The blob's object name.</summary>
    [JsonPropertyName("object_id")]
    public string ObjectId { get; init; }

    /// <summary>The blob size in bytes from <c>ls-tree -l</c>; <c>null</c> only when git could not read the object.</summary>
    [JsonPropertyName("size")]
    public long? Size { get; init; }

    /// <summary>The line count of the whole blob (a lower bound when the output cap truncated it); <c>null</c> when <see cref="IsBinary"/> is <c>true</c>.</summary>
    [JsonPropertyName("total_lines")]
    public int? TotalLines { get; init; }

    /// <summary>Where to continue when <c>maxBytes</c> cut the content: the offset of the first byte left out, to pass back as <c>byteOffset</c>; <c>null</c> when nothing was cut or the blob is binary.</summary>
    [JsonPropertyName("next_byte_offset")]
    public int? NextByteOffset { get; init; }

    /// <summary>Whether the content has a NUL byte in its first 8000 bytes (git's binary heuristic).</summary>
    [JsonPropertyName("is_binary")]
    public bool IsBinary { get; init; }

    /// <summary>The content decoded as UTF-8; <c>null</c> when <see cref="IsBinary"/> is <c>true</c>.</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; }
}