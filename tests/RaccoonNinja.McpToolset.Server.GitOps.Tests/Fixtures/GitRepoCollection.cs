namespace RaccoonNinja.McpToolset.Server.GitOps.Tests.Fixtures;

/// <summary>The xUnit collection whose classes share one <see cref="GitRepoFixture"/>.</summary>
[CollectionDefinition(nameof(GitRepoCollection))]
public sealed class GitRepoCollection : ICollectionFixture<GitRepoFixture> { }