using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Protocol;

/// <summary>
/// End-to-end protocol tests of the argument-shape call-tool filter through a real
/// <see cref="McpClient"/> over stdio against the built <c>git-ops</c> server, exercised through
/// <c>git_status</c>: a missing required <c>cwd</c> is rejected before the tool runs, a wrong-typed
/// <c>cwd</c> is rewritten after the SDK binder fails, and a well-formed call passes through as a
/// normal success envelope. The filter is tool-agnostic, so one tool proves the mechanism.
/// </summary>
[Collection(nameof(GitRepoCollection))]
public sealed class ArgumentShapeFilterTests : IAsyncLifetime
{
    private static readonly int[] WrongTypedCwd = [1, 2];

    private readonly GitRepoFixture _repo;
    private McpClient _client;

    public ArgumentShapeFilterTests(GitRepoFixture repo)
    {
        _repo = repo;
    }

    public async ValueTask InitializeAsync()
    {
        var serverDll = Path.Combine(AppContext.BaseDirectory, "git-ops.dll");
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "gitops-argshape-e2e",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MCP_GITOPS_LOG_LEVEL"] = "warning",
            },
        });
        _client = await McpClient.CreateAsync(transport);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }
    }

    [Fact]
    public async Task MissingRequiredCwd_RejectedBeforeTheToolRuns()
    {
        // Arrange
        var arguments = new Dictionary<string, object>();

        // Act
        var result = await _client.CallToolAsync("git_status", arguments);

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.Contains("cwd", Strings(error.GetProperty("detail").GetProperty("missing_required")));
    }

    [Fact]
    public async Task WrongTypedCwd_RewrittenWithSdkErrorInDetail()
    {
        // Arrange
        var arguments = new Dictionary<string, object> { ["cwd"] = WrongTypedCwd };

        // Act
        var result = await _client.CallToolAsync("git_status", arguments);

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("detail").TryGetProperty("sdk_error", out _));
    }

    [Fact]
    public async Task WellFormedStatus_PassesThroughAsSuccessEnvelope()
    {
        // Arrange
        var arguments = new Dictionary<string, object> { ["cwd"] = _repo.RepoPath };

        // Act
        var result = await _client.CallToolAsync("git_status", arguments);

        // Assert
        Assert.False(result.IsError ?? false);
        var envelope = Envelope(result);
        Assert.False(envelope.TryGetProperty("error", out _));
        Assert.True(envelope.GetProperty("results").GetArrayLength() >= 1);
    }

    private static JsonElement Envelope(CallToolResult result)
        => JsonSerializer.Deserialize<JsonElement>(result.Content.OfType<TextContentBlock>().Single().Text);

    private static string[] Strings(JsonElement array)
        => [.. array.EnumerateArray().Select(item => item.GetString())];
}