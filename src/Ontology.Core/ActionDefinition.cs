namespace Ontology.Core;

public sealed class ActionDefinition : IActionDefinition
{
    public ActionDefinition(string id, string name, string httpMethod, string path,
        IEnumerable<string> entityIds, IEnumerable<string> responseSchemaIds, string sourcePointer,
        string? operationId = null, string? requestSchemaId = null, string? summary = null,
        string? description = null, IEnumerable<string>? tags = null, IEnumerable<ActionParameter>? parameters = null,
        IEnumerable<ActionSchemaBinding>? requestSchemas = null, IEnumerable<ActionResponse>? responses = null,
        IEnumerable<System.Text.Json.JsonElement>? security = null, bool securityInherited = false,
        bool requestBodyRequired = false, IEnumerable<ActionEntityAssociation>? associations = null)
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
        Summary = summary;
        Description = description;
        Tags = ModelCollections.Ids(tags ?? [], nameof(tags));
        Parameters = ModelCollections.Copy(parameters ?? [], nameof(parameters));
        RequestSchemas = ModelCollections.Copy(requestSchemas ?? [], nameof(requestSchemas));
        Responses = ModelCollections.Copy(responses ?? [], nameof(responses));
        Security = Array.AsReadOnly((security ?? []).Select(value => value.Clone()).ToArray());
        SecurityInherited = securityInherited;
        RequestBodyRequired = requestBodyRequired;
        Associations = ModelCollections.Copy(associations ?? [], nameof(associations));
        foreach (var parameter in Parameters)
        {
            ModelCollections.Text(parameter.Name, nameof(parameters));
            ModelCollections.Text(parameter.Location, nameof(parameters));
            ModelCollections.Text(parameter.DataType, nameof(parameters));
            ArgumentNullException.ThrowIfNull(parameter.SourcePointer);
        }
        foreach (var binding in RequestSchemas.Concat(Responses.SelectMany(r => r.Schemas)))
        {
            ModelCollections.Text(binding.SchemaId, nameof(requestSchemas));
            ArgumentNullException.ThrowIfNull(binding.SourcePointer);
        }
        foreach (var association in Associations)
        {
            ModelCollections.Text(association.EntityId, nameof(associations));
            ModelCollections.Text(association.EvidenceKind, nameof(associations));
            ArgumentNullException.ThrowIfNull(association.SourcePointer);
        }
        foreach (var requirement in Security)
        {
            if (requirement.ValueKind != System.Text.Json.JsonValueKind.Object)
                throw new ArgumentException("Security requirements must be objects.", nameof(security));
            var schemes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var scheme in requirement.EnumerateObject())
                if (!schemes.Add(scheme.Name) || scheme.Value.ValueKind != System.Text.Json.JsonValueKind.Array ||
                    scheme.Value.EnumerateArray().Any(scope => scope.ValueKind != System.Text.Json.JsonValueKind.String))
                    throw new ArgumentException("Security schemes must have unique names and string-array scopes.", nameof(security));
        }
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
    public string? Summary { get; }
    public string? Description { get; }
    public IReadOnlyCollection<string> Tags { get; }
    public IReadOnlyCollection<ActionParameter> Parameters { get; }
    public IReadOnlyCollection<ActionSchemaBinding> RequestSchemas { get; }
    public IReadOnlyCollection<ActionResponse> Responses { get; }
    public IReadOnlyCollection<System.Text.Json.JsonElement> Security { get; }
    public bool SecurityInherited { get; }
    public bool RequestBodyRequired { get; }
    public IReadOnlyCollection<ActionEntityAssociation> Associations { get; }
}
