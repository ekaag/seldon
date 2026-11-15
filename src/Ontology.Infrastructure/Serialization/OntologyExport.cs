namespace Ontology.Infrastructure.Serialization;

internal sealed record OntologyExport(string SchemaVersion, OntologyDto Ontology);
internal sealed record OntologyDto(string Id, string Name, EntityDto[] Entities,
    RelationshipDto[] Relationships, ActionDto[] Actions);
internal sealed record ProvenanceDto(string Document, string SourcePointer);
internal sealed record PropertyDto(string Name, string DataType, bool Required,
    string? Description = null, ProvenanceDto? Provenance = null, string? Format = null,
    bool Nullable = false, string? ReferencedSchemaId = null,
    System.Text.Json.JsonElement[]? EnumValues = null, PropertyDto? Items = null, PropertyDto[]? Properties = null);
internal sealed record EntityDto(string Id, string Name, PropertyDto[] Properties,
    string[] ActionIds, string? Description = null, ProvenanceDto? Provenance = null);
internal sealed record RelationshipDto(string Id, string SourceEntityId, string TargetEntityId,
    string Name, string Cardinality, string EvidenceKind, string SourcePointer,
    string? SourcePropertyName = null);
internal sealed record ActionDto(string Id, string Name, string HttpMethod, string Path,
    string[] EntityIds, string[] ResponseSchemaIds, string SourcePointer,
    string? OperationId = null, string? RequestSchemaId = null, string? Summary = null,
    string? Description = null, string[]? Tags = null, ParameterDto[]? Parameters = null,
    SchemaBindingDto[]? RequestSchemas = null, ResponseDto[]? Responses = null,
    System.Text.Json.JsonElement[]? Security = null, bool SecurityInherited = false,
    bool RequestBodyRequired = false, AssociationDto[]? Associations = null);
internal sealed record ParameterDto(string Name, string Location, bool Required, string? Description,
    string DataType, string? SchemaId, string SourcePointer, string? Format = null);
internal sealed record SchemaBindingDto(string SchemaId, string? MediaType, string SourcePointer);
internal sealed record ResponseDto(string StatusCode, string? Description, SchemaBindingDto[] Schemas, string SourcePointer);
internal sealed record AssociationDto(string EntityId, string EvidenceKind, string SourcePointer);
