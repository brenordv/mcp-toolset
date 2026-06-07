using System.ComponentModel;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Parsers;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

[McpServerToolType]
public sealed class GitStatusTool(ToolCommon common)
{
    [McpServerTool(Name = "git_status")]
    [Description("Return a structured working-tree status snapshot (porcelain v2, -z).")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The MCP client's absolute working directory. The repo root is resolved from this via 'git rev-parse --show-toplevel'.")]
        string cwd,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("git_status");
        var holder = new RootHolder();
        return common.WrapAsync(ctx, holder, async () =>
        {
            var root = await common.ResolveAndLogAsync(cwd, ctx, holder, cancellationToken).ConfigureAwait(false);
            var intent = new GitIntent
            {
                Subcommand = "status",
                RepoRoot = root,
                Flags =
                [
                    "--porcelain=v2",
                    "-z",
                    "--branch",
                    "--ahead-behind",
                ],
            };
            var result = await common.ExecuteAsync(intent, ctx, cancellationToken: cancellationToken).ConfigureAwait(false);
            var status = StatusParser.Parse(result.Stdout);
            return ToolCommon.SingleSuccess(status, root, truncated: result.Truncated);
        });
    }
}