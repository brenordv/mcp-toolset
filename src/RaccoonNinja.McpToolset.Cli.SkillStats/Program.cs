using RaccoonNinja.McpToolset.Cli.SkillStats.Ingest;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Cli.SkillStats;

/// <summary>
/// The skill-stats-cli hook entrypoint. Reads one Claude Code hook payload (a
/// <c>UserPromptExpansion</c> or <c>PostToolUse</c> event's JSON) from stdin and appends one row to the
/// local skill-usage store. It takes no arguments and never blocks the agent: a failure logs a line,
/// prints one stderr line, and exits 1, which both events treat as a non-blocking error. It never writes to stdout: on
/// exit 0 Claude Code reads a hook's stdout as JSON that can block the expansion or add context.
/// </summary>
public static class Program
{
    /// <summary>The process entrypoint. The hook payload arrives on stdin; argv is ignored.</summary>
    /// <returns>0 on a recorded row or a benign skip, 1 when recording failed.</returns>
    public static int Main()
    {
        try
        {
            using var stdin = Console.OpenStandardInput();
            return IngestRunner.Run(stdin, Console.Error, SkillStatsConfig.Load);
        }
        catch (Exception)
        {
            // Last-resort guard: nothing may escape and break the agent, even an unexpected failure.
            Console.Error.WriteLine(
                "skill-stats-cli: failed to record skill usage; see ~/.skill-stats/ingest-errors.log");
            return IngestRunner.ExitFailure;
        }
    }
}