using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
// Microsoft.Extensions.Logging.ILogger and Serilog.ILogger collide on import; we qualify Serilog uses explicitly.
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;
using RaccoonNinja.McpToolset.Server.GitOps.Repo;
using RaccoonNinja.McpToolset.Server.GitOps.Runner;
using RaccoonNinja.McpToolset.Server.GitOps.Tools;
using Serilog;

namespace RaccoonNinja.McpToolset.Server.GitOps;

/// <summary>
/// stdio MCP server entrypoint. Order of operations is load-bearing:
/// 1. Configure logging (stderr or rotating file) before anything touches stdout.
/// 2. Build the host with MCP server registration.
/// 3. Install the stdout sentinel AFTER the host has captured Console.OpenStandardOutput().
/// 4. Run the host.
/// 5. On shutdown, emit a server_stop summary with metrics.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = LoggingBootstrap.BuildLogger();
        var metrics = new SessionMetrics();
        var bootstrapLogger = ServerEventLog.ForServer(Log.Logger);

        ServerEventLog.Start(bootstrapLogger);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
        builder.Services.AddSingleton<IRepoRootResolver>(_ => new RepoRootResolver());
        builder.Services.AddSingleton<IRefVerifier>(_ => new RefVerifier());
        builder.Services.AddSingleton<ToolCommon>();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(Program).Assembly);

        // stdout sentinel goes in AFTER MCP wiring, so the stdio transport keeps its raw stream.
        StdoutSentinel.Install();

        var reason = "graceful";
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
            ServerEventLog.StopFailed(bootstrapLogger, ex);
        }
        finally
        {
            ServerEventLog.Stop(bootstrapLogger, metrics.Summary(), reason);
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
        }
        return 0;
    }
}