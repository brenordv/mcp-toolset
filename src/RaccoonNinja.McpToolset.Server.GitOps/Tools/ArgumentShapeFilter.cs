using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Common.Mcp;
using RaccoonNinja.McpToolset.Server.GitOps.Envelope;
using RaccoonNinja.McpToolset.Server.GitOps.Errors;
using RaccoonNinja.McpToolset.Server.GitOps.Errors.GitCheckExceptions;
using RaccoonNinja.McpToolset.Server.GitOps.Logging;
using RaccoonNinja.McpToolset.Server.GitOps.Metrics;

namespace RaccoonNinja.McpToolset.Server.GitOps.Tools;

/// <summary>
/// A call-tool filter that turns SDK argument-binding failures into the server's standard failure
/// envelope. It pre-validates each call's argument names against the matched tool's own input schema and
/// short-circuits an unknown or missing-required name before the tool runs; for a well-named call that
/// still fails inside the SDK (the dominant cause being an argument of the wrong JSON type) it rewrites
/// the SDK's contentless generic error into an <see cref="ErrorCodes.InvalidArgument"/> envelope. Every
/// other result passes through untouched.
/// </summary>
internal static class ArgumentShapeFilter
{
    private const string RewriteMessage =
        "argument names matched the schema but the call failed before the tool ran; most likely an "
        + "argument has the wrong JSON type; check each argument's type against the tool schema";

    private const string ExpectedNamesHint = "valid argument names are listed in detail.expected_arguments";

    private const string WithheldSdkError = "withheld: non-generic SDK error text";

    private const string BindingErrorOutcome = "binding_error";

    /// <summary>Build the call-tool filter, capturing the session metrics it counts binding errors on.</summary>
    /// <param name="metrics">The per-process metrics aggregator.</param>
    /// <returns>The filter delegate to register with <c>AddCallToolFilter</c>.</returns>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create(SessionMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        return next => async (context, cancellationToken) =>
        {
            if (context.MatchedPrimitive is not McpServerTool tool || !HasPropertiesObject(tool.ProtocolTool.InputSchema))
            {
                return await next(context, cancellationToken).ConfigureAwait(false);
            }

            var argumentNames = ArgumentNamesOf(context.Params);
            var validation = ArgumentShapeValidator.Validate(tool.ProtocolTool.InputSchema, argumentNames);
            if (!validation.IsValid)
            {
                RecordBindingError(metrics, context, tool.ProtocolTool.Name, PreValidationLog(validation));
                return PreValidationFailure(validation);
            }

            CallToolResult result;
            try
            {
                result = await next(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception) when (argumentNames.Length > 0)
            {
                // The SDK's argument binder threw before the tool ran. This server's tool bodies never
                // throw (ToolCommon.WrapAsync wraps every fault into an envelope), so an exception escaping
                // next is a bind failure around the method; with the names already validated, a wrong-typed
                // argument is the cause. RewriteFailure reconstructs the SDK's fixed generic text from the
                // tool name for detail.sdk_error.
                RecordBindingError(metrics, context, tool.ProtocolTool.Name, "type binding failure");
                return RewriteFailure(validation.Expected, tool.ProtocolTool.Name, null);
            }

            if (argumentNames.Length == 0 || !IsBareTemplateError(result, tool.ProtocolTool.Name, out var sdkText))
            {
                return result;
            }

            RecordBindingError(metrics, context, tool.ProtocolTool.Name, "type binding failure");
            return RewriteFailure(validation.Expected, tool.ProtocolTool.Name, sdkText);
        };
    }

    private static bool HasPropertiesObject(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object;

    private static string[] ArgumentNamesOf(CallToolRequestParams parameters)
        => parameters?.Arguments is { } arguments ? arguments.Keys.ToArray() : [];

    /// <summary>
    /// True only when the SDK returned its fixed generic failure for this exact tool. The match is the
    /// full bare template with its trailing period: a real git-ops envelope is valid JSON, never this
    /// string. The gate reconstructs the SDK's wording, so a future SDK reword would stop it firing; the
    /// degradation is graceful (the opaque string passes through un-rewritten, never a misclassification).
    /// </summary>
    private static bool IsBareTemplateError(CallToolResult result, string toolName, out string sdkText)
    {
        sdkText = null;
        if (result.IsError != true || result.Content is not { Count: 1 } content || content[0] is not TextContentBlock text)
        {
            return false;
        }

        if (!string.Equals(text.Text, BareTemplate(toolName), StringComparison.Ordinal))
        {
            return false;
        }

        sdkText = text.Text;
        return true;
    }

    private static string BareTemplate(string toolName)
        => $"An error occurred invoking '{toolName}'.";

    private static CallToolResult PreValidationFailure(ArgumentShapeResult validation)
    {
        var detail = new Dictionary<string, object>(StringComparer.Ordinal);
        if (validation.Unknown.Count > 0)
        {
            detail["unknown_arguments"] = validation.Unknown.Select(BuildUnknownEntry).ToList();
        }

        if (validation.MissingRequired.Count > 0)
        {
            detail["missing_required"] = validation.MissingRequired.ToList();
        }

        detail["expected_arguments"] = validation.Expected.ToList();

        return Failure(PreValidationMessage(validation), detail);
    }

    private static Dictionary<string, object> BuildUnknownEntry(ArgumentShapeUnknown unknown)
    {
        var entry = new Dictionary<string, object>(StringComparer.Ordinal) { ["given"] = unknown.Given };
        if (unknown.DidYouMean is not null)
        {
            entry["did_you_mean"] = unknown.DidYouMean;
        }

        return entry;
    }

    private static string PreValidationMessage(ArgumentShapeResult validation)
    {
        var parts = new List<string>(validation.Unknown.Count + validation.MissingRequired.Count);
        foreach (var unknown in validation.Unknown)
        {
            parts.Add(unknown.DidYouMean is null
                ? $"unknown argument '{unknown.Given}'"
                : $"unknown argument '{unknown.Given}' (did you mean '{unknown.DidYouMean}'?)");
        }

        foreach (var missing in validation.MissingRequired)
        {
            parts.Add($"missing required argument '{missing}'");
        }

        return parts.Count > 0
            ? $"{string.Join("; ", parts)}; {ExpectedNamesHint}"
            : ExpectedNamesHint;
    }

    private static CallToolResult RewriteFailure(IReadOnlyList<string> expected, string toolName, string sdkText)
    {
        var template = BareTemplate(toolName);
        var gatedSdkError = sdkText is null || string.Equals(sdkText, template, StringComparison.Ordinal)
            ? template
            : WithheldSdkError;
        var detail = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["expected_arguments"] = expected.ToList(),
            ["sdk_error"] = gatedSdkError,
        };

        return Failure(RewriteMessage, detail);
    }

    private static CallToolResult Failure(string message, IDictionary<string, object> detail)
    {
        var envelope = ResultEnvelope.Failure(new InvalidArgumentException(message, detail));
        var json = JsonSerializer.Serialize(envelope, McpJsonUtilities.DefaultOptions);
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = json }],
        };
    }

    private static void RecordBindingError(SessionMetrics metrics, RequestContext<CallToolRequestParams> context, string toolName, string logMessage)
    {
        metrics.RecordToolCall(toolName, BindingErrorOutcome);

        var loggerFactory = context.Services?.GetService<ILoggerFactory>();
        if (loggerFactory is null)
        {
            return;
        }

        new CallContext(toolName, loggerFactory.CreateLogger(toolName)).Log(
            LogLevel.Warning,
            BindingErrorOutcome,
            message: logMessage,
            extras: new Dictionary<string, object> { [LogFields.ErrorCode] = ErrorCodes.InvalidArgument });
    }

    private static string PreValidationLog(ArgumentShapeResult validation)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"shape violation: {validation.Unknown.Count} unknown, {validation.MissingRequired.Count} missing required");
}