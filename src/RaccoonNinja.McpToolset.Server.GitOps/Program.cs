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
/// 2. Find git once in the absolute PATH entries; every git process starts from that path.
/// 3. Build the host with MCP server registration.
/// 4. Install the stdout sentinel AFTER the host has captured Console.OpenStandardOutput().
/// 5. Log the git version (first statement in the try).
/// 6. Run the host.
/// 7. On shutdown, emit a server_stop summary with metrics.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = LoggingBootstrap.BuildLogger();
        var metrics = new SessionMetrics();
        var bootstrapLogger = ServerEventLog.ForServer(Log.Logger);

        ServerEventLog.Start(bootstrapLogger);
        var gitExecutable = GitExecutableLocator.Find(Environment.GetEnvironmentVariable("PATH"));

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton<IGitProcessRunner, GitProcessRunner>();
        builder.Services.AddSingleton<IRepoRootResolver>(_ => new RepoRootResolver(gitExecutable));
        builder.Services.AddSingleton<IRefVerifier>(_ => new RefVerifier(gitExecutable));
        builder.Services.AddSingleton(services => new ToolCommon(
            services.GetRequiredService<IRepoRootResolver>(),
            services.GetRequiredService<IGitProcessRunner>(),
            services.GetRequiredService<SessionMetrics>(),
            services.GetRequiredService<ILoggerFactory>(),
            gitExecutable));

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(Program).Assembly)
            .WithRequestFilters(filters => filters.AddCallToolFilter(ArgumentShapeFilter.Create(metrics)));

        // stdout sentinel goes in AFTER MCP wiring, so the stdio transport keeps its raw stream.
        StdoutSentinel.Install();

        var reason = "graceful";
        try
        {
            await GitVersionReader.LogAsync(bootstrapLogger, gitExecutable, CancellationToken.None);
            await builder.Build().RunAsync();
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
            await Log.CloseAndFlushAsync();
        }
        return 0;
    }
}