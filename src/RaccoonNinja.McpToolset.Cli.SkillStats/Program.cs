using RaccoonNinja.McpToolset.Cli.SkillStats.Ingest;
using RaccoonNinja.McpToolset.Common.SkillStats.Configuration;

namespace RaccoonNinja.McpToolset.Cli.SkillStats;

/// <summary>
/// The skill-usage ingests hook entrypoint. Reads the Skill tool's PostToolUse JSON from stdin and
/// appends one row to the local skill-usage store. It takes no arguments and never blocks the agent:
/// the tool call has already run, so any failure only logs a line and surfaces stderr with exit 2.
/// </summary>
public static class Program
{
    /// <summary>The process entrypoint. The hook payload arrives on stdin; argv is ignored.</summary>
    /// <returns>0 on a recorded row or a benign skip, 2 when recording failed.</returns>
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
                "skill-usage: failed to record skill usage; see ~/.skill-stats/ingest-errors.log");
            return IngestRunner.ExitFailure;
        }
    }
}