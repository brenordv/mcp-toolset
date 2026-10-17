namespace RaccoonNinja.McpToolset.Server.GitOps.Models;

/// <summary>
/// One record of <c>git ls-tree -z -l</c> output: the entry's mode, object type (<c>blob</c>, <c>tree</c>,
/// <c>commit</c>), object name, size in bytes (<c>null</c> for non-blobs and unreadable objects), and path.
/// </summary>
public sealed record LsTreeEntry(string Mode, string ObjectType, string ObjectId, long? Size, string Path);