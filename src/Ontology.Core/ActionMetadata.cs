namespace Ontology.Core;

public sealed record ActionParameter(string Name, string Location, bool Required, string? Description,
    string DataType, string? SchemaId, string SourcePointer, string? Format = null);
public sealed record ActionSchemaBinding(string SchemaId, string? MediaType, string SourcePointer);
public sealed record ActionEntityAssociation(string EntityId, string EvidenceKind, string SourcePointer);

public sealed class ActionResponse
{
    public ActionResponse(string statusCode, string? description, IEnumerable<ActionSchemaBinding> schemas, string sourcePointer)
    {
        StatusCode = ModelCollections.Text(statusCode, nameof(statusCode));
        Description = description;
        Schemas = ModelCollections.Copy(schemas, nameof(schemas));
        ArgumentNullException.ThrowIfNull(sourcePointer);
        SourcePointer = sourcePointer;
    }
    public string StatusCode { get; }
    public string? Description { get; }
    public IReadOnlyCollection<ActionSchemaBinding> Schemas { get; }
    public string SourcePointer { get; }
}

public interface IActionExtractor
{
    ActionExtractionResult Extract(ApiDocument document, IEnumerable<IEntityType> entities, CancellationToken ct);
}

public sealed class ActionExtractionResult
{
    public ActionExtractionResult(IEnumerable<IEntityType> entities, IEnumerable<IActionDefinition> actions, IEnumerable<OntologyDiagnostic> diagnostics)
    {
        Entities = Array.AsReadOnly(entities.ToArray());
        Actions = Array.AsReadOnly(actions.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public IReadOnlyCollection<IEntityType> Entities { get; }
    public IReadOnlyCollection<IActionDefinition> Actions { get; }
    public IReadOnlyCollection<OntologyDiagnostic> Diagnostics { get; }
}
