namespace RaccoonNinja.McpToolset.Server.TextSearch.Configuration;

/// <summary>
/// Which configured root a <see cref="CallScope"/> resolved under. It disambiguates cursor identity so a
/// base subtree named <c>@foo</c> (whose base-relative <see cref="CallScope.ScopeKey"/> is <c>@foo</c>)
/// cannot alias a package root named <c>foo</c> (whose whole-cache scope key is also <c>@foo</c>). Routing
/// is never ambiguous on its own (a base <c>cwd</c> never starts with <c>@</c>); the kind guards the
/// derived scope key, not the routing.
/// </summary>
public enum ScopeKind
{
    /// <summary>Resolved under the single base root (the default for a blank, absolute, or relative <c>cwd</c>).</summary>
    Base = 1,

    /// <summary>Resolved under a named package root, addressed with an <c>@name</c> <c>cwd</c> prefix.</summary>
    Package = 2,
}
