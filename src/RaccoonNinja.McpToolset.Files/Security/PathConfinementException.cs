namespace RaccoonNinja.McpToolset.Files.Security;

/// <summary>
/// Raised when a candidate path cannot be confined to its root: it is syntactically hostile
/// (UNC, extended-length, device, drive-relative, or alternate-data-stream form), it is malformed,
/// or it resolves through symbolic links and junctions to a real location outside the root. The
/// confiner fails closed, so this exception means the path was refused and never opened.
/// </summary>
public sealed class PathConfinementException : Exception
{
    /// <summary>Create the exception for the parameter that carried the offending path.</summary>
    /// <param name="paramName">The name of the tool parameter the path arrived under.</param>
    /// <param name="reason">A short reason phrase completing <c>path under '{param}' {reason}</c>.</param>
    public PathConfinementException(string paramName, string reason)
        : base($"path under '{paramName}' {reason}")
    {
        ParamName = paramName;
        Reason = reason;
    }

    /// <summary>The tool parameter the offending path arrived under (for example <c>path</c> or <c>root</c>).</summary>
    public string ParamName { get; }

    /// <summary>The reason phrase describing why confinement failed.</summary>
    public string Reason { get; }
}