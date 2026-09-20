namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>One windowed snippet from a note body: the matched term and the surrounding text.</summary>
public sealed record ContentSnippet
{
    /// <summary>The term this snippet was built around.</summary>
    public string Term { get; init; }

    /// <summary>The windowed body text, with <c>…</c> markers where it was cut.</summary>
    public string Text { get; init; }
}