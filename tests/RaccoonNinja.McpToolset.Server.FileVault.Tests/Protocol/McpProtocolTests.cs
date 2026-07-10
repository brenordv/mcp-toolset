using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RaccoonNinja.McpToolset.Server.FileVault.Tests.Protocol;

/// <summary>
/// End-to-end protocol tests through a real <see cref="McpClient"/> over stdio against the built
/// server: handshake identity, snake_case tool schemas, structured content with explicit nulls,
/// the structured domain-error body, dynamic resources, and both prompts' exact Rust-parity texts.
/// </summary>
public sealed class McpProtocolTests : IAsyncLifetime
{
    private const string ExpectedInstructions =
        "A personal, cross-conversation file vault. Save content by name with a one-line "
        + "summary; retrieve, list, version, and edit it from any chat. Writes use optimistic "
        + "concurrency: pass the `base_version` you read, and re-read on a conflict. To change "
        + "only a note's summary, tags, or parent, use `vault_set_meta`; you never need to "
        + "resend content. Notes can be organized hierarchically: link a child note under a "
        + "main note via `parent`, and `vault_get` returns a note's parent and children so you "
        + "can split a large note into smaller related ones.";

    private static readonly string[] ListShapeTags = ["tag-b", "tag-a"];

    private static readonly string[] ExpectedToolNames =
    [
        "vault_save", "vault_set_meta", "vault_get", "vault_list", "vault_append",
        "vault_edit_section", "vault_edit_key", "vault_history", "vault_archive",
        "vault_restore", "vault_purge",
    ];

    private static readonly string[] VaultSaveSchemaProperties =
        ["name", "content", "summary", "base_version", "project", "tags", "format", "parent"];

    private static readonly string[] VaultSetMetaSchemaProperties =
        ["name", "project", "summary", "tags", "parent", "clear_parent", "base_version"];

    private static readonly string[] VaultEditKeySchemaProperties =
        ["name", "key_path", "value", "project", "base_version"];

    private static readonly string[] VaultGetSchemaProperties = ["name", "project", "version"];

    private string _home;
    private McpClient _client;

    public async ValueTask InitializeAsync()
    {
        _home = Path.Combine(Path.GetTempPath(), "filevault-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);

        var serverDll = Path.Combine(AppContext.BaseDirectory, "file-vault.dll");
        // The server assembly is copied next to the tests by the project reference.
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "filevault-e2e",
            // Every VAULT_MCP_* variable is pinned: the transport inherits the process
            // environment, and VaultConfigTests mutates these same variables in parallel.
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["VAULT_MCP_HOME"] = _home,
                ["VAULT_MCP_PROJECT"] = string.Empty,
                ["VAULT_MCP_LOG"] = "warning",
                ["VAULT_MCP_LOG_FILE"] = string.Empty,
                ["VAULT_MCP_MAX_BYTES"] = string.Empty,
                ["VAULT_MCP_BUSY_TIMEOUT_MS"] = string.Empty,
                ["VAULT_MCP_SPLIT_HINT_CHARS"] = string.Empty,
                ["VAULT_MCP_PURGE_DELETE_FILES"] = string.Empty,
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
            // Same best-effort contract (Windows reports a lingering -shm handle this way).
        }
    }

    [Fact]
    public void Handshake_ServerIdentity_IsVaultMcpWithVerbatimInstructions()
    {
        // Assert
        Assert.Equal("vault-mcp", _client.ServerInfo.Name);
        Assert.Equal(ExpectedInstructions, _client.ServerInstructions);
    }

    [Fact]
    public async Task ToolsList_ExposesAllElevenToolsWithSnakeCaseSchemas()
    {
        // Act
        var tools = await _client.ListToolsAsync();

        // Assert
        Assert.Equivalent(ExpectedToolNames, tools.Select(t => t.Name), strict: true);

        // The input schema property names come verbatim from the Rust wire contract.
        Assert.Equivalent(
            VaultSaveSchemaProperties,
            SchemaProperties(tools.Single(t => t.Name == "vault_save")), strict: true);
        Assert.Equivalent(
            VaultSetMetaSchemaProperties,
            SchemaProperties(tools.Single(t => t.Name == "vault_set_meta")), strict: true);
        Assert.Equivalent(
            VaultEditKeySchemaProperties,
            SchemaProperties(tools.Single(t => t.Name == "vault_edit_key")), strict: true);
        Assert.Equivalent(
            VaultGetSchemaProperties,
            SchemaProperties(tools.Single(t => t.Name == "vault_get")), strict: true);
    }

    [Fact]
    public async Task VaultSave_ReturnsStructuredContentWithVersionAndHash()
    {
        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "save-shape",
            ["content"] = "hello wire",
            ["summary"] = "wire-shape check",
            ["project"] = "e2e",
        });

        // Assert
        Assert.False(result.IsError ?? false);
        var body = ToJson(result.StructuredContent);
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        Assert.Matches("^[0-9a-f]{64}$", body.GetProperty("content_hash").GetString());

        Assert.False(body.TryGetProperty("hint", out _));

        Assert.True(File.Exists(Path.Combine(_home, "vault.db")));
    }

    [Fact]
    public async Task VaultSave_ContentOverSplitThreshold_CarriesHintOnTheWire()
    {
        // Arrange
        var content = new string('x', 14_001);

        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "split-hint-shape",
            ["content"] = content,
            ["summary"] = "split-hint wire check",
            ["project"] = "e2e",
        });

        // Assert
        Assert.False(result.IsError ?? false);
        var body = ToJson(result.StructuredContent);
        Assert.Equal(
            "content is 14001 chars; consider keeping this note as a summary + index "
            + "and moving detail into child notes linked via parent",
            body.GetProperty("hint").GetString());
    }

    [Fact]
    public async Task VaultAppend_GrowsNotePastSplitThreshold_CarriesHintOnTheWire()
    {
        // Arrange
        await Save("split-hint-append", new string('x', 14_000), baseVersion: null);

        // Act
        var result = await _client.CallToolAsync("vault_append", new Dictionary<string, object>
        {
            ["name"] = "split-hint-append",
            ["content"] = "x",
            ["base_version"] = 1,
            ["project"] = "e2e",
        });

        // Assert
        Assert.False(result.IsError ?? false);
        Assert.StartsWith("content is 14001 chars", ToJson(result.StructuredContent).GetProperty("hint").GetString());
    }

    [Fact]
    public async Task ArchiveRestore_RoundTrip_ReturnsStatusMessages()
    {
        // Arrange
        await Save("lifecycle-note", "body", baseVersion: null);

        // Act
        var archived = await _client.CallToolAsync("vault_archive", new Dictionary<string, object>
        {
            ["name"] = "lifecycle-note",
            ["project"] = "e2e",
        });
        var restored = await _client.CallToolAsync("vault_restore", new Dictionary<string, object>
        {
            ["name"] = "lifecycle-note",
            ["project"] = "e2e",
        });

        // Assert
        Assert.False(archived.IsError ?? false);
        var archivedBody = ToJson(archived.StructuredContent);
        Assert.True(archivedBody.GetProperty("ok").GetBoolean());
        Assert.Equal("archived", archivedBody.GetProperty("message").GetString());

        Assert.False(restored.IsError ?? false);
        var restoredBody = ToJson(restored.StructuredContent);
        Assert.True(restoredBody.GetProperty("ok").GetBoolean());
        Assert.Equal("restored", restoredBody.GetProperty("message").GetString());

        // The restored note is writable again.
        await Save("lifecycle-note", "body v2", baseVersion: 1);
    }

    [Fact]
    public async Task ReadResource_MissingNote_ReturnsDomainErrorBody()
    {
        // Act
        var act = async () => await _client.ReadResourceAsync("vault://e2e/ghost");

        // Assert
        var exception = await Assert.ThrowsAsync<McpProtocolException>(act);
        Assert.Contains("not_found", exception.Message);
    }

    [Fact]
    public async Task VaultGet_TopLevelNote_EmitsExplicitNullParent()
    {
        // Arrange
        await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "null-parent",
            ["content"] = "body",
            ["summary"] = "s",
            ["project"] = "e2e",
        });

        // Act
        var result = await _client.CallToolAsync("vault_get", new Dictionary<string, object>
        {
            ["name"] = "null-parent",
            ["project"] = "e2e",
        });

        // Assert
        var body = ToJson(result.StructuredContent);
        Assert.True(body.TryGetProperty("parent", out var parent));
        Assert.Equal(JsonValueKind.Null, parent.ValueKind);
        Assert.Equal("body", body.GetProperty("content").GetString());
        Assert.Equal(1, body.GetProperty("current_version").GetInt32());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("children").ValueKind);
    }

    [Fact]
    public async Task VaultGet_MissingNote_ReturnsIsErrorWithNotFoundJsonBody()
    {
        // Act
        var result = await _client.CallToolAsync("vault_get", new Dictionary<string, object>
        {
            ["name"] = "ghost-note",
            ["project"] = "e2e",
        });

        // Assert
        Assert.True(result.IsError);
        var error = ParseErrorBody(result).GetProperty("error");
        Assert.Equal("not_found", error.GetProperty("code").GetString());
        Assert.Equal("e2e", error.GetProperty("project").GetString());
        Assert.Equal("ghost-note", error.GetProperty("name").GetString());
        Assert.Contains("ghost-note", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task VaultSave_StaleBase_ReturnsConflictBodyWithVersionsAndDiff()
    {
        // Arrange
        await Save("conflicted", "line one", baseVersion: null);
        await Save("conflicted", "line two", baseVersion: 1);

        // Act
        var result = await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "conflicted",
            ["content"] = "line stale",
            ["summary"] = "s",
            ["project"] = "e2e",
            ["base_version"] = 1,
        });

        // Assert
        Assert.True(result.IsError);
        var error = ParseErrorBody(result).GetProperty("error");
        Assert.Equal("conflict", error.GetProperty("code").GetString());
        Assert.Equal(2, error.GetProperty("current_version").GetInt32());
        Assert.Equal(1, error.GetProperty("base_version").GetInt32());
        var diff = error.GetProperty("diff").GetString();
        Assert.Contains("-line one", diff);
        Assert.Contains("+line two", diff);
    }

    [Fact]
    public async Task VaultPurge_WithoutConfirm_ReturnsConfirmationRequiredBody()
    {
        // Arrange
        await Save("purge-me", "content", baseVersion: null);

        // Act
        var result = await _client.CallToolAsync("vault_purge", new Dictionary<string, object>
        {
            ["name"] = "purge-me",
            ["project"] = "e2e",
        });

        // Assert
        Assert.True(result.IsError);
        Assert.Equal("confirmation_required", ParseErrorBody(result).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resources_MonorepoProject_ListsAndReadsByUri()
    {
        // Arrange
        await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "resource-note",
            ["content"] = "resource body",
            ["summary"] = "resource summary",
            ["project"] = "mono/app",
        });

        // Act
        var resources = await _client.ListResourcesAsync();
        var read = await _client.ReadResourceAsync("vault://mono/app/resource-note");

        // Assert
        var resource = resources.Single(r => r.Uri == "vault://mono/app/resource-note");
        Assert.Equal("resource-note", resource.Name);
        Assert.Equal("resource summary", resource.Description);
        var contents = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents));
        Assert.Equal("resource body", contents.Text);
    }

    [Fact]
    public async Task Prompts_ContinueDraft_ReturnsExactRustParityText()
    {
        // Arrange
        await Save("draft-note", "draft body here", baseVersion: null);

        // Act
        var prompts = await _client.ListPromptsAsync();
        var prompt = await _client.GetPromptAsync("continue-draft", new Dictionary<string, object>
        {
            ["name"] = "draft-note",
            ["project"] = "e2e",
        });

        // Assert
        var promptNames = prompts.Select(p => p.Name).ToList();
        Assert.Contains("continue-draft", promptNames);
        Assert.Contains("summarize-vault", promptNames);
        var message = Assert.Single(prompt.Messages);
        Assert.Equal(Role.User, message.Role);
        var content = Assert.IsType<TextContentBlock>(message.Content);
        Assert.Equal(
            "Here is the current draft (version 1). Continue it from where it leaves off, "
            + "preserving voice and intent. Do not repeat what is already written.\n\ndraft body here",
            content.Text);
    }

    [Fact]
    public async Task Prompts_SummarizeVault_ListsItemsInExactFormat()
    {
        // Arrange
        await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "summarize-me",
            ["content"] = "body",
            ["summary"] = "one-liner",
            ["project"] = "prompt-proj",
        });

        // Act
        var prompt = await _client.GetPromptAsync("summarize-vault", new Dictionary<string, object>
        {
            ["project"] = "prompt-proj",
        });

        // Assert
        var text = Assert.IsType<TextContentBlock>(prompt.Messages.Single().Content).Text;
        Assert.StartsWith("Summarize the following vault items for the user, grouping related ones:\n\n", text);
        Assert.Contains("- prompt-proj/summarize-me (v1): one-liner\n", text);
    }

    [Fact]
    public async Task Prompts_SummarizeVault_EmptyProject_SaysVaultIsEmpty()
    {
        // Act
        var prompt = await _client.GetPromptAsync("summarize-vault", new Dictionary<string, object>
        {
            ["project"] = "definitely-empty-project",
        });

        // Assert
        var content = Assert.IsType<TextContentBlock>(prompt.Messages.Single().Content);
        Assert.Equal(
            "Summarize the following vault items for the user, grouping related ones:\n\n(the vault is empty)",
            content.Text);
    }

    [Fact]
    public async Task VaultList_ReturnsItemsWithSnakeCaseFields()
    {
        // Arrange
        await _client.CallToolAsync("vault_save", new Dictionary<string, object>
        {
            ["name"] = "list-shape",
            ["content"] = "body",
            ["summary"] = "s",
            ["project"] = "list-proj",
            ["tags"] = ListShapeTags,
        });

        // Act
        var result = await _client.CallToolAsync("vault_list", new Dictionary<string, object>
        {
            ["project"] = "list-proj",
        });

        // Assert
        var item = ToJson(result.StructuredContent).GetProperty("items").EnumerateArray().Single();
        Assert.Equal(1, item.GetProperty("current_version").GetInt32());
        Assert.True(item.GetProperty("updated_at").GetInt64() > 0);
        Assert.Equal(["tag-a", "tag-b"], item.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("parent").ValueKind);
    }

    private async Task Save(string name, string content, int? baseVersion)
    {
        var arguments = new Dictionary<string, object>
        {
            ["name"] = name,
            ["content"] = content,
            ["summary"] = "s",
            ["project"] = "e2e",
        };
        if (baseVersion is int version)
        {
            arguments["base_version"] = version;
        }

        var result = await _client.CallToolAsync("vault_save", arguments);
        Assert.False(result.IsError ?? false);
    }

    private static List<string> SchemaProperties(McpClientTool tool)
        => [.. ToJson(tool.JsonSchema)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(p => p.Name)];

    private static JsonElement ToJson(object value)
        => JsonSerializer.SerializeToElement(value);

    private static JsonElement ParseErrorBody(CallToolResult result)
    {
        // The SDK prefixes a failed call's text with "An error occurred invoking '<tool>': ";
        // the structured error contract is the JSON object that follows it.
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        Assert.True(start >= 0, $"the error text must carry the JSON error body, but was: '{text}'");
        return JsonSerializer.Deserialize<JsonElement>(text[start..]);
    }
}