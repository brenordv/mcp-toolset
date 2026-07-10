using RaccoonNinja.McpToolset.Server.FileVault.Domain;
using RaccoonNinja.McpToolset.Server.FileVault.Errors;

namespace RaccoonNinja.McpToolset.Server.FileVault.Extensions;

/// <summary>Parsing and string conversions for <see cref="VaultFormat"/>.</summary>
public static class VaultFormatExtensions
{
    /// <summary>The file extension used for on-disk version snapshots.</summary>
    /// <param name="format">The content format.</param>
    /// <returns>The extension without a leading dot.</returns>
    public static string Extension(this VaultFormat format)
        => format switch
        {
            VaultFormat.Markdown => "md",
            VaultFormat.Json => "json",
            VaultFormat.Yaml => "yaml",
            _ => "txt",
        };

    /// <summary>The lowercase canonical name persisted in the <c>files.format</c> column.</summary>
    /// <param name="format">The content format.</param>
    /// <returns>The canonical wire/database string.</returns>
    public static string ToWireString(this VaultFormat format)
        => format switch
        {
            VaultFormat.Markdown => "markdown",
            VaultFormat.Json => "json",
            VaultFormat.Yaml => "yaml",
            _ => "text",
        };

    /// <summary>
    /// Parse an optional format string, keeping <c>null</c> as <c>null</c> so the caller decides
    /// what an omitted format means (first saves default to text; updates inherit the stored format).
    /// </summary>
    /// <param name="value">The raw format string, or <c>null</c> when the caller omitted it.</param>
    /// <returns>The parsed format, or <c>null</c> for <c>null</c> input.</returns>
    /// <exception cref="VaultException">Thrown with <see cref="VaultErrorCode.InvalidFormat"/> for an unknown format.</exception>
    public static VaultFormat? ParseOptionalVaultFormat(string value)
        => value is null ? null : ParseVaultFormat(value);

    /// <summary>Parse a format string, accepting the Rust server's aliases case-insensitively.</summary>
    /// <param name="value">The raw format string; <c>null</c> defaults to <see cref="VaultFormat.Text"/>.</param>
    /// <returns>The parsed format.</returns>
    /// <exception cref="VaultException">Thrown with <see cref="VaultErrorCode.InvalidFormat"/> for an unknown format.</exception>
    public static VaultFormat ParseVaultFormat(string value)
    {
        return value is null
            ? VaultFormat.Text
            : value.ToLowerInvariant() switch
            {
                "text" or "txt" or "plain" => VaultFormat.Text,
                "markdown" or "md" => VaultFormat.Markdown,
                "json" => VaultFormat.Json,
                "yaml" or "yml" => VaultFormat.Yaml,
                var other => throw VaultException.InvalidFormat(
                    $"unknown format '{other}' (expected one of: text, markdown, json, yaml)"),
            };
    }
}