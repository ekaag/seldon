using System.Text.Json;
using Ontology.Core;

namespace Ontology.Builder;

/// <summary>Deterministic schema-to-type extraction, without relationship or action inference.</summary>
public sealed class EntityExtractor : IEntityExtractor
{
    public EntityExtractionResult Extract(ApiDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();
        return new Extraction(document, ct).Run();
    }

    private sealed class Extraction(ApiDocument document, CancellationToken ct)
    {
        private readonly List<OntologyDiagnostic> diagnostics = [];
        private readonly HashSet<string> reported = new(StringComparer.Ordinal);
        private int visits;
        private string DocumentName => document.Schemas.FirstOrDefault()?.Provenance.Document ?? "";
        private static string Id(string pointer) => "schema:" + pointer;
        private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
        private static string? Text(JsonElement node, string field) => node.ValueKind == JsonValueKind.Object &&
            node.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        private void Warn(string code, string pointer, string message)
        {
            if (reported.Add(code + pointer)) diagnostics.Add(new(code, DiagnosticSeverity.Warning, message, new(DocumentName, pointer)));
        }

        private bool Guard(int depth, string pointer)
        {
            ct.ThrowIfCancellationRequested();
            if (++visits > 100_000) throw new InvalidOperationException("Entity extraction exceeded its schema traversal limit.");
            if (depth <= 32) return true;
            Warn("schema-depth-limit", pointer, "Schema expansion exceeded depth 32; remaining type information is unknown.");
            return false;
        }

        private (JsonElement Node, string Pointer)? Resolve(JsonElement node, string pointer)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (Text(node, "$ref") is { } reference)
            {
                var target = document.References.FirstOrDefault(r => r.Value == reference)?.TargetPointer;
                if (target is null || !seen.Add(target))
                {
                    Warn("unresolved-schema-reference", pointer, $"Reference '{reference}' cannot be expanded locally (missing, external, or cyclic).");
                    return null;
                }
                var resolved = document.Schemas.FirstOrDefault(s => s.Provenance.SourcePointer == target);
                if (resolved is null)
                {
                    Warn("unsupported-schema-target", pointer, $"Reference '{reference}' does not target a cataloged schema.");
                    return null;
                }
                node = resolved.Definition;
                pointer = target;
            }
            return (node, pointer);
        }

        private bool Object(JsonElement node, string pointer, HashSet<string> seen)
        {
            if (!seen.Add(pointer)) return false;
            var resolved = Resolve(node, pointer);
            if (resolved is null) return false;
            (node, pointer) = resolved.Value;
            if (node.ValueKind != JsonValueKind.Object) return false;
            if (Text(node, "type") == "object" || node.TryGetProperty("properties", out _)) return true;
            if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == "object")) return true;
            return node.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array &&
                all.EnumerateArray().Select((child, i) => (child, i)).Any(pair => Object(pair.child, pointer + "/allOf/" + pair.i, seen));
        }

        internal EntityExtractionResult Run()
        {
            var entities = new List<IEntityType>();
            foreach (var schema in document.Schemas.OrderBy(s => s.Provenance.SourcePointer, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var pointer = schema.Provenance.SourcePointer;
                // Inline body objects (including array items) are entities; nested property objects are values.
                var itemRoot = pointer;
                while (itemRoot.EndsWith("/items", StringComparison.Ordinal)) itemRoot = itemRoot[..^6];
                var inlineRoot = itemRoot.EndsWith("/schema", StringComparison.Ordinal) ||
                    (itemRoot != pointer && document.Schemas.Any(s => s.Name is not null && s.Provenance.SourcePointer == itemRoot));
                if (schema.Name is null && (!inlineRoot || pointer.Contains("/properties/", StringComparison.Ordinal))) continue;
                if (!Object(schema.Definition, pointer, [])) continue;
                var resolved = Resolve(schema.Definition, pointer)!.Value;
                // An operation's reference to a named type is not another inline entity.
                if (schema.Name is null && Text(schema.Definition, "$ref") is not null &&
                    document.Schemas.Any(s => s.Name is not null && s.Provenance.SourcePointer == resolved.Pointer)) continue;
                entities.Add(new EntityType(Id(pointer), schema.Name ?? "Inline:" + pointer,
                    Properties(resolved.Node, resolved.Pointer, [], 0), [],
                    Text(schema.Definition, "description") ?? Text(resolved.Node, "description"), schema.Provenance));
            }
            // Surface unsupported unions even when no object candidate can be inferred safely.
            foreach (var schema in document.Schemas)
                UnionWarnings(schema.Definition, schema.Provenance.SourcePointer);
            return new(entities, diagnostics.OrderBy(d => d.Provenance?.SourcePointer, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal));
        }

        private void UnionWarnings(JsonElement node, string pointer)
        {
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                Warn("unsupported-items-list", pointer + "/items",
                    "A list of item schemas is preserved but not interpreted as a union or a single item type; extraction may be partial.");
            foreach (var keyword in new[] { "oneOf", "anyOf" })
                if (node.TryGetProperty(keyword, out _)) Warn("unsupported-" + keyword, pointer + "/" + keyword,
                    $"{keyword} alternatives are not merged; the property's type may be unknown.");
        }

        private IReadOnlyCollection<IPropertyDefinition> Properties(JsonElement node, string pointer, HashSet<string> stack, int depth)
        {
            var fields = new Dictionary<string, (JsonElement Node, string Pointer)>(StringComparer.Ordinal);
            var required = new HashSet<string>(StringComparer.Ordinal);
            var conflicts = new HashSet<string>(StringComparer.Ordinal);
            void Gather(JsonElement current, string location, HashSet<string> path, int level)
            {
                if (!Guard(level, location)) return;
                var resolved = Resolve(current, location);
                if (resolved is null) return;
                (current, location) = resolved.Value;
                if (!path.Add(location))
                {
                    Warn("cyclic-composition", location, "Cyclic allOf composition was stopped.");
                    return;
                }
                if (current.ValueKind == JsonValueKind.Object)
                {
                    UnionWarnings(current, location);
                    if (current.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array)
                        foreach (var name in names.EnumerateArray()) if (name.ValueKind == JsonValueKind.String) required.Add(name.GetString()!);
                    if (current.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array)
                        foreach (var (child, i) in all.EnumerateArray().Select((child, i) => (child, i)))
                            Gather(child, location + "/allOf/" + i, new(path, StringComparer.Ordinal), level + 1);
                    if (current.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                        foreach (var entry in properties.EnumerateObject())
                        {
                            var fieldPointer = location + "/properties/" + Escape(entry.Name);
                            if (fields.TryGetValue(entry.Name, out var previous) && previous.Node.GetRawText() != entry.Value.GetRawText())
                            {
                                conflicts.Add(entry.Name);
                                Warn("conflicting-allOf-property", fieldPointer, $"Multiple constraints on '{entry.Name}' cannot be safely merged; its type is unknown.");
                            }
                            else fields.TryAdd(entry.Name, (entry.Value, fieldPointer));
                        }
                }
            }
            Gather(node, pointer, new(stack, StringComparer.Ordinal), depth);
            return fields.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => conflicts.Contains(f.Key)
                ? (IPropertyDefinition)new PropertyDefinition(f.Key, "unknown", required.Contains(f.Key), provenance: new(DocumentName, f.Value.Pointer))
                : Property(f.Key, f.Value.Node, f.Value.Pointer, required.Contains(f.Key), depth + 1)).ToArray();
        }

        private IPropertyDefinition Property(string name, JsonElement original, string pointer, bool required, int depth)
        {
            if (!Guard(depth, pointer)) return new PropertyDefinition(name, "unknown", required, provenance: new(DocumentName, pointer));
            var resolved = Resolve(original, pointer);
            var node = resolved?.Node ?? original;
            var resolvedPointer = resolved?.Pointer ?? pointer;
            UnionWarnings(node, resolvedPointer);
            var referenceId = Text(original, "$ref") is not null && resolved is not null ? Id(resolvedPointer) : null;
            var type = Text(node, "type");
            var nullable = node.ValueKind == JsonValueKind.Object && node.TryGetProperty("nullable", out var nullableNode) && nullableNode.ValueKind == JsonValueKind.True;
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("type", out var types) && types.ValueKind == JsonValueKind.Array)
            {
                var names = types.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).ToArray();
                nullable |= names.Contains("null", StringComparer.Ordinal);
                var concrete = names.Where(n => n != "null").Distinct(StringComparer.Ordinal).ToArray();
                type = concrete.Length == 1 ? concrete[0] : "unknown";
                if (concrete.Length > 1) Warn("unsupported-type-union", resolvedPointer, "Multiple non-null types are retained as unknown.");
            }
            if (type is null && Object(node, resolvedPointer, [])) type = "object";
            IPropertyDefinition? items = null;
            if (type == "array" && node.TryGetProperty("items", out var item))
                items = Property("items", item, resolvedPointer + "/items", false, depth + 1);
            var nested = type == "object" && referenceId is null ? Properties(node, resolvedPointer, [], depth + 1) : [];
            var values = node.ValueKind == JsonValueKind.Object && node.TryGetProperty("enum", out var enums) && enums.ValueKind == JsonValueKind.Array
                ? enums.EnumerateArray().ToArray() : [];
            return new PropertyDefinition(name, type ?? "unknown", required,
                Text(original, "description") ?? Text(node, "description"), new(DocumentName, pointer),
                Text(node, "format"), nullable, referenceId, values, items, nested);
        }
    }
}
