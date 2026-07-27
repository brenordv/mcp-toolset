namespace RaccoonNinja.McpToolset.Files.Selection;

/// <summary>
/// The guard rails a <see cref="SafeRegexCompiler"/> applies to an agent-supplied pattern. Defaults match
/// the server config: 1s match timeout, a 2&#160;KB pattern cap, and a bounded-quantifier product cap that
/// keeps a construction-time blow-up like <c>(a{1000}){1000}</c> from building a huge automaton before any
/// match runs (the match timeout does not bound compilation).
/// </summary>
public sealed record SafeRegexOptions
{
    /// <summary>When <c>true</c>, match case-sensitively; the default is case-insensitive.</summary>
    public bool CaseSensitive { get; init; }

    /// <summary>The per-match timeout, the real ReDoS guard once a pattern runs on the backtracking engine.</summary>
    public TimeSpan MatchTimeout { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>The maximum pattern length in characters; longer patterns are rejected before compiling.</summary>
    public int MaxPatternLength { get; init; } = 2048;

    /// <summary>
    /// The repetition ceiling: the product of all bounded-quantifier upper bounds (<c>{n}</c>, <c>{n,m}</c>) must
    /// stay below this. A pattern that reaches or exceeds it is rejected before compilation, which is where a
    /// bounded quantifier can otherwise exhaust memory building the automaton. The default rejects the canonical
    /// blow-up <c>(a{1000}){1000}</c>.
    /// </summary>
    public long MaxRepetitionProduct { get; init; } = 1_000_000;
}