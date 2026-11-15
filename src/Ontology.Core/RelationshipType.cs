namespace Ontology.Core;

public sealed class RelationshipType : IRelationshipType
{
    public RelationshipType(string id, string sourceEntityId, string targetEntityId, string name,
        string cardinality, string evidenceKind, string sourcePointer, string? sourcePropertyName = null)
    {
        Id = ModelCollections.Text(id, nameof(id));
        SourceEntityId = ModelCollections.Text(sourceEntityId, nameof(sourceEntityId));
        TargetEntityId = ModelCollections.Text(targetEntityId, nameof(targetEntityId));
        Name = ModelCollections.Text(name, nameof(name));
        Cardinality = ModelCollections.Text(cardinality, nameof(cardinality));
        EvidenceKind = ModelCollections.Text(evidenceKind, nameof(evidenceKind));
        ArgumentNullException.ThrowIfNull(sourcePointer);
        SourcePointer = sourcePointer;
        SourcePropertyName = sourcePropertyName;
    }

    public string Id { get; }
    public string SourceEntityId { get; }
    public string TargetEntityId { get; }
    public string Name { get; }
    public string Cardinality { get; }
    public string EvidenceKind { get; }
    public string SourcePointer { get; }
    public string? SourcePropertyName { get; }
}
