namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>
/// The MCP call's arguments did not match the tool's input schema: an unknown or missing-required
/// name caught before binding, or a value whose JSON type the SDK binder could not bind. Distinct
/// from <see cref="RejectedArgumentException"/>, which means git itself rejected an otherwise
/// well-typed argument value.
/// </summary>
public sealed class InvalidArgumentException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.InvalidArgument;
}