namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.LogFileValidatorExceptions;

/// <summary>
/// Raised when a configured log-file path fails validation during startup.
/// </summary>
/// <remarks>
/// This is intentionally a separate error domain from
/// <see cref="GitCheckExceptions.GitCheckException"/>: it is a fail-fast configuration error
/// surfaced before the host runs, never wrapped into a tool result envelope. The rejection
/// reason is carried by <see cref="System.Exception.Message"/>.
/// </remarks>
public sealed class LogPathRejectedException(string reason) : Exception(reason);