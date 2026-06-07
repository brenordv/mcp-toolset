using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Security;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Security;

public class GitCommandBuilderTests
{
    private static GitIntent BasicIntent(string subcommand = "log")
        => new()
        {
            Subcommand = subcommand,
            RepoRoot = Path.GetTempPath(),
        };

    [Fact]
    public void Build_Prepends_FixedConfig_And_GlobalFlags()
    {
        var (argv, _) = GitCommandBuilder.Build(BasicIntent());
        Assert.Equal("git", argv[0]);
        Assert.Contains("-c", argv);
        Assert.Contains("core.fsmonitor=", argv);
        Assert.Contains("core.hooksPath=/nonexistent/git-check-hooks-disabled", argv);
        Assert.Contains("--no-pager", argv);
        Assert.Contains("--literal-pathspecs", argv);
    }

    [Fact]
    public void Build_Adds_DashCBlock_With_RepoRoot()
    {
        var intent = BasicIntent();
        var (argv, _) = GitCommandBuilder.Build(intent);
        var index = argv.IndexOf("-C");
        Assert.Equal(intent.RepoRoot, argv[index + 1]);
    }

    [Fact]
    public void Build_Adds_Diff_Hardening_For_DiffBearing_Subcommands()
    {
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("diff"));
        Assert.Contains("--no-ext-diff", argv);
        Assert.Contains("--no-textconv", argv);
    }

    [Fact]
    public void Build_Omits_Ext_Diff_For_Grep()
    {
        var (argv, _) = GitCommandBuilder.Build(BasicIntent("grep"));
        Assert.DoesNotContain("--no-ext-diff", argv);
        Assert.Contains("--no-textconv", argv);
    }

    [Fact]
    public void Build_Emits_AttachedOptions_With_Equals_For_LongFlags()
    {
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "alice"));
        var (argv, _) = GitCommandBuilder.Build(intent);
        Assert.Contains("--author=alice", argv);
    }

    [Fact]
    public void Build_Emits_AttachedOptions_Glued_For_SingleChar_Flags()
    {
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("-S", "ROOT"));
        var (argv, _) = GitCommandBuilder.Build(intent);
        Assert.Contains("-SROOT", argv);
    }

    [Fact]
    public void Build_Rejects_AttachedOption_Whose_Value_Starts_With_Dash()
    {
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "-evil"));
        Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent));
    }

    [Fact]
    public void Build_Always_Inserts_EndOfOptions_Barrier_Before_VerifiedRefs()
    {
        var intent = BasicIntent();
        intent.VerifiedRefs.Add("abc1234567");
        var (argv, _) = GitCommandBuilder.Build(intent);
        var barrierIdx = argv.IndexOf("--end-of-options");
        var refIdx = argv.IndexOf("abc1234567");
        Assert.True(barrierIdx < refIdx);
    }

    [Fact]
    public void Build_Separates_Pathspecs_With_DoubleDash()
    {
        var intent = BasicIntent();
        intent.Pathspecs.Add("src/foo.cs");
        var (argv, _) = GitCommandBuilder.Build(intent);
        var dashIdx = argv.IndexOf("--");
        var pathIdx = argv.IndexOf("src/foo.cs");
        Assert.True(dashIdx > 0 && pathIdx > dashIdx);
    }

    [Fact]
    public void Build_Rejects_Pathspec_That_Starts_With_Dash()
    {
        var intent = BasicIntent();
        intent.Pathspecs.Add("-rm-rf");
        Assert.Throws<RejectedArgumentException>(() => GitCommandBuilder.Build(intent));
    }

    [Fact]
    public void Build_Returns_Env_With_FixedGit_Neutralizers_And_Allowlisted_PATH()
    {
        var (_, env) = GitCommandBuilder.Build(BasicIntent());
        Assert.Equal("0", env["GIT_TERMINAL_PROMPT"]);
        Assert.Equal("1", env["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal("0", env["GIT_OPTIONAL_LOCKS"]);
        Assert.Equal("1", env["GIT_LITERAL_PATHSPECS"]);
        Assert.True(env.ContainsKey("PATH"));
    }

    [Fact]
    public void HardeningArgvPrefix_Contains_FixedConfig_And_GlobalFlags()
    {
        var prefix = GitCommandBuilder.HardeningArgvPrefix();
        Assert.Contains("-c", prefix);
        Assert.Contains("core.pager=cat", prefix);
        Assert.Contains("--no-pager", prefix);
    }

    [Fact]
    public void MaskedForLog_Replaces_UserOriginating_Tokens()
    {
        var intent = BasicIntent();
        intent.AttachedOptions.Add(new AttachedOption("--author", "alice"));
        intent.Pathspecs.Add("src/foo.cs");
        var (argv, _) = GitCommandBuilder.Build(intent);
        var masked = GitCommandBuilder.MaskedForLog(argv, intent);

        Assert.DoesNotContain("alice", string.Join(' ', masked));
        Assert.DoesNotContain("src/foo.cs", string.Join(' ', masked));
        Assert.Contains("--author=<author>", masked);
        Assert.Contains("<path>", masked);
    }
}