using System.Text.Json;

namespace Ontology.Core;

public interface IApiDocumentLoader
{
    Task<ApiDocumentLoadResult> LoadAsync(string inputPath, CancellationToken ct);
}

public sealed record ApiSchema(string? Name, SourceProvenance Provenance, JsonElement Definition);
public sealed record ApiOperation(string HttpMethod, string Path, SourceProvenance Provenance, JsonElement Definition);
public sealed record ApiReference(string Value, SourceProvenance Provenance, string? TargetPointer);

/// <summary>Parser-neutral syntax prior to ontology inference.</summary>
public sealed class ApiDocument
{
    public ApiDocument(string version, string title, IEnumerable<ApiSchema> schemas,
        IEnumerable<ApiOperation> operations, IEnumerable<ApiReference> references, JsonElement root)
    {
        Version = version;
        Title = title;
        Schemas = Array.AsReadOnly(schemas.Select(s => s with { Definition = s.Definition.Clone() }).ToArray());
        Operations = Array.AsReadOnly(operations.Select(o => o with { Definition = o.Definition.Clone() }).ToArray());
        References = Array.AsReadOnly(references.ToArray());
        Root = root.Clone();
    }
    public string Version { get; }
    public string Title { get; }
    public IReadOnlyCollection<ApiSchema> Schemas { get; }
    public IReadOnlyCollection<ApiOperation> Operations { get; }
    public IReadOnlyCollection<ApiReference> References { get; }
    public JsonElement Root { get; }

    /// <summary>Resolves an internal reference without expanding recursive schemas or loading files.</summary>
    public JsonElement? ResolveReference(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(reference))
        {
            var target = References.FirstOrDefault(r => r.Value == reference)?.TargetPointer;
            if (target is null) return null;
            var node = Root;
            if (target.Length > 0)
                foreach (var token in target[1..].Split('/'))
                {
                    var key = token.Replace("~1", "/").Replace("~0", "~");
                    if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var property)) node = property;
                    else if (node.ValueKind == JsonValueKind.Array && int.TryParse(key, out var index) && index >= 0 && index < node.GetArrayLength()) node = node[index];
                    else return null;
                }
            if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("$ref", out var next)) return node;
            if (next.ValueKind != JsonValueKind.String) return null;
            reference = next.GetString()!;
        }
        return null;
    }
}

public sealed class ApiDocumentLoadResult
{
    public ApiDocumentLoadResult(ApiDocument? document, IEnumerable<OntologyDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public ApiDocument? Document { get; }
    public IReadOnlyCollection<OntologyDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
