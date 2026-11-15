using System.Text.Json;
using System.Text.Json.Serialization;
using Ontology.Core;
using Ontology.Infrastructure.Serialization;

namespace Ontology.Infrastructure;

/// <summary>Versioned JSON transport. Caller-owned streams remain open.</summary>
public sealed class JsonOntologySerializer : IOntologySerializer
{
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 64
    };

    public async Task WriteAsync(IOntology ontology, Stream destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(ontology);
        ArgumentNullException.ThrowIfNull(destination);
        // Apply the same domain validation to export as import, including custom implementations.
        var snapshot = new InMemoryOntology(ontology.Id, ontology.Name, ontology.Entities, ontology.Relationships, ontology.Actions);
        var dto = new OntologyDto(snapshot.Id, snapshot.Name,
            snapshot.Entities.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => new EntityDto(
                e.Id, e.Name, e.Properties.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new PropertyDto(
                    p.Name, p.DataType, p.Required, p.Description, ToDto(p.Provenance))).ToArray(),
                Sort(e.ActionIds), e.Description, ToDto(e.Provenance))).ToArray(),
            snapshot.Relationships.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r => new RelationshipDto(
                r.Id, r.SourceEntityId, r.TargetEntityId, r.Name, r.Cardinality, r.EvidenceKind,
                r.SourcePointer, r.SourcePropertyName)).ToArray(),
            snapshot.Actions.OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => new ActionDto(
                a.Id, a.Name, a.HttpMethod, a.Path, Sort(a.EntityIds), Sort(a.ResponseSchemaIds),
                a.SourcePointer, a.OperationId, a.RequestSchemaId)).ToArray());
        await JsonSerializer.SerializeAsync(destination, new OntologyExport(SchemaVersion, dto), Options, ct);
    }

    public async Task<IOntology> ReadAsync(Stream source, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            using var document = await JsonDocument.ParseAsync(source, new JsonDocumentOptions { MaxDepth = 64 }, ct);
            RejectDuplicateMembers(document.RootElement);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.String)
                throw new JsonException("A string schemaVersion is required.");
            if (version.GetString() != SchemaVersion)
                throw new JsonException($"Unsupported ontology schemaVersion '{version.GetString()}'; expected '{SchemaVersion}'.");

            var export = document.Deserialize<OntologyExport>(Options)
                ?? throw new JsonException("Ontology export cannot be null.");
            var dto = export.Ontology;
            return new InMemoryOntology(dto.Id, dto.Name,
                dto.Entities.Select(e =>
                {
                    RequireItem(e);
                    return (IEntityType)new EntityType(e.Id, e.Name, e.Properties.Select(p =>
                    {
                        RequireItem(p);
                        return (IPropertyDefinition)new PropertyDefinition(p.Name, p.DataType, p.Required, p.Description, FromDto(p.Provenance));
                    }), e.ActionIds, e.Description, FromDto(e.Provenance));
                }),
                dto.Relationships.Select(r =>
                {
                    RequireItem(r);
                    return (IRelationshipType)new RelationshipType(r.Id, r.SourceEntityId, r.TargetEntityId,
                        r.Name, r.Cardinality, r.EvidenceKind, r.SourcePointer, r.SourcePropertyName);
                }),
                dto.Actions.Select(a =>
                {
                    RequireItem(a);
                    return (IActionDefinition)new ActionDefinition(a.Id, a.Name, a.HttpMethod, a.Path,
                        a.EntityIds, a.ResponseSchemaIds, a.SourcePointer, a.OperationId, a.RequestSchemaId);
                }));
        }
        catch (ArgumentException exception)
        {
            throw new JsonException($"Invalid ontology export: {exception.Message}", exception);
        }
    }

    private static string[] Sort(IEnumerable<string> values) => values.Order(StringComparer.Ordinal).ToArray();
    private static ProvenanceDto? ToDto(SourceProvenance? source) => source is null ? null : new(source.Document, source.SourcePointer);
    private static SourceProvenance? FromDto(ProvenanceDto? source) => source is null ? null : new(source.Document, source.SourcePointer);

    private static void RequireItem<T>(T? item) where T : class
    {
        if (item is null) throw new JsonException("Ontology collections cannot contain null entries.");
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate JSON member '{property.Name}'.");
                RejectDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateMembers(item);
    }
}
