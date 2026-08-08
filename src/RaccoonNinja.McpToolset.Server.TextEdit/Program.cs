using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using RaccoonNinja.McpToolset.Files.Security;
using RaccoonNinja.McpToolset.Files.Text;
using RaccoonNinja.McpToolset.Server.TextEdit.Configuration;
using RaccoonNinja.McpToolset.Server.TextEdit.Content;
using RaccoonNinja.McpToolset.Server.TextEdit.Journal;
using RaccoonNinja.McpToolset.Server.TextEdit.Logging;
using RaccoonNinja.McpToolset.Server.TextEdit.Metrics;
using RaccoonNinja.McpToolset.Server.TextEdit.Tools;
using Serilog;

namespace RaccoonNinja.McpToolset.Server.TextEdit;

/// <summary>
/// stdio MCP server entrypoint. Order is load-bearing:
/// 1. Configure Serilog (stderr/file, never stdout) before anything can touch stdout.
/// 2. Load config and build the confinement root; a bad root is fatal (exit 1).
/// 3. Build the host and register the collaborators and the tools.
/// 4. Install the stdout sentinel after MCP wiring, so the stdio transport keeps its raw stream.
/// 5. Run; on shutdown emit a server_stop summary with metrics.
/// </summary>
public static class Program
{
    private const string ServerName = "text-edit";

    private const string ServerInstructions =
        "Root-confined text mutation over a base root that may hold several projects, with hash-gated undo. "
        + "Pass cwd to scope a call to one project. Do NOT blanket-approve the "
        + "write tools (normalize_files, replace_text, undo_batch): keep them on prompt. Every write is "
        + "confined to the configured root and refuses a secret file via a non-overridable denylist, and "
        + "every batch is journaled so it can be undone. Call describe_scope first for the root and caps. "
        + "replace_text substitutes literal or regex matches (regex back-references $1 and ${name}); pass "
        + "dry_run to preview a unified diff, and expected_match_count to abort unless exactly that many "
        + "matches would change. normalize_files fixes encoding, line endings, and trailing whitespace. "
        + "list_recent_batches shows what can be undone; undo_batch/undo_last_batch restore a batch, "
        + "skipping any file changed since. Explicit paths are relative to cwd (or the base root when cwd "
        + "is omitted); reported and undoable paths are always base-relative, so undo and list_recent_batches "
        + "are base-global.";

    /// <summary>The process entrypoint.</summary>
    /// <param name="args">Command-line arguments (passed to the host builder; config comes from the environment).</param>
    /// <returns>0 on a clean shutdown, 1 on a fatal startup or crash.</returns>
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = LoggingBootstrap.BuildLogger();
        var metrics = new SessionMetrics();
        var serverLogger = ServerEventLog.ForServer(Log.Logger);

        EditConfig config;
        ScopeResolver resolver;
        SecretDenylist denylist;
        JournalStore journal;
        GatedFileWriter writer;
        Undoer undoer;
        try
        {
            config = EditConfig.Load();
            resolver = ScopeResolver.Load();
            denylist = resolver.Denylist;

            var journalPaths = JournalPaths.Resolve(resolver.BaseConfinement);
            journal = new JournalStore(journalPaths);
            try
            {
                journal.EnsureSchema();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
            {
                throw new EditStartupException($"could not open the journal database: {ex.Message}");
            }

            var detector = new EncodingDetector();
            writer = new GatedFileWriter(resolver.BaseConfinement, denylist, detector, journal, config, resolver.BaseRootName);
            undoer = new Undoer(resolver.BaseConfinement, denylist, journal, config.MaxFileBytes);

            var caps = string.Create(
                CultureInfo.InvariantCulture,
                $"{config.CapsSummary()} defaultIgnore={resolver.DefaultIgnorePatterns.Count} denylist={resolver.Denylist.DescribePatterns().Count}");
            ServerEventLog.Scope(serverLogger, resolver.RootHash, caps);
        }
        catch (EditStartupException ex)
        {
            ServerEventLog.StartFailed(serverLogger, ex);
            await Console.Error.WriteLineAsync($"text-edit: fatal error: {ex.Message}").ConfigureAwait(false);
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
            return 1;
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: false);

        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(resolver);
        builder.Services.AddSingleton<ISecretDenylist>(denylist);
        builder.Services.AddSingleton(journal);
        builder.Services.AddSingleton(writer);
        builder.Services.AddSingleton(undoer);
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