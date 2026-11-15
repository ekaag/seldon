namespace Ontology.Core;

public interface IRelationshipExtractor
{
    RelationshipExtractionResult Extract(ApiDocument document, IEnumerable<IEntityType> entities, CancellationToken ct);
}

public sealed class RelationshipExtractionResult
{
    public RelationshipExtractionResult(IEnumerable<IRelationshipType> relationships, IEnumerable<OntologyDiagnostic> diagnostics)
    {
        Relationships = Array.AsReadOnly(relationships.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public IReadOnlyCollection<IRelationshipType> Relationships { get; }
    public IReadOnlyCollection<OntologyDiagnostic> Diagnostics { get; }
}
