using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;

namespace RaccoonNinja.McpToolset.Server.GitOps.Security;

/// <summary>
/// Light input-shape validation applied before any user value reaches argv.
/// The attached-form / <c>--end-of-options</c> discipline in <see cref="GitCommandBuilder"/>
/// is the load-bearing defense; these checks are belt-and-braces.
/// </summary>
public static class ArgumentValidation
{
    /// <summary>Reject NUL, control chars (except tab), and a leading <c>-</c>.</summary>
    public static void RejectIfUnsafeValue(string paramName, string value)
    {
        if (value == null) return;
        if (value.Length == 0) return;
        if (value.Contains('\0'))
        {
            throw new RejectedArgumentException(
                $"parameter '{paramName}' contains NUL",
                new Dictionary<string, object> { ["param"] = paramName });
        }
        foreach (var c in value)
        {
            if (c < 0x20 && c != '\t')
            {
                throw new RejectedArgumentException(
                    $"parameter '{paramName}' contains a control character",
                    new Dictionary<string, object> { ["param"] = paramName });
            }
        }
        if (value[0] == '-')
        {
            throw new RejectedArgumentException(
                $"parameter '{paramName}' must not begin with '-'",
                new Dictionary<string, object> { ["param"] = paramName });
        }
    }
}