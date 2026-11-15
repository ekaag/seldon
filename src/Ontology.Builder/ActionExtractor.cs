using System.Text.Json;
using Ontology.Core;

namespace Ontology.Builder;

public sealed class ActionExtractor : IActionExtractor
{
    public ActionExtractionResult Extract(ApiDocument document, IEnumerable<IEntityType> entities, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(entities);
        ct.ThrowIfCancellationRequested();
        var catalog = entities.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var byId = catalog.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var named = document.Schemas.Where(s => s.Name is not null).Select(s => "schema:" + s.Provenance.SourcePointer).ToHashSet(StringComparer.Ordinal);
        var diagnostics = new List<OntologyDiagnostic>();
        var actions = new List<IActionDefinition>();
        var operationIds = document.Operations.Select(o => Text(o.Definition, "operationId")).Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        void Warn(string code, string message, string pointer) => diagnostics.Add(new(code, DiagnosticSeverity.Warning, message,
            new(document.Schemas.FirstOrDefault()?.Provenance.Document ?? document.Operations.FirstOrDefault()?.Provenance.Document ?? "", pointer)));

        JsonElement? At(string pointer)
        {
            var node = document.Root;
            if (pointer.Length == 0) return node;
            foreach (var token in pointer[1..].Split('/'))
            {
                var key = token.Replace("~1", "/").Replace("~0", "~");
                if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var child)) node = child;
                else if (node.ValueKind == JsonValueKind.Array && int.TryParse(key, out var i) && i >= 0 && i < node.GetArrayLength()) node = node[i];
                else return null;
            }
            return node;
        }
        (JsonElement Node, string Pointer)? Resolve(JsonElement node, string pointer)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (Text(node, "$ref") is { } reference)
            {
                ct.ThrowIfCancellationRequested();
                var target = document.References.FirstOrDefault(r => r.Value == reference)?.TargetPointer;
                if (target is null || !seen.Add(target) || At(target) is not { } resolved)
                {
                    Warn("unresolved-action-reference", $"Reference '{reference}' cannot be resolved locally.", pointer + "/$ref");
                    return null;
                }
                node = resolved;
                pointer = target;
            }
            return (node, pointer);
        }

        foreach (var operation in document.Operations.OrderBy(o => o.Provenance.SourcePointer, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var node = operation.Definition;
            var pointer = operation.Provenance.SourcePointer;
            var operationId = Text(node, "operationId");
            var id = !string.IsNullOrWhiteSpace(operationId) && operationIds[operationId] == 1
                ? "operation:" + Uri.EscapeDataString(operationId)
                : "operation:pointer:" + Uri.EscapeDataString(pointer);
            if (!string.IsNullOrWhiteSpace(operationId) && operationIds[operationId] > 1)
                Warn("duplicate-operation-id", $"Duplicate operationId '{operationId}'; source-pointer identity is used.", pointer);
            var associations = new Dictionary<string, ActionEntityAssociation>(StringComparer.Ordinal);
            void Associate(string entityId, string evidence, string evidencePointer)
            {
                if (byId.ContainsKey(entityId)) associations.TryAdd(entityId + ":" + evidence + ":" + evidencePointer, new(entityId, evidence, evidencePointer));
            }
            var visits = 0;
            void SchemaEvidence(JsonElement schema, string location, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (++visits > 100_000) throw new InvalidOperationException("Action schema traversal limit exceeded.");
                if (depth > 32) { Warn("action-schema-depth-limit", "Schema association stopped at depth 32.", location); return; }
                var resolved = Resolve(schema, location);
                if (resolved is null) return;
                var (value, resolvedPointer) = resolved.Value;
                var entityId = "schema:" + resolvedPointer;
                if (byId.ContainsKey(entityId))
                {
                    Associate(entityId, Text(schema, "$ref") is null ? "InlineSchema" : "ExplicitSchemaRef", location);
                    return;
                }
                if (value.ValueKind != JsonValueKind.Object) return;
                if (value.TryGetProperty("items", out var items)) SchemaEvidence(items, resolvedPointer + "/items", depth + 1);
                if (value.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                    foreach (var property in properties.EnumerateObject()) SchemaEvidence(property.Value, resolvedPointer + "/properties/" + Escape(property.Name), depth + 1);
                foreach (var keyword in new[] { "allOf", "oneOf", "anyOf" })
                    if (value.TryGetProperty(keyword, out var array) && array.ValueKind == JsonValueKind.Array)
                    {
                        if (keyword != "allOf") Warn("conditional-action-schema", $"{keyword} references describe possible entity types, not guaranteed outputs.", resolvedPointer + "/" + keyword);
                        foreach (var (child, i) in array.EnumerateArray().Select((child, i) => (child, i))) SchemaEvidence(child, resolvedPointer + "/" + keyword + "/" + i, depth + 1);
                    }
            }
            ActionSchemaBinding Binding(JsonElement schema, string location, string? mediaType)
            {
                SchemaEvidence(schema, location, 0);
                var resolved = Resolve(schema, location);
                return new("schema:" + (resolved?.Pointer ?? location), mediaType, location);
            }
            List<ActionSchemaBinding> Content(JsonElement container, string location)
            {
                var bindings = new List<ActionSchemaBinding>();
                if (container.ValueKind != JsonValueKind.Object) return bindings;
                if (container.TryGetProperty("schema", out var schema)) bindings.Add(Binding(schema, location + "/schema", null));
                if (container.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Object)
                    foreach (var media in content.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                        if (media.Value.ValueKind == JsonValueKind.Object && media.Value.TryGetProperty("schema", out var mediaSchema))
                            bindings.Add(Binding(mediaSchema, location + "/content/" + Escape(media.Name) + "/schema", media.Name));
                return bindings;
            }
            var parameters = new Dictionary<string, ActionParameter>(StringComparer.Ordinal);
            var parameterDefinitions = new Dictionary<string, (JsonElement Node, string Pointer)>(StringComparer.Ordinal);
            var requests = new List<ActionSchemaBinding>();
            var requestRequired = false;
            void Parameters(JsonElement container, string location)
            {
                if (container.ValueKind != JsonValueKind.Object || !container.TryGetProperty("parameters", out var array) || array.ValueKind != JsonValueKind.Array) return;
                foreach (var (parameter, i) in array.EnumerateArray().Select((parameter, i) => (parameter, i)))
                {
                    var resolved = Resolve(parameter, location + "/parameters/" + i);
                    if (resolved is null) continue;
                    var (value, parameterPointer) = resolved.Value;
                    var name = Text(value, "name");
                    var place = Text(value, "in");
                    if (name is null || place is null) { Warn("invalid-action-parameter", "Parameter name and location are required.", parameterPointer); continue; }
                    parameterDefinitions[place + ":" + (place == "header" ? name.ToLowerInvariant() : name)] = (value, parameterPointer);
                }
            }
            var pathPointer = pointer[..pointer.LastIndexOf('/')];
            if (At(pathPointer) is { } pathItem && Resolve(pathItem, pathPointer) is { } resolvedPath)
                Parameters(resolvedPath.Node, resolvedPath.Pointer);
            Parameters(node, pointer);
            foreach (var (key, parameterDefinition) in parameterDefinitions.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                    var (value, parameterPointer) = parameterDefinition;
                    var name = Text(value, "name")!;
                    var place = Text(value, "in")!;
                    JsonElement? schema = value.TryGetProperty("schema", out var typedSchema) ? typedSchema : null;
                    var schemaBinding = schema is { } s ? Binding(s, parameterPointer + "/schema", null) : null;
                    var definition = schema is { } d ? Resolve(d, parameterPointer + "/schema")?.Node ?? d : value;
                    var metadata = new ActionParameter(name, place, Flag(value, "required"), Text(value, "description"),
                        Text(definition, "type") ?? "unknown", schemaBinding?.SchemaId, parameterPointer, Text(definition, "format"));
                    parameters[key] = metadata;
                    if (place == "body" && schemaBinding is not null) { requests.Add(schemaBinding); requestRequired |= metadata.Required; }
                    if (value.TryGetProperty("content", out _))
                    {
                        var contentSchemas = Content(value, parameterPointer);
                        if (contentSchemas.Count == 1) parameters[key] = metadata with { SchemaId = contentSchemas[0].SchemaId };
                    }
            }
            if (node.TryGetProperty("requestBody", out var request) && Resolve(request, pointer + "/requestBody") is { } body)
            {
                requestRequired = Flag(body.Node, "required");
                requests.AddRange(Content(body.Node, body.Pointer));
            }
            var responses = new List<ActionResponse>();
            if (node.TryGetProperty("responses", out var responseMap) && responseMap.ValueKind == JsonValueKind.Object)
                foreach (var response in responseMap.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    var responsePointer = pointer + "/responses/" + Escape(response.Name);
                    if (Resolve(response.Value, responsePointer) is not { } resolvedResponse) continue;
                    responses.Add(new(response.Name, Text(resolvedResponse.Node, "description"), Content(resolvedResponse.Node, resolvedResponse.Pointer), responsePointer));
                }

            // Schema evidence wins for each entity. Route matching supplements nested route context.
            var schemaEntities = associations.Values.Select(a => a.EntityId).ToHashSet(StringComparer.Ordinal);
            foreach (var token in operation.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var variable = token.StartsWith('{') && token.EndsWith('}');
                var term = variable ? token[1..^1] : token;
                if (variable)
                {
                    if (!parameters.Values.Any(p => p.Location == "path" && p.Name == term) || !term.EndsWith("id", StringComparison.OrdinalIgnoreCase) || term.Length <= 2) continue;
                    term = term[..^2].TrimEnd('_', '-');
                }
                var matches = catalog.Where(e => named.Contains(e.Id) && (string.Equals(e.Name, term, StringComparison.OrdinalIgnoreCase) ||
                    (!variable && string.Equals(e.Name + "s", term, StringComparison.OrdinalIgnoreCase)))).ToArray();
                if (matches.Length > 1) { Warn("ambiguous-action-route", $"Route token '{token}' matches multiple entity types; no fallback association was added.", pointer); continue; }
                if (matches.Length == 1 && !schemaEntities.Contains(matches[0].Id))
                {
                    Associate(matches[0].Id, variable ? "ParameterHeuristic" : "RouteHeuristic", pointer);
                    Warn("heuristic-action-association", $"Unverified route/parameter association with '{matches[0].Id}' from '{token}'.", pointer);
                }
            }
            var securityInherited = !node.TryGetProperty("security", out var security);
            if (securityInherited) document.Root.TryGetProperty("security", out security);
            var securityValues = security.ValueKind == JsonValueKind.Array ? security.EnumerateArray().ToArray() : [];
            var requestIds = requests.Select(r => r.SchemaId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (requestIds.Length > 1) Warn("multiple-request-schemas", "Multiple request schemas are preserved in requestSchemas; requestSchemaId is null.", pointer);
            if (associations.Count == 0) Warn("unbound-action", $"Operation '{operation.HttpMethod} {operation.Path}' has no confident entity association and remains unbound.", pointer);
            var tags = node.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array
                ? tagArray.EnumerateArray().Where(t => t.ValueKind == JsonValueKind.String).Select(t => t.GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : [];
            actions.Add(new ActionDefinition(id, !string.IsNullOrWhiteSpace(operationId) ? operationId :
                !string.IsNullOrWhiteSpace(Text(node, "summary")) ? Text(node, "summary")! : operation.HttpMethod + " " + operation.Path,
                operation.HttpMethod, operation.Path, associations.Values.Select(a => a.EntityId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                responses.SelectMany(r => r.Schemas).Select(s => s.SchemaId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), pointer,
                operationId, requestIds.Length == 1 ? requestIds[0] : null, Text(node, "summary"), Text(node, "description"), tags,
                parameters.Values.OrderBy(p => p.Location, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal), requests, responses,
                securityValues, securityInherited, requestRequired, associations.Values.OrderBy(a => a.EntityId, StringComparer.Ordinal).ThenBy(a => a.EvidenceKind, StringComparer.Ordinal).ThenBy(a => a.SourcePointer, StringComparer.Ordinal)));
        }
        var linked = catalog.Select(e => (IEntityType)new EntityType(e.Id, e.Name, e.Properties,
            actions.Where(a => a.EntityIds.Contains(e.Id, StringComparer.Ordinal)).Select(a => a.Id).Order(StringComparer.Ordinal), e.Description, e.Provenance));
        return new(linked, actions.OrderBy(a => a.Id, StringComparer.Ordinal), diagnostics.OrderBy(d => d.Provenance?.SourcePointer, StringComparer.Ordinal).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal));
    }

    private static string? Text(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool Flag(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    private static string Escape(string value) => value.Replace("~", "~0").Replace("/", "~1");
}
