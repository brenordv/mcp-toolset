using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using RaccoonNinja.McpToolset.Server.FileVault.Configuration;
using RaccoonNinja.McpToolset.Server.FileVault.Exceptions;
using RaccoonNinja.McpToolset.Server.FileVault.Logging;
using RaccoonNinja.McpToolset.Server.FileVault.Metrics;
using RaccoonNinja.McpToolset.Server.FileVault.Platform;
using RaccoonNinja.McpToolset.Server.FileVault.Resources;
using RaccoonNinja.McpToolset.Server.FileVault.Services;
using RaccoonNinja.McpToolset.Server.FileVault.Storage;
using RaccoonNinja.McpToolset.Server.FileVault.Tools;
using Serilog;

namespace RaccoonNinja.McpToolset.Server.FileVault;

/// <summary>
/// stdio MCP server entrypoint. Order of operations is load-bearing:
/// 1. Configure logging (stderr by default) before anything touches stdout.
/// 2. Load configuration and open/migrate the store; a failure here is fatal (exit 1).
/// 3. Build the host with MCP server registration (tools, prompts, resource handlers).
/// 4. Install the stdout sentinel AFTER the host has captured Console.OpenStandardOutput().
/// 5. Run the host; on shutdown, emit a server_stop summary with metrics.
/// </summary>
public static class Program
{
    private const string ServerName = "vault-mcp";

    private const string ServerInstructions =
        "A personal, cross-conversation file vault. Save content by name with a one-line "
        + "summary; retrieve, list, version, and edit it from any chat. Writes use optimistic "
        + "concurrency: pass the `base_version` you read, and re-read on a conflict. To change "
        + "only a note's summary, tags, or parent, use `vault_set_meta` — you never need to "
        + "resend content. Notes can be organized hierarchically: link a child note under a "
        + "main note via `parent`, and `vault_get` returns a note's parent and children so you "
        + "can split a large note into smaller related ones.";

    public static async Task<int> Main(string[] args)
    {
        Log.Logger = LoggingBootstrap.BuildLogger();
        var metrics = new SessionMetrics();
        var serverLogger = ServerEventLog.ForServer(Log.Logger);

        VaultConfig config;
        SqliteConnectionFactory connectionFactory;
        try
        {
            config = VaultConfig.Load();
            config.EnsureDirs();
            if (StoreHardening.LooksLikeNetworkPath(config.Home))
            {
                ServerEventLog.NonLocalStoreWarning(serverLogger, config.Home);
            }

            connectionFactory = new SqliteConnectionFactory(config);
            var migrationsApplied = Migrator.Run(connectionFactory);
            metrics.RecordMigrationsApplied(migrationsApplied);
            ServerEventLog.Start(serverLogger, config.Home, config.MaxContentBytes, config.BusyTimeoutMs, config.SplitHintChars);
            EmitStoreOpen(serverLogger, connectionFactory, config, migrationsApplied);
        }
        catch (Exception ex) when (ex is VaultStartupException or SqliteException or IOException or UnauthorizedAccessException)
        {
            ServerEventLog.StartFailed(serverLogger, ex);
            await Console.Error.WriteLineAsync($"vault-mcp: fatal error: {ex.Message}");
            await Log.CloseAndFlushAsync();
            return 1;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(connectionFactory);
        builder.Services.AddSingleton<IVaultRepository, SqliteVaultRepository>();
        builder.Services.AddSingleton(new FileStore(config.FilesDir));
        builder.Services.AddSingleton<VaultService>();
        builder.Services.AddSingleton(provider => new ProjectResolver(
            provider.GetRequiredService<VaultConfig>(),
            Environment.CurrentDirectory));
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
            .WithToolsFromAssembly(typeof(Program).Assembly)
            .WithPromptsFromAssembly(typeof(Program).Assembly)
            .WithListResourcesHandler((context, _) =>
                ValueTask.FromResult(VaultResourceHandlers.ListResources(
                    context.Services!.GetRequiredService<VaultService>())))
            .WithReadResourceHandler((context, _) =>
                ValueTask.FromResult(VaultResourceHandlers.ReadResource(
                    context.Services!.GetRequiredService<VaultService>(),
                    context.Params?.Uri)));

        // stdout sentinel goes in AFTER MCP wiring, so the stdio transport keeps its raw stream.
        StdoutSentinel.Install();

        var reason = "graceful";
        var exitCode = 0;
        try
        {
            await builder.Build().RunAsync();
        }
        catch (OperationCanceledException)
        {
            reason = "cancelled";
        }
        catch (Exception ex)
        {
            // A crashed host must not report a clean shutdown: supervisors key off the exit code.
            reason = $"unexpected exception: {ex.GetType().Name}";
            exitCode = 1;
            ServerEventLog.StopFailed(serverLogger, ex);
        }
        finally
        {
            ServerEventLog.Stop(serverLogger, metrics.Summary(), reason);
            await Log.CloseAndFlushAsync();
        }

        return exitCode;
    }

    /// <summary>Emit the cheap startup store summary: schema version, row counts, db size.</summary>
    private static void EmitStoreOpen(
        Serilog.ILogger serverLogger,
        SqliteConnectionFactory connectionFactory,
        VaultConfig config,
        int migrationsApplied)
    {
        using var connection = connectionFactory.Open();
        var schemaVersion = Migrator.CurrentVersion(connection);
        var active = CountScalar(connection, "SELECT COUNT(*) FROM files WHERE state = 'active'");
        var archived = CountScalar(connection, "SELECT COUNT(*) FROM files WHERE state = 'archived'");
        var versions = CountScalar(connection, "SELECT COUNT(*) FROM versions");
        var dbSize = File.Exists(config.DbPath) ? new FileInfo(config.DbPath).Length : 0;
        ServerEventLog.StoreOpen(serverLogger, schemaVersion, active, archived, versions, dbSize, migrationsApplied);
    }

    private static long CountScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar();
    }
}