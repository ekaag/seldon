namespace Ontology.Core;

public sealed class ActionDefinition : IActionDefinition
{
    public ActionDefinition(string id, string name, string httpMethod, string path,
        IEnumerable<string> entityIds, IEnumerable<string> responseSchemaIds, string sourcePointer,
        string? operationId = null, string? requestSchemaId = null)
    {
        Id = ModelCollections.Text(id, nameof(id));
        Name = ModelCollections.Text(name, nameof(name));
        HttpMethod = ModelCollections.Text(httpMethod, nameof(httpMethod));
        Path = ModelCollections.Text(path, nameof(path));
        EntityIds = ModelCollections.Ids(entityIds, nameof(entityIds));
        ResponseSchemaIds = ModelCollections.Ids(responseSchemaIds, nameof(responseSchemaIds));
        ArgumentNullException.ThrowIfNull(sourcePointer);
        SourcePointer = sourcePointer;
        OperationId = operationId;
        RequestSchemaId = requestSchemaId;
    }

    public string Id { get; }
    public string Name { get; }
    public string HttpMethod { get; }
    public string Path { get; }
    public string? OperationId { get; }
    public IReadOnlyCollection<string> EntityIds { get; }
    public string? RequestSchemaId { get; }
    public IReadOnlyCollection<string> ResponseSchemaIds { get; }
    public string SourcePointer { get; }
}
