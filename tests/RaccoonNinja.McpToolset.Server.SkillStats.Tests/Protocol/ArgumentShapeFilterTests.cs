using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Protocol;

/// <summary>
/// End-to-end protocol tests of the argument-shape call-tool filter through a real
/// <see cref="McpClient"/> over stdio against the built <c>skill-stats</c> server. This server's error
/// contract is code + message only, so the filter's diagnostics ride in the message: an unknown name is
/// named with its suggestion, a wrong-typed argument is rewritten, and a genuine domain error (a missing
/// store) still surfaces as its own code. The store home points at an empty directory, so a well-formed
/// call reaches the tool and returns <c>store_unavailable</c>, proving the filter leaves tool results
/// alone.
/// </summary>
public sealed class ArgumentShapeFilterTests : IAsyncLifetime
{
    private static readonly int[] WrongTypedDaysBack = [1, 2];

    private string _home;
    private McpClient _client;

    public async ValueTask InitializeAsync()
    {
        _home = Path.Combine(Path.GetTempPath(), "skillstats-argshape", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);

        var serverDll = Path.Combine(AppContext.BaseDirectory, "skill-stats-mcp.dll");
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "skillstats-argshape-e2e",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["SKILL_STATS_HOME"] = _home,
                ["MCP_SKILLSTATS_LOG_LEVEL"] = "warning",
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

        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; the child server may still be releasing a handle.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows reports a lingering handle this way.
        }
    }

    [Fact]
    public async Task UnknownArgument_RewrittenWithSuggestionInMessage()
    {
        // Act
        var result = await _client.CallToolAsync("top_skills", new Dictionary<string, object> { ["limitt"] = 5 });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = ParseText(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.Contains("did you mean 'limit'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongTypedArgument_RewrittenAsInvalidArgument()
    {
        // Act
        var result = await _client.CallToolAsync("top_skills", new Dictionary<string, object> { ["days_back"] = WrongTypedDaysBack });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = ParseText(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.Contains("valid argument names are:", error.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenuineDomainError_MissingStore_StaysStoreUnavailable()
    {
        // Act
        var result = await _client.CallToolAsync("top_skills", new Dictionary<string, object> { ["limit"] = 5 });

        // Assert
        Assert.False(result.IsError ?? false);
        var error = ToJson(result.StructuredContent).GetProperty("error");
        Assert.Equal("StoreUnavailable", error.GetProperty("code").GetString());
    }

    private static JsonElement ParseText(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        Assert.True(start >= 0, $"the error text must carry the JSON error body, but was: '{text}'");
        return JsonSerializer.Deserialize<JsonElement>(text[start..]);
    }

    private static JsonElement ToJson(object value)
        => JsonSerializer.SerializeToElement(value);
}