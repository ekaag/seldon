using System.Text.Json;
using Ontology.Core;

namespace Ontology.Builder;

/// <summary>Structural edges and explicitly uncertain, exact-name foreign-key candidates.</summary>
public sealed class RelationshipExtractor : IRelationshipExtractor
{
    public RelationshipExtractionResult Extract(ApiDocument document, IEnumerable<IEntityType> entities, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entities);
        ct.ThrowIfCancellationRequested();
        var catalog = entities.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var byId = catalog.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var schemas = document.Schemas.ToDictionary(s => s.Provenance.SourcePointer, StringComparer.Ordinal);
        var named = document.Schemas.Where(s => s.Name is not null).Select(s => "schema:" + s.Provenance.SourcePointer).ToHashSet(StringComparer.Ordinal);
        var relationships = new Dictionary<string, IRelationshipType>(StringComparer.Ordinal);
        var diagnostics = new Dictionary<string, OntologyDiagnostic>(StringComparer.Ordinal);
        var visits = 0;

        void Warn(string code, string message, SourceProvenance? provenance)
        {
            var key = code + ":" + provenance?.SourcePointer + ":" + message;
            diagnostics.TryAdd(key, new(code, DiagnosticSeverity.Warning, message, provenance));
        }

        bool Guard(int depth, SourceProvenance? provenance)
        {
            ct.ThrowIfCancellationRequested();
            if (++visits > 100_000) throw new InvalidOperationException("Relationship extraction exceeded its traversal limit.");
            if (depth <= 32) return true;
            Warn("relationship-depth-limit", "Relationship traversal stopped at depth 32.", provenance);
            return false;
        }

        void Add(IEntityType source, string target, string semanticPath, string label, string cardinality,
            string evidence, string pointer, string? sourceProperty)
        {
            // Encode components separately so separators in schema/property names cannot collide.
            var id = "relationship:" + string.Join(":", new[] { source.Id, semanticPath, target, evidence }.Select(Uri.EscapeDataString));
            relationships.TryAdd(id, new RelationshipType(id, source.Id, target, label, cardinality, evidence, pointer, sourceProperty));
        }

        void Property(IEntityType source, IPropertyDefinition property, string semanticPath,
            string label, string topProperty, bool collection, int depth)
        {
            if (!Guard(depth, property.Provenance)) return;
            collection |= property.DataType == "array";
            if (property.ReferencedSchemaId is { } target)
            {
                if (byId.ContainsKey(target))
                {
                    Add(source, target, semanticPath, label, collection ? "many" : "one", "ExplicitSchemaRef",
                        (property.Provenance?.SourcePointer ?? "") + "/$ref", topProperty);
                    return;
                }
                // Primitive schema references are legitimate properties, not entity relationships.
                if (property.DataType == "object")
                    Warn("relationship-target-not-entity", $"Property '{label}' references '{target}', which is absent from the entity catalog.", property.Provenance);
            }
            if (property.Items is { } items)
                Property(source, items, semanticPath + "/items", label, topProperty, true, depth + 1);
            foreach (var child in property.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
                Property(source, child, semanticPath + "/properties/" + Escape(child.Name), label + "." + child.Name, topProperty, collection, depth + 1);

            // Only scalar fields without explicit schema references can supply heuristic evidence.
            if (property.ReferencedSchemaId is not null || property.DataType is not ("string" or "integer") ||
                !property.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase) || property.Name.Length <= 2) return;
            var stem = property.Name[..^2].TrimEnd('_', '-');
            if (stem.Length == 0) return;
            var matches = catalog.Where(e => named.Contains(e.Id) && string.Equals(e.Name, stem, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0) return;
            if (matches.Length > 1)
            {
                Warn("ambiguous-foreign-key", $"Field '{label}' matches multiple entities ({string.Join(", ", matches.Select(e => e.Id))}); no relationship was created.", property.Provenance);
                return;
            }
            var candidate = matches[0];
            Add(source, candidate.Id, semanticPath, label, collection ? "many" : "unknown", "Heuristic",
                property.Provenance?.SourcePointer ?? "", topProperty);
            Warn("heuristic-relationship-candidate", $"Unverified candidate '{source.Id}' → '{candidate.Id}' from field '{label}' by exact entity-name matching; review before use.", property.Provenance);
        }

        void Composition(IEntityType source, JsonElement node, string pointer, HashSet<string> stack, int depth)
        {
            if (!Guard(depth, source.Provenance) || node.ValueKind != JsonValueKind.Object) return;
            if (!stack.Add(pointer))
            {
                Warn("cyclic-relationship-composition", "Cyclic composition traversal was stopped.", new(source.Provenance?.Document ?? "", pointer));
                return;
            }
            if (!node.TryGetProperty("allOf", out var all) || all.ValueKind != JsonValueKind.Array) return;
            foreach (var (child, index) in all.EnumerateArray().Select((child, index) => (child, index)))
            {
                var location = pointer + "/allOf/" + index;
                if (child.ValueKind == JsonValueKind.Object && child.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String)
                {
                    var value = reference.GetString()!;
                    var targetPointer = document.References.FirstOrDefault(r => r.Value == value)?.TargetPointer;
                    if (targetPointer is not null && byId.ContainsKey("schema:" + targetPointer))
                        // Repeated allOf references to the same base have identical semantics.
                        Add(source, "schema:" + targetPointer, "/allOf", "allOf", "unknown", "ExplicitSchemaRef", location + "/$ref", null);
                    else
                        Warn("unresolved-composition-target", $"Composition reference '{value}' has no entity target.", new(source.Provenance?.Document ?? "", location + "/$ref"));
                }
                else Composition(source, child, location, new(stack, StringComparer.Ordinal), depth + 1);
            }
        }

        foreach (var entity in catalog)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var property in entity.Properties.OrderBy(p => p.Name, StringComparer.Ordinal))
                Property(entity, property, "/properties/" + Escape(property.Name), property.Name, property.Name, false, 0);
            if (entity.Provenance is { } provenance && schemas.TryGetValue(provenance.SourcePointer, out var schema))
                Composition(entity, schema.Definition, provenance.SourcePointer, [], 0);
        }
        return new(relationships.Values.OrderBy(r => r.Id, StringComparer.Ordinal),
            diagnostics.Values.OrderBy(d => d.Provenance?.SourcePointer, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal));
    }

    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
}
