using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Configuration;

public sealed class RootRegistryTests : IDisposable
{
    private static readonly ISecretDenylist Denylist = new SecretDenylist();
    private static readonly SearchConfig Config = new() { MaxFileBytes = SearchConfig.DefaultMaxFileBytes };

    private readonly List<string> _cleanup = [];

    [Fact]
    public void Create_NoWorkspaceRoot_FailsFast()
        => Assert.Throws<SearchStartupException>(() => RootRegistry.Create(Config, Denylist, "", null));

    [Fact]
    public void Create_NonexistentRoot_FailsFast()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"rnmcp-missing-{Guid.NewGuid():N}");
        Assert.Throws<SearchStartupException>(() => RootRegistry.Create(Config, Denylist, missing, null));
    }

    [Fact]
    public void Create_OverlappingRoots_FailsFast()
    {
        var parent = NewTempDirectory("parent");
        var child = Path.Combine(parent, "nested");
        Directory.CreateDirectory(child);

        Assert.Throws<SearchStartupException>(() =>
            RootRegistry.Create(Config, Denylist, string.Join(';', parent, child), null));
    }

    [Fact]
    public void Create_ReservedName_FailsFast()
    {
        var dir = NewTempDirectory("reserved");
        Assert.Throws<SearchStartupException>(() => RootRegistry.Create(Config, Denylist, $"@bad={dir}", null));
    }

    [Fact]
    public void Create_DuplicateAlias_FailsFast()
    {
        var first = NewTempDirectory("dup1");
        var second = NewTempDirectory("dup2");
        Assert.Throws<SearchStartupException>(() =>
            RootRegistry.Create(Config, Denylist, $"x={first};x={second}", null));
    }

    [Fact]
    public void Create_Aliases_PreserveNamesAndKinds()
    {
        var workspace = NewTempDirectory("ws");
        var package = NewTempDirectory("pkg");

        var registry = RootRegistry.Create(Config, Denylist, $"app={workspace}", $"cargo={package}");

        Assert.Contains(registry.All, root => root is { Name: "app", Kind: RootKind.Workspace });
        Assert.Contains(registry.All, root => root is { Name: "cargo", Kind: RootKind.Package });
    }

    [Fact]
    public void Create_DerivedNameCollision_IsDisambiguated()
    {
        var first = Path.Combine(NewTempDirectory("a"), "src");
        var second = Path.Combine(NewTempDirectory("b"), "src");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        var registry = RootRegistry.Create(Config, Denylist, string.Join(';', first, second), null);

        var names = registry.All.Select(root => root.Name).ToArray();
        Assert.Contains("src", names);
        Assert.Contains("src-2", names);
    }

    [Fact]
    public void Resolve_UnknownRoot_ThrowsInvalidArgument()
    {
        var dir = NewTempDirectory("only");
        var registry = RootRegistry.Create(Config, Denylist, $"app={dir}", null);

        var exception = Assert.Throws<TextSearchException>(() => registry.Resolve("nope"));
        Assert.Equal(ErrorCodes.InvalidArgument, exception.Code);
    }

    public void Dispose()
    {
        foreach (var dir in _cleanup)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private string NewTempDirectory(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"rnmcp-reg-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _cleanup.Add(dir);
        return dir;
    }
}