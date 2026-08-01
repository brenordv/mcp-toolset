namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>Thrown when the server cannot start: the root is unset, or a config override is invalid.</summary>
public sealed class SearchStartupException : Exception
{
    /// <summary>Create the exception with a caller-facing reason.</summary>
    /// <param name="message">Why startup failed.</param>
    public SearchStartupException(string message)
        : base(message)
    {
    }
}