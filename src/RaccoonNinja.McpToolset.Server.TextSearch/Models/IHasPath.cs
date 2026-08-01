namespace RaccoonNinja.McpToolset.Server.TextSearch.Models;

/// <summary>A result item that exposes its scope-relative path, so callers read it directly instead of via reflection.</summary>
public interface IHasPath
{
    /// <summary>The <c>/</c>-separated path relative to the call's scope (the <c>cwd</c>, or the base root when it is omitted).</summary>
    string Path { get; }
}