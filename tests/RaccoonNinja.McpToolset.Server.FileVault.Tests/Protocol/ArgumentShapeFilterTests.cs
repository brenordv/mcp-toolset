using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Protocol;

/// <summary>
/// End-to-end protocol tests of the argument-shape call-tool filter through a real
/// <see cref="McpClient"/> over stdio against the built <c>file-vault</c> server: a wrong-typed
/// argument and an unknown/missing name become a structured <c>invalid_argument</c> body, while a
/// genuine domain error (a stale-base <c>conflict</c>) still surfaces as its own code, proving the
/// rewrite gate never clobbers a real domain result.
/// </summary>
public sealed class ArgumentShapeFilterTests : IAsyncLifetime
{
    private static readonly string[] WellFormedTags = ["a"];

    private string _home;
    private McpClient _client;

    public async ValueTask InitializeAsync()
    {
        _home = Path.Combine(Path.GetTempPath(), "filevault-argshape", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);

        var serverDll = Path.Combine(AppContext.BaseDirectory, "file-vault.dll");
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "filevault-argshape-e2e",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["VAULT_MCP_HOME"] = _home,
                ["VAULT_MCP_PROJECT"] = string.Empty,
                ["VAULT_MCP_LOG"] = "warning",
                ["VAULT_MCP_LOG_FILE"] = string.Empty,
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

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup; the child server may still be releasing the store.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows reports a lingering -shm handle this way.
        }
    }

    [Fact]
    public async Task TagsAsString_WrongJsonType_RewrittenAsInvalidArgument()
    {
        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "str-tags",
            ["content"] = "body",
            ["summary"] = "s",
            ["project"] = "argshape",
            ["tags"] = "a, b, c",
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = ParseErrorBody(result).GetProperty("error");
        Assert.Equal("invalid_argument", error.GetProperty("code").GetString());
        Assert.True(error.TryGetProperty("expected_arguments", out _));
        Assert.True(error.TryGetProperty("sdk_error", out _));
    }

    [Fact]
    public async Task UnknownArgument_RejectedWithSuggestion()
    {
        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "unknown-arg",
            ["content"] = "body",
            ["summary"] = "s",
            ["project"] = "argshape",
            ["tag"] = "x",
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = ParseErrorBody(result).GetProperty("error");
        Assert.Equal("invalid_argument", error.GetProperty("code").GetString());
        Assert.Contains("tags", Suggestions(error.GetProperty("unknown_arguments")));
    }

    [Fact]
    public async Task MissingRequiredName_RejectedBeforeTheToolRuns()
    {
        // Act
        var result = await _client.CallToolAsync("vault_get", new Dictionary<string, object>
        {
            ["project"] = "argshape",
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = ParseErrorBody(result).GetProperty("error");
        Assert.Equal("invalid_argument", error.GetProperty("code").GetString());
        Assert.Contains("name", Strings(error.GetProperty("missing_required")));
    }

    [Fact]
    public async Task GenuineDomainError_StaleBaseConflict_StaysConflictNotInvalidArgument()
    {
        // Arrange
        await SaveOkAsync("conflict-guard", "line one", baseVersion: null);
        await SaveOkAsync("conflict-guard", "line two", baseVersion: 1);

        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "conflict-guard",
            ["content"] = "line stale",
            ["summary"] = "s",
            ["project"] = "argshape",
            ["base_version"] = 1,
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = ParseErrorBody(result).GetProperty("error");
        Assert.Equal("conflict", error.GetProperty("code").GetString());
        Assert.Equal(2, error.GetProperty("current_version").GetInt32());
    }

    [Fact]
    public async Task WellFormedSave_PassesThroughUntouched()
    {
        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "well-formed",
            ["content"] = "body",
            ["summary"] = "s",
            ["project"] = "argshape",
            ["tags"] = WellFormedTags,
        });

        // Assert
        Assert.False(result.IsError ?? false);
        Assert.Equal(1, ToJson(result.StructuredContent).GetProperty("version").GetInt32());
    }

    private async Task SaveOkAsync(string name, string content, int? baseVersion)
    {
        var arguments = new Dictionary<string, object>
        {
            ["name"] = name,
            ["content"] = content,
            ["summary"] = "s",
            ["project"] = "argshape",
        };
        if (baseVersion is int version)
        {
            arguments["base_version"] = version;
        }

        var result = await _client.CallToolAsync("vault_save", arguments);
        Assert.False(result.IsError ?? false);
    }

    private static JsonElement ToJson(object value)
        => JsonSerializer.SerializeToElement(value);

    private static string[] Strings(JsonElement array)
        => [.. array.EnumerateArray().Select(item => item.GetString())];

    private static string[] Suggestions(JsonElement unknownArguments)
        => [.. unknownArguments.EnumerateArray()
            .Where(item => item.TryGetProperty("did_you_mean", out _))
            .Select(item => item.GetProperty("did_you_mean").GetString())];

    private static JsonElement ParseErrorBody(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        Assert.True(start >= 0, $"the error text must carry the JSON error body, but was: '{text}'");
        return JsonSerializer.Deserialize<JsonElement>(text[start..]);
    }
}