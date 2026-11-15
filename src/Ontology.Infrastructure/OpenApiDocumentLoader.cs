using System.Text.Json;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Ontology.Core;

namespace Ontology.Infrastructure;

public sealed class OpenApiDocumentLoader : IApiDocumentLoader
{
    public const int MaximumInputBytes = 16 * 1024 * 1024;

    public async Task<ApiDocumentLoadResult> LoadAsync(string inputPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var diagnostics = new List<OntologyDiagnostic>();
        void Report(string code, DiagnosticSeverity severity, string message, string pointer = "") =>
            diagnostics.Add(new(code, severity, message, new(inputPath, pointer)));
        try
        {
            // Never use the parser's URL overload or load external documents.
            await using var file = File.OpenRead(inputPath);
            using var input = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await file.ReadAsync(buffer, ct)) != 0)
            {
                if (input.Length + count > MaximumInputBytes)
                {
                    Report("input-too-large", DiagnosticSeverity.Error, $"Input exceeds {MaximumInputBytes} bytes.");
                    return new(null, diagnostics);
                }
                input.Write(buffer, 0, count);
            }
            input.Position = 0;
            using var json = await ApiSourceJson.ReadAsync(input, Path.GetExtension(inputPath).Equals(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml", ct);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The API document root must be an object.");
            input.Position = 0;
            var settings = new OpenApiReaderSettings { LoadExternalRefs = false, LeaveStreamOpen = true };
            settings.AddYamlReader();
            var format = Path.GetExtension(inputPath).Equals(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml";
            var parsed = await OpenApiDocument.LoadAsync(input, format, settings, ct);
            if (parsed.Diagnostic is { } diagnostic)
            {
                foreach (var error in diagnostic.Errors)
                    Report("openapi-parser-error", DiagnosticSeverity.Error, error.Message, error.Pointer ?? "");
                foreach (var warning in diagnostic.Warnings)
                    Report("openapi-parser-warning", DiagnosticSeverity.Warning, warning.Message, warning.Pointer ?? "");
            }
            if (parsed.Document is null) return new(null, diagnostics);
            var version = parsed.Diagnostic?.SpecificationVersion;
            if (version is not (OpenApiSpecVersion.OpenApi2_0 or OpenApiSpecVersion.OpenApi3_0 or OpenApiSpecVersion.OpenApi3_1))
            {
                Report("unsupported-version", DiagnosticSeverity.Error, $"Unsupported specification version '{version}'. Expected Swagger 2.0 or OpenAPI 3.0/3.1.");
                return new(null, diagnostics);
            }
            var root = json.RootElement;
            var nodes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            Index(root, "", nodes, ct);
            var references = new List<ApiReference>();
            foreach (var (pointer, node) in nodes)
            {
                if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("$ref", out var reference)) continue;
                var value = reference.GetString() ?? "";
                var target = LocalPointer(value);
                if (target is null)
                    Report("external-reference-unsupported", DiagnosticSeverity.Warning, $"Reference '{value}' is not a supported internal JSON Pointer; it was not loaded.", pointer + "/$ref");
                else if (!nodes.ContainsKey(target))
                {
                    Report("unresolved-reference", DiagnosticSeverity.Error, $"Internal reference '{value}' does not exist.", pointer + "/$ref");
                    target = null;
                }
                references.Add(new(value, new(inputPath, pointer + "/$ref"), target));
            }
            JsonElement? Resolve(JsonElement node)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                while (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("$ref", out var reference))
                {
                    var pointer = LocalPointer(reference.GetString() ?? "");
                    if (pointer is null || !visited.Add(pointer) || !nodes.TryGetValue(pointer, out node)) return null;
                }
                return node;
            }
            var schemas = new Dictionary<string, ApiSchema>(StringComparer.Ordinal);
            void Schema(JsonElement node, string pointer, string? name = null)
            {
                if (!schemas.TryAdd(pointer, new(name, new(inputPath, pointer), node))) return;
                if (node.ValueKind != JsonValueKind.Object) return;
                foreach (var field in new[] { "properties", "$defs", "definitions", "patternProperties", "dependentSchemas" })
                    if (node.TryGetProperty(field, out var map) && map.ValueKind == JsonValueKind.Object)
                        foreach (var entry in map.EnumerateObject()) Schema(entry.Value, pointer + "/" + field + "/" + Escape(entry.Name));
                foreach (var field in new[] { "items", "additionalProperties", "not", "contains", "if", "then", "else", "propertyNames", "unevaluatedProperties" })
                    if (node.TryGetProperty(field, out var child) && child.ValueKind is JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False)
                        Schema(child, pointer + "/" + field);
                foreach (var field in new[] { "allOf", "oneOf", "anyOf", "prefixItems" })
                    if (node.TryGetProperty(field, out var array) && array.ValueKind == JsonValueKind.Array)
                        foreach (var (child, index) in array.EnumerateArray().Select((value, index) => (value, index)))
                            Schema(child, pointer + "/" + field + "/" + index);
            }
            var schemaRoot = version == OpenApiSpecVersion.OpenApi2_0 ? "/definitions" : "/components/schemas";
            if (nodes.TryGetValue(schemaRoot, out var named) && named.ValueKind == JsonValueKind.Object)
                foreach (var entry in named.EnumerateObject()) Schema(entry.Value, schemaRoot + "/" + Escape(entry.Name), entry.Name);
            foreach (var (pointer, node) in nodes)
                if (pointer.EndsWith("/schema", StringComparison.Ordinal)) Schema(node, pointer);
            var operations = new List<ApiOperation>();
            if (root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object)
                foreach (var path in paths.EnumerateObject())
                {
                    if (Resolve(path.Value) is not { ValueKind: JsonValueKind.Object } pathItem) continue;
                    foreach (var method in new[] { "get", "put", "post", "delete", "options", "head", "patch", "trace" })
                        if (pathItem.TryGetProperty(method, out var operation))
                            operations.Add(new(method.ToUpperInvariant(), path.Name,
                                new(inputPath, "/paths/" + Escape(path.Name) + "/" + method), operation));
                }
            var versionName = version == OpenApiSpecVersion.OpenApi2_0 ? "2.0" : version == OpenApiSpecVersion.OpenApi3_0 ? "3.0" : "3.1";
            return new(new ApiDocument(versionName, parsed.Document.Info?.Title ?? "", schemas.Values.OrderBy(s => s.Provenance.SourcePointer, StringComparer.Ordinal),
                operations.OrderBy(o => o.Provenance.SourcePointer, StringComparer.Ordinal), references.OrderBy(r => r.Provenance.SourcePointer, StringComparer.Ordinal), root), diagnostics);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or OpenApiException or OpenApiUnsupportedSpecVersionException or ArgumentException or InvalidOperationException or YamlDotNet.Core.YamlException)
        {
            Report("document-load-failed", DiagnosticSeverity.Error, exception.Message);
            return new(null, diagnostics);
        }
    }

    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
    private static string? LocalPointer(string value) => value == "#" ? "" :
        value.StartsWith("#/", StringComparison.Ordinal) ? Uri.UnescapeDataString(value[1..]) : null;

    private static void Index(JsonElement node, string pointer, Dictionary<string, JsonElement> nodes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        nodes.Add(pointer, node);
        if (node.ValueKind == JsonValueKind.Object)
            foreach (var property in node.EnumerateObject())
            {
                if (property.Name is "example" or "examples" or "default" or "enum" || property.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
                Index(property.Value, pointer + "/" + Escape(property.Name), nodes, ct);
            }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var (child, index) in node.EnumerateArray().Select((value, index) => (value, index)))
                Index(child, pointer + "/" + index, nodes, ct);
    }
}
