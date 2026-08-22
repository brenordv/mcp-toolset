namespace RaccoonNinja.McpToolset.Server.SkillStats.Errors;

/// <summary>
/// A domain error that travels inside a failure envelope, never through the MCP protocol error
/// channel. It carries a stable <see cref="Code"/> and a caller-safe message built from a fixed reason,
/// never from a caught .NET exception's message, so an absolute store path can never reach the client.
/// </summary>
public sealed class SkillStatsException : Exception
{
    /// <summary>Create the exception with a code and a caller-safe message.</summary>
    /// <param name="code">One of <see cref="ErrorCodes"/>.</param>
    /// <param name="message">A caller-facing message with no machine-identifying content.</param>
    public SkillStatsException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>The stable error code (see <see cref="ErrorCodes"/>).</summary>
    public string Code { get; }

    /// <summary>An out-of-range or malformed argument.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static SkillStatsException InvalidArgument(string reason)
        => new(ErrorCodes.InvalidArgument, reason);

    /// <summary>The store is absent, unreadable, or of an incompatible schema.</summary>
    /// <param name="reason">The plain reason.</param>
    /// <returns>The exception.</returns>
    public static SkillStatsException StoreUnavailable(string reason)
        => new(ErrorCodes.StoreUnavailable, reason);
}