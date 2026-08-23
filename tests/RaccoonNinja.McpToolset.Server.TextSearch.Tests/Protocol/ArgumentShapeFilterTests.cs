using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tests.Protocol;

/// <summary>
/// End-to-end protocol tests of the argument-shape call-tool filter through a real
/// <see cref="McpClient"/> over stdio against the built <c>text-search</c> server: an unknown/missing
/// name is rejected before the tool runs, a wrong-typed known argument is rewritten after the SDK
/// fails, a well-formed call passes through untouched, and the binding-error log line carries no
/// caller-supplied key.
/// </summary>
public sealed class ArgumentShapeFilterTests : IAsyncLifetime
{
    private string _home;
    private string _logPath;
    private McpClient _client;

    public async ValueTask InitializeAsync()
    {
        _home = Path.Combine(Path.GetTempPath(), "rnmcp-argshape-e2e", Guid.NewGuid().ToString("N"));
        var baseRoot = Path.Combine(_home, "base");
        Directory.CreateDirectory(baseRoot);
        await File.WriteAllTextAsync(Path.Combine(baseRoot, "sample.txt"), "a needle in the haystack");
        _logPath = Path.Combine(_home, "server.log");

        var serverDll = Path.Combine(AppContext.BaseDirectory, "text-search.dll");
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "argshape-e2e",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MCP_TEXTSEARCH_BASE_ROOT"] = baseRoot,
                ["MCP_TEXTSEARCH_LOG_FILE"] = _logPath,
                ["MCP_TEXTSEARCH_LOG_LEVEL"] = "warning",
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
            // Best-effort temp cleanup; the child server may still be releasing the log file.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows reports a lingering open handle this way.
        }
    }

    [Fact]
    public async Task ShapeViolation_UnknownAndMissingNames_RejectedBeforeTheToolRuns()
    {
        // Act
        var result = await _client.CallToolAsync("search_text", new Dictionary<string, object>
        {
            ["query"] = "needle",
            ["isRegex"] = false,
            ["globs"] = "*.txt",
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());

        var detail = error.GetProperty("detail");
        Assert.Contains("pattern", Strings(detail.GetProperty("missing_required")));
        Assert.Contains("is_regex", Suggestions(detail.GetProperty("unknown_arguments")));
    }

    [Fact]
    public async Task WrongJsonType_ForKnownArgument_RewrittenWithSdkErrorInDetail()
    {
        // Act
        var result = await _client.CallToolAsync("search_text", new Dictionary<string, object>
        {
            ["pattern"] = "needle",
            ["context_lines"] = new List<int> { 1, 2 },
        });

        // Assert
        Assert.True(result.IsError ?? false);
        var error = Envelope(result).GetProperty("error");
        Assert.Equal("InvalidArgument", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("detail").TryGetProperty("sdk_error", out _));
    }

    [Fact]
    public async Task WellFormedCall_PassesThroughAndOmitsNullEnvelopeMembers()
    {
        // Act
        var result = await _client.CallToolAsync("search_text", new Dictionary<string, object>
        {
            ["pattern"] = "needle",
        });

        // Assert
        Assert.False(result.IsError ?? false);
        var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
        Assert.DoesNotContain("\"error\"", text);
        Assert.DoesNotContain("\"cursor\"", text);
        Assert.DoesNotContain("\"pre_filter_count\"", text);
    }

    [Fact]
    public async Task BindingErrorLogLine_CarriesNoCallerSuppliedKey()
    {
        // Act
        _ = await _client.CallToolAsync("search_text", new Dictionary<string, object>
        {
            ["frobnicate"] = "x",
            ["whatsit"] = "y",
        });
        var log = await ReadLogUntilAsync(_logPath, "binding_error");

        // Assert
        Assert.Contains("binding_error", log);
        Assert.DoesNotContain("frobnicate", log);
        Assert.DoesNotContain("whatsit", log);
    }

    private static JsonElement Envelope(CallToolResult result)
        => JsonSerializer.Deserialize<JsonElement>(Assert.IsType<TextContentBlock>(result.Content[0]).Text);

    private static string[] Strings(JsonElement array)
        => [.. array.EnumerateArray().Select(item => item.GetString())];

    private static string[] Suggestions(JsonElement unknownArguments)
        => [.. unknownArguments.EnumerateArray()
            .Where(item => item.TryGetProperty("did_you_mean", out _))
            .Select(item => item.GetProperty("did_you_mean").GetString())];

    private static async Task<string> ReadLogUntilAsync(string path, string marker)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var text = ReadShared(path);
            if (text.Contains(marker, StringComparison.Ordinal))
            {
                return text;
            }

            await Task.Delay(100);
        }

        return ReadShared(path);
    }

    private static string ReadShared(string path)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }
}