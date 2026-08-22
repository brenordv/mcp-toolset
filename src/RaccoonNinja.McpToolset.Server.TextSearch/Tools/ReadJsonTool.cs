using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RaccoonNinja.McpToolset.Common.Files.Text;
using RaccoonNinja.McpToolset.Server.TextSearch.Configuration;
using RaccoonNinja.McpToolset.Server.TextSearch.Content.Json;
using RaccoonNinja.McpToolset.Server.TextSearch.Envelope;
using RaccoonNinja.McpToolset.Server.TextSearch.Errors;
using RaccoonNinja.McpToolset.Server.TextSearch.Logging;
using RaccoonNinja.McpToolset.Server.TextSearch.Models;

namespace RaccoonNinja.McpToolset.Server.TextSearch.Tools;

/// <summary>
/// The <c>read_json</c> tool: parse one JSON file in the call's scope and return the whole document
/// or the value at a minimal, Python-flavored <c>json_path</c>. The read goes through the same gate
/// as <c>read_lines</c>, so confinement, the denylist, the ignore tiers, and content-based secret
/// detection apply unchanged. Every error message is a constant string; anything derived from file
/// content (positions, resolved prefixes, property-name hints) travels only in the error detail,
/// which never reaches the server log.
/// </summary>
[McpServerToolType]
public sealed class ReadJsonTool(ToolCommon common, SearchConfig config, ScopeResolver resolver, IEncodingDetector detector)
{
    // JSONC leniency is deliberate: the biggest population of "JSON" files agents open (tsconfig,
    // editor and agent config) carries comments and trailing commas, and both flags reject nothing
    // that strict JSON accepts. Duplicate keys are rejected at parse: allowing them makes JsonNode
    // materialization throw an ArgumentException that quotes the duplicated key, so failing fast as
    // a typed JsonException is the one deterministic option the node DOM offers. Depth stays at the
    // parser default (64).
    private static readonly JsonDocumentOptions s_documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        AllowDuplicateProperties = false,
    };

    /// <summary>Parse a JSON file and return the whole document or the value at <paramref name="json_path"/>.</summary>
    /// <param name="path">The scope-relative file path.</param>
    /// <param name="cwd">An absolute working directory inside the base root the path is relative to; omit for the base root.</param>
    /// <param name="json_path">The property path into the document; omit for the whole document.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A single-item envelope carrying the resolved value and its kind.</returns>
    [McpServerTool(Name = "read_json", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Parse one JSON file and return the whole document or only the value at json_path, embedded as "
        + "real JSON (kind: object|array|string|number|boolean|null). json_path is dot/bracket syntax with "
        + "Python-style indexing: a.b.c, items[0], items[-1] (negative counts from the end), quoted keys "
        + "for names with dots (deps[\"lodash.merge\"]); property matching is case-sensitive; an optional "
        + "leading $ is ignored. Comments and trailing commas are tolerated (JSONC); duplicate object keys "
        + "are rejected as JsonInvalid. The path is relative to cwd (an absolute working directory "
        + "inside the base root); omit cwd to resolve against the base root, or pass cwd @name/<subpath> "
        + "(a package root from describe_scope) to read from a dependency cache. A miss returns "
        + "JsonPathNotFound with the deepest resolved prefix and the property names or array length at "
        + "that point; a value over the max_json_value_bytes cap returns ValueTooLarge with the same "
        + "navigation hint, so pass a narrower json_path and retry.")]
    public Task<ResultEnvelope> InvokeAsync(
        [Description("The path of the JSON file to read, relative to cwd (or the base root when cwd is omitted).")]
        string path,
        [Description("Absolute working directory inside the base root the path is relative to; or @name/<subpath> (a package root from describe_scope). Omit to resolve against the base root.")]
        string cwd = null,
        [Description("The property path into the document, e.g. compilerOptions.target, items[-1].name, deps[\"lodash.merge\"]. Omit for the whole document.")]
        string json_path = null,
        CancellationToken cancellationToken = default)
    {
        var ctx = common.MakeContext("read_json");
        return common.WrapAsync(ctx, () =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw TextSearchException.InvalidArgument("path must not be empty");
            }

            var segments = JsonPathParser.Parse(json_path);
            var scope = resolver.Resolve(cwd);
            var loaded = common.LoadDocument(ctx, scope.Reader, detector, path);

            // TextDocument exposes terminator-free lines, not the raw text. Rejoining with '\n' is
            // lossless for JSON (a raw newline is invalid inside a JSON string) and keeps the parser's
            // reported line numbers aligned with the file's real lines.
            var text = string.Join('\n', loaded.Document.Lines.Select(line => line.Content));
            var value = ExtractValue(text, segments);

            ctx.Log(
                LogLevel.Debug,
                "files_scanned",
                extras: new Dictionary<string, object>(StringComparer.Ordinal) { [LogFields.FilesScanned] = 1 });

            var filters = FiltersAppliedBuilder.Create()
                .Value("cwd", scope.ScopeKey)
                .Redact("path", path)
                .Redact("json_path", json_path)
                .Build();

            var payload = new JsonValueResult(
                loaded.RelativePath,
                string.IsNullOrWhiteSpace(json_path) ? null : json_path,
                JsonNodeFacts.KindOf(value),
                value);
            return Task.FromResult(ToolCommon.SingleSuccess(payload, filters));
        });
    }

    /// <summary>
    /// Parse, evaluate, and size-gate under one JSON-fault mapping, so no fault from the document's
    /// own content (a parse error, a depth overflow, a late materialization fault) can escape as an
    /// internal error or leak content through an exception message.
    /// </summary>
    private JsonNode ExtractValue(string text, IReadOnlyList<JsonPathSegment> segments)
    {
        try
        {
            var root = JsonNode.Parse(text, nodeOptions: null, s_documentOptions);
            var evaluation = JsonPathEvaluator.Evaluate(root, segments);
            if (!evaluation.Found)
            {
                throw PathNotFound(segments, evaluation);
            }

            EnsureValueWithinCap(evaluation.Value);
            return evaluation.Value;
        }
        catch (JsonException ex)
        {
            throw JsonInvalid(ex);
        }
        catch (ArgumentException)
        {
            throw new TextSearchException(ErrorCodes.JsonInvalid, "file is not valid JSON");
        }
    }

    /// <summary>Throw <see cref="ErrorCodes.ValueTooLarge"/> when the serialized value exceeds the cap.</summary>
    private void EnsureValueWithinCap(JsonNode value)
    {
        if (value is null)
        {
            return;
        }

        var valueBytes = Encoding.UTF8.GetByteCount(value.ToJsonString());
        if (valueBytes <= config.MaxJsonValueBytes)
        {
            return;
        }

        var detail = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["value_bytes"] = valueBytes,
            ["limit"] = config.MaxJsonValueBytes,
        };
        JsonNodeFacts.AddHint(detail, value);
        throw new TextSearchException(
            ErrorCodes.ValueTooLarge,
            "value exceeds the configured value limit; pass a narrower json_path",
            detail);
    }

    /// <summary>Build the <see cref="ErrorCodes.JsonPathNotFound"/> error with the deepest resolved prefix and a navigation hint.</summary>
    private static TextSearchException PathNotFound(IReadOnlyList<JsonPathSegment> segments, JsonPathEvaluation evaluation)
    {
        var detail = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["resolved_prefix"] = JsonPathRendering.Render(segments, evaluation.FailedIndex),
            ["kind"] = JsonNodeFacts.KindOf(evaluation.FailedNode),
        };
        JsonNodeFacts.AddHint(detail, evaluation.FailedNode);
        return new TextSearchException(
            ErrorCodes.JsonPathNotFound,
            "json_path does not resolve in this document",
            detail);
    }

    /// <summary>
    /// Build the <see cref="ErrorCodes.JsonInvalid"/> error from the parser's typed positions only.
    /// The exception's message can quote document content, so it never reaches the error; the
    /// zero-based line is presented one-based to match <c>read_lines</c> numbering.
    /// </summary>
    private static TextSearchException JsonInvalid(JsonException ex)
    {
        var detail = new Dictionary<string, object>(StringComparer.Ordinal);
        if (ex.LineNumber is { } line)
        {
            detail["line"] = line + 1;
        }

        if (ex.BytePositionInLine is { } bytePosition)
        {
            detail["byte_in_line"] = bytePosition;
        }

        return new TextSearchException(ErrorCodes.JsonInvalid, "file is not valid JSON", detail);
    }
}