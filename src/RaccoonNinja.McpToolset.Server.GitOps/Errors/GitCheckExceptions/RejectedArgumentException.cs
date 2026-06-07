namespace RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

/// <summary>
/// Security validation tripped on a typed parameter. The <see cref="Exception.Message"/>
/// names the parameter and reason but never echoes the raw value.
/// </summary>
public sealed class RejectedArgumentException(string message, IDictionary<string, object> detail = null)
    : GitCheckException(message, detail)
{
    public override string Code => ErrorCodes.RejectedArgument;
}