namespace Ontology.Infrastructure.Serialization;

internal sealed record OntologyExport(string SchemaVersion, OntologyDto Ontology);
internal sealed record OntologyDto(string Id, string Name, EntityDto[] Entities,
    RelationshipDto[] Relationships, ActionDto[] Actions);
internal sealed record ProvenanceDto(string Document, string SourcePointer);
internal sealed record PropertyDto(string Name, string DataType, bool Required,
    string? Description = null, ProvenanceDto? Provenance = null);
internal sealed record EntityDto(string Id, string Name, PropertyDto[] Properties,
    string[] ActionIds, string? Description = null, ProvenanceDto? Provenance = null);
internal sealed record RelationshipDto(string Id, string SourceEntityId, string TargetEntityId,
    string Name, string Cardinality, string EvidenceKind, string SourcePointer,
    string? SourcePropertyName = null);
internal sealed record ActionDto(string Id, string Name, string HttpMethod, string Path,
    string[] EntityIds, string[] ResponseSchemaIds, string SourcePointer,
    string? OperationId = null, string? RequestSchemaId = null);
