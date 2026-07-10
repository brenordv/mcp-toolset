namespace RaccoonNinja.McpToolset.Server.FileVault.Exceptions;

/// <summary>
/// A fatal startup failure: invalid configuration, an unusable store directory, or a failed
/// migration. Thrown before the MCP transport starts; the process logs it and exits nonzero.
/// </summary>
public sealed class VaultStartupException : Exception
{
    /// <summary>Create the exception with a human-readable reason.</summary>
    /// <param name="message">What made startup impossible.</param>
    public VaultStartupException(string message)
        : base(message)
    {
    }

    /// <summary>Create the exception wrapping an underlying failure.</summary>
    /// <param name="message">What made startup impossible.</param>
    /// <param name="innerException">The underlying failure.</param>
    public VaultStartupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}