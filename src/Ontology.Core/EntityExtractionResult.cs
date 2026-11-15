namespace Ontology.Core;

public interface IEntityExtractor
{
    EntityExtractionResult Extract(ApiDocument document, CancellationToken ct);
}

public sealed class EntityExtractionResult
{
    public EntityExtractionResult(IEnumerable<IEntityType> entities, IEnumerable<OntologyDiagnostic> diagnostics)
    {
        Entities = Array.AsReadOnly(entities.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public IReadOnlyCollection<IEntityType> Entities { get; }
    public IReadOnlyCollection<OntologyDiagnostic> Diagnostics { get; }
}
