using ModelContextProtocol.Client;

namespace RaccoonNinja.McpToolset.Server.SkillStats.Tests.Protocol;

/// <summary>
/// Guards the argument-shape filter's logging invariant: a binding error emits one content-free Warning
/// through <c>CallContext.Log</c>, which has no exception channel, so the on-disk sink never gains an
/// <c>exception_tail</c> (the one field that could echo a bind exception's raw argument value or path).
/// The <c>text-edit</c> filter logs through the byte-identical path. This runs its own server so it can
/// point the log at a file and read it back after shutdown.
/// </summary>
public sealed class BindingErrorLogTests
{
    [Fact]
    public async Task BindingErrorWarning_CarriesNoExceptionTailOrPath()
    {
        // Arrange
        var home = Path.Combine(Path.GetTempPath(), "skillstats-logscrub", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        var logFile = Path.Combine(home, "server.log");
        var serverDll = Path.Combine(AppContext.BaseDirectory, "skill-stats-mcp.dll");
        Assert.True(File.Exists(serverDll));

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = "dotnet",
            Arguments = [serverDll],
            Name = "skillstats-logscrub-e2e",
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["SKILL_STATS_HOME"] = home,
                ["MCP_SKILLSTATS_LOG_FILE"] = logFile,
                ["MCP_SKILLSTATS_LOG_LEVEL"] = "warning",
            },
        });
        var client = await McpClient.CreateAsync(transport);

        // Act
        var result = await client.CallToolAsync("top_skills", new Dictionary<string, object> { ["limitt"] = 5 });
        Assert.True(result.IsError ?? false);
        await client.DisposeAsync();
        var log = await ReadWhenAsync(logFile, "binding_error", TimeSpan.FromSeconds(15));

        // Assert
        Assert.Contains("binding_error", log, StringComparison.Ordinal);
        Assert.DoesNotContain("exception_tail", log, StringComparison.Ordinal);
        Assert.DoesNotContain(":\\", log, StringComparison.Ordinal);

        try
        {
            Directory.Delete(home, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Windows reports a lingering handle this way.
        }
    }

    private static async Task<string> ReadWhenAsync(string path, string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var content = TryReadShared(path);
            if (content is not null && content.Contains(marker, StringComparison.Ordinal))
            {
                return content;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"the log file did not contain '{marker}' within {timeout.TotalSeconds:0}s");
        return string.Empty;
    }

    private static string TryReadShared(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            // The child server may still hold the file open; the caller retries.
            return null;
        }
    }
}