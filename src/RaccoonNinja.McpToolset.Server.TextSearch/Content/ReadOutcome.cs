namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>
/// The typed result of a gated content read. The absolute real path never appears here; only the
/// root-relative path and, on success, the bytes. Callers branch on <see cref="Status"/> rather than
/// catching filesystem exceptions, so a .NET exception message carrying an absolute path can never
/// escape the reader.
/// </summary>
public sealed record ReadOutcome
{
    /// <summary>The outcome category.</summary>
    public ReadStatus Status { get; private init; }

    /// <summary>The file bytes on <see cref="ReadStatus.Ok"/>; otherwise <c>null</c>.</summary>
    public byte[] Bytes { get; private init; }

    /// <summary>The <c>/</c>-separated root-relative path, when one was resolved; otherwise <c>null</c>.</summary>
    public string RelativePath { get; private init; }

    /// <summary>The file size in bytes, populated on <see cref="ReadStatus.TooLarge"/>.</summary>
    public long Size { get; private init; }

    /// <summary>Whether the read succeeded.</summary>
    public bool IsOk => Status == ReadStatus.Ok;

    /// <summary>A successful read.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <param name="relativePath">The root-relative path.</param>
    /// <returns>The outcome.</returns>
    public static ReadOutcome Ok(byte[] bytes, string relativePath)
        => new() { Status = ReadStatus.Ok, Bytes = bytes, RelativePath = relativePath };

    /// <summary>A path that resolved outside the root.</summary>
    /// <returns>The outcome.</returns>
    public static ReadOutcome OutOfRoot() => new() { Status = ReadStatus.OutOfRoot };

    /// <summary>A denylisted path.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <returns>The outcome.</returns>
    public static ReadOutcome Denied(string relativePath)
        => new() { Status = ReadStatus.Denied, RelativePath = relativePath };

    /// <summary>A missing path.</summary>
    /// <returns>The outcome.</returns>
    public static ReadOutcome NotFound() => new() { Status = ReadStatus.NotFound };

    /// <summary>A path that resolved to a directory.</summary>
    /// <returns>The outcome.</returns>
    public static ReadOutcome IsDirectory() => new() { Status = ReadStatus.IsDirectory };

    /// <summary>A file over the size cap.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <param name="size">The actual size in bytes.</param>
    /// <returns>The outcome.</returns>
    public static ReadOutcome TooLarge(string relativePath, long size)
        => new() { Status = ReadStatus.TooLarge, RelativePath = relativePath, Size = size };

    /// <summary>An I/O failure opening or reading the file.</summary>
    /// <returns>The outcome.</returns>
    public static ReadOutcome IoError() => new() { Status = ReadStatus.IoError };

    /// <summary>A path ignored by a <c>.gitignore</c>/<c>.mcpignore</c> rule.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <returns>The outcome.</returns>
    public static ReadOutcome Ignored(string relativePath)
        => new() { Status = ReadStatus.Ignored, RelativePath = relativePath };

    /// <summary>A file whose content matched a secret detector and is withheld.</summary>
    /// <param name="relativePath">The root-relative path.</param>
    /// <returns>The outcome.</returns>
    public static ReadOutcome SecretContent(string relativePath)
        => new() { Status = ReadStatus.SecretContent, RelativePath = relativePath };
}