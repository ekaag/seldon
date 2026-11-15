using System.Text.Json;
using Ontology.Builder;
using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.Extraction;

public class ActionExtractorTests
{
    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "seldon.sln"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "samples", "actions-openapi.yaml");
    }
    private static async Task<ApiDocument> Sample()
    {
        var loaded = await new OpenApiDocumentLoader().LoadAsync(SamplePath(), default);
        Assert.False(loaded.HasErrors, string.Join("; ", loaded.Diagnostics.Select(d => d.Message)));
        return loaded.Document!;
    }
    private static ActionExtractionResult Extract(ApiDocument document) => new ActionExtractor()
        .Extract(document, new EntityExtractor().Extract(document, default).Entities, default);

    [Fact]
    public async Task ExtractsReadWriteOperationsAndAssociatesBothRouteContextAndOutput()
    {
        var result = Extract(await Sample());
        Assert.Equal(4, result.Actions.Count);
        var read = result.Actions.Single(a => a.OperationId == "listRecords");
        Assert.Equal("GET", read.HttpMethod);
        Assert.Equal("/customers/{customerId}/records", read.Path);
        Assert.Equal("List related records", read.Summary);
        Assert.Equal("Read operation metadata", read.Description);
        Assert.Equal(new[] { "customers", "records" }, read.Tags);
        Assert.Equal(new[] { "schema:/components/schemas/Customer", "schema:/components/schemas/Record" }, read.EntityIds);
        Assert.Contains(read.Associations, a => a.EntityId.EndsWith("/Record") && a.EvidenceKind == "ExplicitSchemaRef");
        Assert.Contains(read.Associations, a => a.EntityId.EndsWith("/Customer") && a.EvidenceKind.EndsWith("Heuristic"));
        var write = result.Actions.Single(a => a.OperationId == "createRecord");
        Assert.Equal("POST", write.HttpMethod);
        Assert.True(write.RequestBodyRequired);
        Assert.Equal("schema:/components/schemas/Record", write.RequestSchemaId);
        Assert.Equal("application/json", Assert.Single(write.RequestSchemas).MediaType);
        Assert.Equal(new[] { "201", "400" }, write.Responses.Select(r => r.StatusCode));
        Assert.Empty(write.Responses.Single(r => r.StatusCode == "400").Schemas);
        foreach (var entity in result.Entities)
            Assert.Equal(result.Actions.Where(a => a.EntityIds.Contains(entity.Id)).Select(a => a.Id).Order(StringComparer.Ordinal), entity.ActionIds);
        _ = new InMemoryOntology("sample", "Sample", result.Entities, [], result.Actions);
    }

    [Fact]
    public async Task MergesPathAndOperationParametersWithOperationOverrides()
    {
        var read = Extract(await Sample()).Actions.Single(a => a.OperationId == "listRecords");
        Assert.Equal(3, read.Parameters.Count);
        var limit = read.Parameters.Single(p => p.Name == "limit");
        Assert.True(limit.Required);
        Assert.Equal("query", limit.Location);
        Assert.Equal("int32", limit.Format);
        Assert.Equal("integer", limit.DataType);
        Assert.True(read.Parameters.Single(p => p.Name == "customerId").Required);
        Assert.False(read.Parameters.Single(p => p.Name == "X-Trace").Required);
    }

    [Fact]
    public async Task PreservesSecurityInheritanceAndExplicitAnonymousOverride()
    {
        var result = Extract(await Sample());
        var secured = result.Actions.Single(a => a.OperationId == "createRecord");
        Assert.True(secured.SecurityInherited);
        Assert.True(Assert.Single(secured.Security).TryGetProperty("apiKey", out _));
        var anonymous = result.Actions.Single(a => a.Path == "/health");
        Assert.False(anonymous.SecurityInherited);
        Assert.Empty(anonymous.Security);
    }

    [Fact]
    public async Task KeepsUnboundActionsAndUsesStableIdsWithoutOperationId()
    {
        var result = Extract(await Sample());
        var health = result.Actions.Single(a => a.Path == "/health");
        Assert.Empty(health.EntityIds);
        Assert.Contains(result.Diagnostics, d => d.Code == "unbound-action" && d.Provenance!.SourcePointer == health.SourcePointer);
        var archive = result.Actions.Single(a => a.Path.EndsWith("/archive"));
        Assert.Null(archive.OperationId);
        Assert.StartsWith("operation:pointer:", archive.Id);
        Assert.Equal("schema:/components/schemas/Customer", Assert.Single(archive.EntityIds));
    }

    [Fact]
    public async Task FullActionMetadataRoundTripsWithDeterministicOrdering()
    {
        var document = await Sample();
        var result = Extract(document);
        var reversed = new ApiDocument(document.Version, document.Title, document.Schemas.Reverse(), document.Operations.Reverse(), document.References.Reverse(), document.Root);
        async Task<byte[]> Export(ActionExtractionResult value)
        {
            using var stream = new MemoryStream();
            await new JsonOntologySerializer().WriteAsync(new InMemoryOntology("sample", "Sample", value.Entities, [], value.Actions), stream, default);
            return stream.ToArray();
        }
        var bytes = await Export(result);
        Assert.Equal(bytes, await Export(Extract(reversed)));
        using var input = new MemoryStream(bytes);
        var restored = await new JsonOntologySerializer().ReadAsync(input, default);
        using var output = new MemoryStream();
        await new JsonOntologySerializer().WriteAsync(restored, output, default);
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(3, restored.Actions.Single(a => a.OperationId == "listRecords").Parameters.Count);
        Assert.True(restored.Actions.Single(a => a.OperationId == "createRecord").RequestBodyRequired);
    }

    private static async Task<ApiDocument> Load(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            var result = await new OpenApiDocumentLoader().LoadAsync(path, default);
            Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            return result.Document!;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SwaggerBodyAndReferencedResponsesBindEntities()
    {
        var document = await Load("""
            {"swagger":"2.0","info":{"title":"Swagger","version":"1"},
             "paths":{"/commands":{"post":{"parameters":[{"name":"payload","in":"body","required":true,"schema":{"$ref":"#/definitions/Item"}}],
             "responses":{"200":{"$ref":"#/responses/ItemResponse"}}}}},
             "responses":{"ItemResponse":{"description":"Item","schema":{"$ref":"#/definitions/Item"}}},
             "definitions":{"Item":{"type":"object","properties":{"id":{"type":"string"}}}}}
            """);
        var action = Assert.Single(Extract(document).Actions);
        Assert.Equal("schema:/definitions/Item", action.RequestSchemaId);
        Assert.Equal("schema:/definitions/Item", Assert.Single(action.ResponseSchemaIds));
        Assert.True(action.RequestBodyRequired);
        Assert.Equal("body", Assert.Single(action.Parameters).Location);
        Assert.Equal("schema:/definitions/Item", Assert.Single(action.EntityIds));
    }

    [Fact]
    public async Task MultipleRequestMediaSchemasArePreservedWithoutChoosingOne()
    {
        var document = await Load("""
            {"openapi":"3.1.0","info":{"title":"Multi","version":"1"},"paths":{"/commands":{"post":{
             "requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/A"}},"application/xml":{"schema":{"$ref":"#/components/schemas/B"}}}},
             "responses":{"204":{"description":"OK"}}}}},"components":{"schemas":{"A":{"type":"object"},"B":{"type":"object"}}}}
            """);
        var result = Extract(document);
        var action = Assert.Single(result.Actions);
        Assert.Null(action.RequestSchemaId);
        Assert.Equal(2, action.RequestSchemas.Count);
        Assert.Equal(2, action.EntityIds.Count);
        Assert.Contains(result.Diagnostics, d => d.Code == "multiple-request-schemas");
    }

    [Fact]
    public async Task AmbiguousRoutesRemainUnbound()
    {
        var document = await Load("""
            {"openapi":"3.0.3","info":{"title":"Ambiguous","version":"1"},"paths":{"/customers":{"get":{"responses":{"204":{"description":"OK"}}}}},
             "components":{"schemas":{"Customer":{"type":"object"},"customer":{"type":"object"}}}}
            """);
        var result = Extract(document);
        Assert.Empty(Assert.Single(result.Actions).EntityIds);
        Assert.Contains(result.Diagnostics, d => d.Code == "ambiguous-action-route");
        Assert.Contains(result.Diagnostics, d => d.Code == "unbound-action");
    }

    [Fact]
    public void MetadataCollectionsAreSnapshotsAndSecuritySurvivesSourceDisposal()
    {
        var parameters = new List<ActionParameter> { new("id", "path", true, null, "string", null, "/parameters/0") };
        var bindings = new List<ActionSchemaBinding> { new("schema:A", "application/json", "/schema") };
        var response = new ActionResponse("200", "OK", bindings, "/responses/200");
        ActionDefinition action;
        using (var json = JsonDocument.Parse("[{\"oauth\":[\"write\",\"read\"]}]"))
            action = new("operation:test", "Test", "GET", "/test", [], [], "", parameters: parameters,
                responses: [response], security: json.RootElement.EnumerateArray());
        parameters.Clear();
        bindings.Clear();
        Assert.Single(action.Parameters);
        Assert.Single(response.Schemas);
        Assert.Equal(2, Assert.Single(action.Security).GetProperty("oauth").GetArrayLength());
        Assert.Throws<NotSupportedException>(() => ((ICollection<ActionParameter>)action.Parameters).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<ActionSchemaBinding>)response.Schemas).Clear());
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var document = await Sample();
        Assert.ThrowsAny<OperationCanceledException>(() => new ActionExtractor().Extract(document, [], cancellation.Token));
    }

    [Fact]
    public async Task OverriddenSchemaParametersDoNotLeaveStaleEntityBindings()
    {
        var document = await Load("""
            {"openapi":"3.0.3","info":{"title":"Override","version":"1"},"paths":{"/commands":{
             "parameters":[{"name":"filter","in":"query","schema":{"$ref":"#/components/schemas/A"}}],
             "get":{"parameters":[{"name":"filter","in":"query","content":{"application/json":{"schema":{"$ref":"#/components/schemas/B"}}}}],
             "responses":{"204":{"description":"OK"}}}}},"components":{"schemas":{"A":{"type":"object"},"B":{"type":"object"}}}}
            """);
        var action = Assert.Single(Extract(document).Actions);
        Assert.Equal("schema:/components/schemas/B", Assert.Single(action.EntityIds));
        Assert.Empty(action.RequestSchemas);
        Assert.Null(action.RequestSchemaId);
        Assert.Equal("schema:/components/schemas/B", Assert.Single(action.Parameters).SchemaId);
    }

    [Fact]
    public async Task CallbackAndWebhookOperationsAreRetainedWithoutInvokingThem()
    {
        var document = await Load("""
            {"openapi":"3.1.0","info":{"title":"Notifications","version":"1"},"paths":{"/subscribe":{"post":{
             "callbacks":{"changed":{"{$request.query.callbackUrl}":{"post":{"requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Item"}}}},"responses":{"204":{"description":"OK"}}}}}},
             "responses":{"202":{"description":"Accepted"}}}}},
             "webhooks":{"itemChanged":{"post":{"requestBody":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/Item"}}}},"responses":{"204":{"description":"OK"}}}}},
             "components":{"schemas":{"Item":{"type":"object"}}}}
            """);
        var result = Extract(document);
        Assert.Equal(3, result.Actions.Count);
        Assert.Contains(result.Actions, a => a.SourcePointer.StartsWith("/webhooks/") && a.EntityIds.Count == 1);
        Assert.Contains(result.Actions, a => a.SourcePointer.Contains("/callbacks/") && a.EntityIds.Count == 1);
    }

    [Fact]
    public async Task DuplicateOperationIdsUseUniqueDeterministicPointerIdentities()
    {
        var document = await Sample();
        var operations = document.Operations.Select(operation =>
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(operation.Definition.GetRawText())!;
            node["operationId"] = "duplicate";
            return operation with { Definition = JsonSerializer.SerializeToElement(node) };
        });
        var modified = new ApiDocument(document.Version, document.Title, document.Schemas, operations, document.References, document.Root);
        var result = Extract(modified);
        Assert.Equal(4, result.Actions.Select(a => a.Id).Distinct().Count());
        Assert.All(result.Actions, a => Assert.StartsWith("operation:pointer:", a.Id));
        Assert.Equal(4, result.Diagnostics.Count(d => d.Code == "duplicate-operation-id"));
    }

    [Fact]
    public async Task InvalidSecurityMetadataIsRejectedOnImport()
    {
        var result = Extract(await Sample());
        using var stream = new MemoryStream();
        var serializer = new JsonOntologySerializer();
        await serializer.WriteAsync(new InMemoryOntology("sample", "Sample", result.Entities, [], result.Actions), stream, default);
        var json = System.Text.Json.Nodes.JsonNode.Parse(stream.ToArray())!;
        json["ontology"]!["actions"]![0]!["security"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"oauth\":42}]");
        using var invalid = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        await Assert.ThrowsAsync<JsonException>(() => serializer.ReadAsync(invalid, default));
    }
}
