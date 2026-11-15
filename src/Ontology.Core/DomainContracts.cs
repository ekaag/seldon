namespace Ontology.Core;

/// <summary>A catalog of schema types and capabilities, not live records.</summary>
public interface IOntology
{
    string Id { get; }
    string Name { get; }
    IReadOnlyCollection<IEntityType> Entities { get; }
    IReadOnlyCollection<IRelationshipType> Relationships { get; }
    IReadOnlyCollection<IActionDefinition> Actions { get; }
    IEntityType? FindEntity(string entityId);
}

public interface IEntityType
{
    string Id { get; }
    string Name { get; }
    string? Description { get; }
    SourceProvenance? Provenance { get; }
    IReadOnlyCollection<IPropertyDefinition> Properties { get; }
    IReadOnlyCollection<string> ActionIds { get; }
}

public interface IPropertyDefinition
{
    string Name { get; }
    string DataType { get; }
    bool Required { get; }
    string? Description { get; }
    SourceProvenance? Provenance { get; }
}

public interface IRelationshipType
{
    string Id { get; }
    string SourceEntityId { get; }
    string TargetEntityId { get; }
    string Name { get; }
    string? SourcePropertyName { get; }
    string Cardinality { get; }
    string EvidenceKind { get; }
    string SourcePointer { get; }
}

public interface IActionDefinition
{
    string Id { get; }
    string Name { get; }
    string HttpMethod { get; }
    string Path { get; }
    string? OperationId { get; }
    IReadOnlyCollection<string> EntityIds { get; }
    string? RequestSchemaId { get; }
    IReadOnlyCollection<string> ResponseSchemaIds { get; }
    string SourcePointer { get; }
}
