using RaccoonNinja.McpToolset.Files.Selection;

namespace RaccoonNinja.McpToolset.Files.Tests.Selection;

public sealed class PathIgnoreEvaluatorTests : IDisposable
{
    private readonly string _root;

    public PathIgnoreEvaluatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rnmcp-pathignore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void IsIgnored_AncestorDirectoryRule_MatchesFileBeneathIt()
    {
        // Arrange
        WriteIgnoreFile(string.Empty, IgnoreRules.GitIgnoreFileName, "bin/\n");

        // Act
        var actual = PathIgnoreEvaluator.IsIgnored(_root, "bin/app.dll");

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsIgnored_AncestorDirectoryRule_MatchesNestedDirectoryBeneathIt()
    {
        // Arrange
        WriteIgnoreFile(string.Empty, IgnoreRules.GitIgnoreFileName, "bin/\n");

        // Act
        var actual = PathIgnoreEvaluator.IsIgnored(_root, "src/bin/app.dll");

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsIgnored_LeafRule_MatchesTheFileAtAnyDepth()
    {
        // Arrange
        WriteIgnoreFile(string.Empty, IgnoreRules.GitIgnoreFileName, "*.log\n");

        // Act
        var actual = PathIgnoreEvaluator.IsIgnored(_root, "logs/app.log");

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsIgnored_UntrackedFile_IsNotIgnored()
    {
        // Arrange
        WriteIgnoreFile(string.Empty, IgnoreRules.GitIgnoreFileName, "bin/\n*.log\n");

        // Act
        var actual = PathIgnoreEvaluator.IsIgnored(_root, "src/Program.cs");

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public void IsIgnored_DeeperIgnoreFileReincludes_LastMatchWins()
    {
        // Arrange
        WriteIgnoreFile(string.Empty, IgnoreRules.GitIgnoreFileName, "*.log\n");
        WriteIgnoreFile("sub", IgnoreRules.McpIgnoreFileName, "!keep.log\n");

        // Act
        var reincluded = PathIgnoreEvaluator.IsIgnored(_root, "sub/keep.log");
        var stillIgnored = PathIgnoreEvaluator.IsIgnored(_root, "sub/other.log");

        // Assert
        Assert.False(reincluded);
        Assert.True(stillIgnored);
    }

    [Fact]
    public void IsIgnored_AgentIgnoreFileLeaf_IsIgnored()
    {
        // Arrange
        WriteIgnoreFile(string.Empty, ".cursorignore", "agent-secret.txt\n");

        // Act
        var actual = PathIgnoreEvaluator.IsIgnored(_root, "config/agent-secret.txt");

        // Assert
        Assert.True(actual);
    }

    [Fact]
    public void IsIgnored_BlankArguments_Throw()
    {
        // Arrange
        // Act
        // Assert
        Assert.Throws<ArgumentException>(() => PathIgnoreEvaluator.IsIgnored("   ", "a.txt"));
        Assert.Throws<ArgumentException>(() => PathIgnoreEvaluator.IsIgnored(_root, "   "));
    }

    private void WriteIgnoreFile(string relativeDir, string fileName, string contents)
    {
        var dir = relativeDir.Length == 0
            ? _root
            : Path.Combine(_root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), contents);
    }
}