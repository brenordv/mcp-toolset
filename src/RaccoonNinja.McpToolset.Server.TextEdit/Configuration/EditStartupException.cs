namespace RaccoonNinja.McpToolset.Server.TextEdit.Configuration;

/// <summary>
/// Thrown when the server cannot start: the root is unset or is not exactly one entry, a config override
/// is invalid, the journal directory would sit inside the root, or journal hardening failed. Every one of
/// these is a fatal, fail-loud condition rather than a silently degraded start.
/// </summary>
public sealed class EditStartupException : Exception
{
    /// <summary>Create the exception with a caller-facing reason.</summary>
    /// <param name="message">Why startup failed.</param>
    public EditStartupException(string message)
        : base(message)
    {
    }
}