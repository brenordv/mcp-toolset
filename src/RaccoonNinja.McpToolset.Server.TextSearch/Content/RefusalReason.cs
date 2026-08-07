namespace RaccoonNinja.McpToolset.Server.TextSearch.Content;

/// <summary>Maps a non-OK read outcome to a stable refusal-reason string for metrics and logs.</summary>
internal static class RefusalReason
{
    /// <summary>The refusal reason for a non-OK <see cref="ReadStatus"/>.</summary>
    /// <param name="status">The read status.</param>
    /// <returns>A stable reason slug.</returns>
    public static string From(ReadStatus status)
        => status switch
        {
            ReadStatus.OutOfRoot => "out_of_root",
            ReadStatus.Denied => "denylisted",
            ReadStatus.NotFound => "vanished",
            ReadStatus.IsDirectory => "not_a_file",
            ReadStatus.TooLarge => "too_large",
            ReadStatus.IoError => "io_error",
            ReadStatus.Ignored => "ignored",
            ReadStatus.SecretContent => "secret_content",
            _ => "unknown",
        };
}