namespace RaccoonNinja.McpToolset.Server.FileVault.Services;

/// <summary>Central clock so every persisted timestamp uses the same source.</summary>
public static class VaultClock
{
    /// <summary>Current UNIX time in whole seconds (UTC).</summary>
    /// <returns>Seconds since the Unix epoch.</returns>
    public static long NowUnixSeconds()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}