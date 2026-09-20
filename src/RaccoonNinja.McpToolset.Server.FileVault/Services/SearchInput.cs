using RaccoonNinja.McpToolset.Server.FileVault.Storage;

namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>One note handed to <see cref="ContentSearcher"/>: a candidate plus its decoded body.</summary>
public sealed record SearchInput
{
    /// <summary>The candidate note.</summary>
    public SearchCandidateRow Candidate { get; init; }

    /// <summary>The decoded body text of the candidate's current version.</summary>
    public string Body { get; init; }
}