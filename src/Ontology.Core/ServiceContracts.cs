namespace Ontology.Core;

public interface IOntologyFactory
{
    IOntology Create(string id, string name, IEnumerable<IEntityType> entities,
        IEnumerable<IRelationshipType> relationships, IEnumerable<IActionDefinition> actions);
}

public interface IOntologyBuilder
{
    Task<OntologyBuildResult> BuildAsync(string inputPath, CancellationToken ct);
}

public interface IOntologyRepository
{
    Task SaveAsync(IOntology ontology, CancellationToken ct);
    Task<IOntology?> GetAsync(string id, CancellationToken ct);
}

public interface IOntologySerializer
{
    Task WriteAsync(IOntology ontology, Stream destination, CancellationToken ct);
    Task<IOntology> ReadAsync(Stream source, CancellationToken ct);
}
