namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

/// <summary>
/// Mutable container so a failure envelope produced by the tool wrapper can
/// surface a repo root that was resolved *inside* the wrapped block.
/// </summary>
public sealed class RootHolder
{
    public string Value { get; private set; }

    public void Set(string root)
    {
        Value = root;
    }
}