namespace RaccoonNinja.McpToolset.Common.Mcp.Logging;

/// <summary>
/// Renders an exception as its type chain, outer to inner, for a log line. A type's name is chosen by the code that
/// defines the type, never by request data, so unlike a message or a stack trace it cannot carry an absolute path, a
/// secret, or user content.
/// </summary>
public static class ExceptionTypeChain
{
    /// <summary>The most exception types <see cref="Render"/> lists before it stops.</summary>
    public const int MaxDepth = 8;

    /// <summary>Render an exception as its type chain, outer to inner.</summary>
    /// <param name="exception">The logged exception.</param>
    /// <returns>
    /// The full type names joined by <c> -> </c>, with <c> -> ...</c> appended past <see cref="MaxDepth"/> entries.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <c>null</c>.</exception>
    public static string Render(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var names = new List<string>();
        var current = exception;
        while (current is not null && names.Count < MaxDepth)
        {
            names.Add(current.GetType().FullName);
            current = current.InnerException;
        }

        var chain = string.Join(" -> ", names);
        return current is null ? chain : chain + " -> ...";
    }
}