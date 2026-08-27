using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RaccoonNinja.McpToolset.Server.TextEdit.Tests.Protocol;

/// <summary>
/// End-to-end protocol tests of the argument-shape call-tool filter through a real
/// <see cref="McpClient"/> over stdio against the built <c>text-edit</c> server, exercised through
/// <c>replace_text</c>: a missing required name is rejected before the tool runs, a wrong-typed argument
/// is rewritten after the SDK binder fails, an unknown name is rejected with a suggestion, and both a
/// genuine domain error and a well-formed dry run pass through untouched. The filter is tool-agnostic, so
/// one tool proves the mechanism.
/// </summary>
public sealed class ArgumentShapeFilterTests : IAsyncLifetime
{
    private static readonly int[] WrongTypedDryRun = [1, 2];

    private string _baseRoot;
    private McpClient _client;

    public async ValueTask InitializeAsync()
    {
        _baseRoot = Path.Combine(Path.GetTempPath(), "textedit-argshape", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_baseRoot);
        await File.WriteAllTextAsync(Path.Combine(_baseRoot, "sample.txt"), "foo\n");

        var serverDll = Path.Combine(AppContext.BaseDirectory, "text-edit.dll");
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "textedit-argshape-e2e",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MCP_TEXTEDIT_BASE_ROOT"] = _baseRoot,
                ["MCP_TEXTEDIT_LOG_LEVEL"] = "warning",
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
            Directory.Delete(_baseRoot, recursive: true);
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
    public async Task MissingRequired_RejectedBeforeTheToolRuns()
    {
        // Act
        var result = await _client.CallToolAsync("replace_text", new Dictionary<string, object>());

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.Contains("pattern", Strings(error.GetProperty("detail").GetProperty("missing_required")));
    }

    [Fact]
    public async Task UnknownArgument_RewrittenWithSuggestion()
    {
        // Act
        var result = await _client.CallToolAsync("replace_text", new Dictionary<string, object>
        {
            ["pattern"] = "foo",
            ["replacement"] = "bar",
            ["globb"] = "sample.txt",
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.Contains("glob", Suggestions(error.GetProperty("detail").GetProperty("unknown_arguments")));
    }

    [Fact]
    public async Task WrongTypedArgument_RewrittenWithSdkErrorInDetail()
    {
        // Act
        var result = await _client.CallToolAsync("replace_text", new Dictionary<string, object>
        {
            ["pattern"] = "foo",
            ["replacement"] = "bar",
            ["dry_run"] = WrongTypedDryRun,
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("detail").TryGetProperty("sdk_error", out _));
    }

    [Fact]
    public async Task GenuineDomainError_InvalidRegex_StaysPatternInvalid()
    {
        // Act
        var result = await _client.CallToolAsync("replace_text", new Dictionary<string, object>
        {
            ["pattern"] = "[",
            ["replacement"] = "x",
            ["is_regex"] = true,
        });

        // Assert
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("PatternInvalid", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task WellFormedDryRun_PassesThroughAsSuccess()
    {
        // Act
        var result = await _client.CallToolAsync("replace_text", new Dictionary<string, object>
        {
            ["pattern"] = "foo",
            ["replacement"] = "bar",
            ["glob"] = "sample.txt",
            ["dry_run"] = true,
        });

        // Assert
        Assert.False(result.IsError ?? false);
        var envelope = Envelope(result);
        Assert.False(envelope.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null);
        Assert.True(envelope.GetProperty("results").GetArrayLength() >= 1);
    }

    private static JsonElement Envelope(CallToolResult result)
    {
        var text = result.Content.OfType<TextContentBlock>().Single().Text;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        Assert.True(start >= 0, $"the result text must carry the JSON envelope, but was: '{text}'");
        return JsonSerializer.Deserialize<JsonElement>(text[start..]);
    }

    private static string[] Strings(JsonElement array)
        => [.. array.EnumerateArray().Select(item => item.GetString())];

    private static string[] Suggestions(JsonElement unknownArguments)
        => [.. unknownArguments.EnumerateArray()
            .Where(item => item.TryGetProperty("did_you_mean", out _))
            .Select(item => item.GetProperty("did_you_mean").GetString())];
}