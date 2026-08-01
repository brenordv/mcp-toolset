using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;
using RaccoonNinja.McpToolset.Server.TextSearch.Metrics;
using RaccoonNinja.McpToolset.Server.TextSearch.Tools;
using Serilog;

namespace RaccoonNinja.McpToolset.Server.TextSearch;

/// <summary>
/// stdio MCP server entrypoint. Order is load-bearing:
/// 1. Configure Serilog (stderr/file, never stdout) before anything can touch stdout.
/// 2. Load config and build the confinement root; a bad root is fatal (exit 1).
/// 3. Build the host and register the read-side collaborators and the tools.
/// 4. Install the stdout sentinel after MCP wiring, so the stdio transport keeps its raw stream.
/// 5. Run; on shutdown emit a server_stop summary with metrics.
/// </summary>
public static class Program
{
    private const string ServerName = "text-search";

    private const string ServerInstructions =
        "Read-only, base-root-confined text search and inspection. Blanket-approve it: it never writes, "
        + "never leaves its configured base root, and never reads a secret (a non-overridable denylist "
        + "covers .env, keys, .git, and the like). Pass cwd (an absolute working directory inside the base "
        + "root) to scope a call to one project; omit it to search the whole base root, which is the heavy "
        + "path. Paths are relative to cwd (or to the base root when cwd is omitted), in and out. Call "
        + "describe_scope first for the caps, the ignore tiers, and the denylist. find_files lists files by "
        + "glob (primary), regex, or explicit paths; inspect_files reports encoding and line shape; "
        + "search_text greps line by line; read_lines returns a numbered slice. include_ignored takes globs "
        + "that re-include otherwise-ignored paths (never secrets). List results paginate via the returned "
        + "cursor (keep cwd stable across pages).";

    /// <summary>The process entrypoint.</summary>
    /// <param name="args">Command-line arguments (passed to the host builder; config comes from the environment).</param>
    /// <returns>0 on a clean shutdown, 1 on a fatal startup or crash.</returns>
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = LoggingBootstrap.BuildLogger();
        var metrics = new SessionMetrics();
        var serverLogger = ServerEventLog.ForServer(Log.Logger);

        SearchConfig config;
        ScopeResolver resolver;
        try
        {
            config = SearchConfig.Load();
            resolver = ScopeResolver.Load(config);
            var caps = string.Create(
                CultureInfo.InvariantCulture,
                $"{config.CapsSummary()} defaultIgnore={resolver.DefaultIgnorePatterns.Count} denylist={resolver.Denylist.DescribePatterns().Count}");
            ServerEventLog.Scope(serverLogger, resolver.RootHash, caps);
        }
        catch (SearchStartupException ex)
        {
            ServerEventLog.StartFailed(serverLogger, ex);
            await Console.Error.WriteLineAsync($"text-search: fatal error: {ex.Message}").ConfigureAwait(false);
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
            return 1;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(resolver);
        builder.Services.AddSingleton<ISecretDenylist>(resolver.Denylist);
        builder.Services.AddSingleton<IEncodingDetector, EncodingDetector>();
        builder.Services.AddSingleton<ToolCommon>();

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation
                {
                    Name = ServerName,
                    Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                };
                options.ServerInstructions = ServerInstructions;
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(Program).Assembly);

        // The stdout sentinel goes in AFTER MCP wiring, so the stdio transport keeps its raw stream.
        StdoutSentinel.Install();

        var reason = "graceful";
        var exitCode = 0;
        try
        {
            await builder.Build().RunAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            reason = "cancelled";
        }
        catch (Exception ex)
        {
            reason = $"unexpected exception: {ex.GetType().Name}";
            exitCode = 1;
            ServerEventLog.StopFailed(serverLogger, ex);
        }
        finally
        {
            ServerEventLog.Stop(serverLogger, metrics.Summary(), reason);
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }

        return exitCode;
    }
}