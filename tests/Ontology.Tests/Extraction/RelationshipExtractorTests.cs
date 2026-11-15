using System.Text.Json;
using Ontology.Builder;
using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.Extraction;

public class RelationshipExtractorTests
{
    private static string SamplePath(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "seldon.sln"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "samples", file);
    }

    private static async Task<ApiDocument> Sample()
    {
        var result = await new OpenApiDocumentLoader().LoadAsync(SamplePath("relationships-openapi.yaml"), default);
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return result.Document!;
    }

    private static (EntityExtractionResult Entities, RelationshipExtractionResult Edges) Extract(ApiDocument document)
    {
        var entities = new EntityExtractor().Extract(document, default);
        return (entities, new RelationshipExtractor().Extract(document, entities.Entities, default));
    }

    [Fact]
    public async Task ExplicitReferencesPreserveRolesCardinalityEvidenceAndDirection()
    {
        var (entities, result) = Extract(await Sample());
        var owner = result.Relationships.Single(r => r.Name == "owner");
        Assert.Equal("schema:/components/schemas/Record", owner.SourceEntityId);
        Assert.Equal("schema:/components/schemas/Customer", owner.TargetEntityId);
        Assert.Equal("one", owner.Cardinality);
        Assert.Equal("ExplicitSchemaRef", owner.EvidenceKind);
        Assert.Equal("owner", owner.SourcePropertyName);
        Assert.EndsWith("/properties/owner/$ref", owner.SourcePointer);
        Assert.Equal("many", result.Relationships.Single(r => r.Name == "customers").Cardinality);
        Assert.NotEqual(owner.Id, result.Relationships.Single(r => r.Name == "reviewer").Id);
        Assert.DoesNotContain(result.Relationships, r => r.SourceEntityId == "schema:/components/schemas/Customer");
        _ = new InMemoryOntology("sample", "Sample", entities.Entities, result.Relationships, []);
    }

    [Fact]
    public async Task RepeatedCompositionDeduplicatesWithoutCollapsingDifferentProperties()
    {
        var (_, result) = Extract(await Sample());
        var composition = Assert.Single(result.Relationships, r => r.Name == "allOf");
        Assert.Equal("schema:/components/schemas/Base", composition.TargetEntityId);
        Assert.Equal("unknown", composition.Cardinality);
        Assert.Equal("ExplicitSchemaRef", composition.EvidenceKind);
        Assert.Null(composition.SourcePropertyName);
        Assert.EndsWith("/allOf/0/$ref", composition.SourcePointer);
        Assert.Equal(6, result.Relationships.Count);
        Assert.Equal(result.Relationships.Count, result.Relationships.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task NestedReferencesRetainTopPropertyAndPreciseSourcePointer()
    {
        var (_, result) = Extract(await Sample());
        var nested = result.Relationships.Single(r => r.Name == "detail.approvedBy");
        Assert.Equal("detail", nested.SourcePropertyName);
        Assert.EndsWith("/properties/detail/properties/approvedBy/$ref", nested.SourcePointer);
        Assert.Equal("one", nested.Cardinality);
    }

    [Fact]
    public async Task ForeignKeysRemainUnverifiedCandidatesWithDiagnosticEvidence()
    {
        var (_, result) = Extract(await Sample());
        var candidate = Assert.Single(result.Relationships, r => r.EvidenceKind == "Heuristic");
        Assert.Equal("customerId", candidate.Name);
        Assert.Equal("unknown", candidate.Cardinality);
        Assert.Equal("schema:/components/schemas/Customer", candidate.TargetEntityId);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Code == "heuristic-relationship-candidate");
        Assert.Equal(candidate.SourcePointer, diagnostic.Provenance!.SourcePointer);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("Unverified", diagnostic.Message);
    }

    private static async Task<ApiDocument> LoadSchemas(string schemas, bool swagger = false)
    {
        var json = swagger
            ? "{\"swagger\":\"2.0\",\"info\":{\"title\":\"Test\",\"version\":\"1\"},\"paths\":{},\"definitions\":" + schemas + "}"
            : "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"Test\",\"version\":\"1\"},\"paths\":{},\"components\":{\"schemas\":" + schemas + "}}";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            var loaded = await new OpenApiDocumentLoader().LoadAsync(path, default);
            Assert.False(loaded.HasErrors, string.Join("; ", loaded.Diagnostics.Select(d => d.Message)));
            return loaded.Document!;
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AmbiguousForeignKeysAreDiagnosedWithoutEdges()
    {
        var document = await LoadSchemas("""
            {"Customer":{"type":"object"},"customer":{"type":"object"},
             "Record":{"type":"object","properties":{"customerId":{"type":"string"},"otherId":{"type":"string"},"id":{"type":"string"}}}}
            """);
        var (_, result) = Extract(document);
        Assert.Empty(result.Relationships);
        Assert.Contains(result.Diagnostics, d => d.Code == "ambiguous-foreign-key");
    }

    [Fact]
    public async Task PrimitiveReferencesAndExplicitlyTypedIdsDoNotBecomeForeignKeyEdges()
    {
        var document = await LoadSchemas("""
            {"Code":{"type":"string"},"Customer":{"type":"object"},
             "Record":{"type":"object","properties":{"customerId":{"$ref":"#/components/schemas/Code"},"customerIds":{"type":"array","items":{"type":"string"}}}}}
            """);
        Assert.Empty(Extract(document).Edges.Relationships);
    }

    [Fact]
    public async Task RecursiveAndNestedArrayReferencesRemainBoundedAndMany()
    {
        var document = await LoadSchemas("""
            {"Node":{"type":"object","properties":{
              "parent":{"$ref":"#/components/schemas/Node"},
              "groups":{"type":"array","items":{"type":"array","items":{"$ref":"#/components/schemas/Node"}}},
              "details":{"type":"array","items":{"type":"object","properties":{"child":{"$ref":"#/components/schemas/Node"}}}}
            }}}
            """);
        var (entities, result) = Extract(document);
        Assert.Equal(3, result.Relationships.Count);
        Assert.Equal("one", result.Relationships.Single(r => r.Name == "parent").Cardinality);
        Assert.All(result.Relationships.Where(r => r.Name != "parent"), r => Assert.Equal("many", r.Cardinality));
        _ = new InMemoryOntology("node", "Node", entities.Entities, result.Relationships, []);
    }

    [Fact]
    public async Task MissingEntityTargetsAreDiagnosedAndNeverCreateDanglingEdges()
    {
        var document = await Sample();
        var entities = new EntityExtractor().Extract(document, default).Entities.Where(e => e.Name != "Customer");
        var result = new RelationshipExtractor().Extract(document, entities, default);
        Assert.DoesNotContain(result.Relationships, r => r.TargetEntityId == "schema:/components/schemas/Customer");
        Assert.Contains(result.Diagnostics, d => d.Code == "relationship-target-not-entity");
    }

    [Fact]
    public async Task OrderingAndJsonRoundTripPreserveAllEdgeMetadata()
    {
        var document = await Sample();
        var (entities, result) = Extract(document);
        var reversed = new ApiDocument(document.Version, document.Title, document.Schemas.Reverse(), document.Operations,
            document.References.Reverse(), document.Root);
        var reordered = new RelationshipExtractor().Extract(reversed, entities.Entities.Reverse(), default);
        Assert.Equal(result.Relationships.Select(r => r.Id), reordered.Relationships.Select(r => r.Id));
        async Task<byte[]> Write(IEnumerable<IRelationshipType> edges)
        {
            using var stream = new MemoryStream();
            await new JsonOntologySerializer().WriteAsync(new InMemoryOntology("sample", "Sample", entities.Entities, edges, []), stream, default);
            return stream.ToArray();
        }
        var bytes = await Write(result.Relationships);
        Assert.Equal(bytes, await Write(reordered.Relationships));
        using var input = new MemoryStream(bytes);
        var restored = await new JsonOntologySerializer().ReadAsync(input, default);
        Assert.Equal(bytes, await Write(restored.Relationships));
    }

    [Fact]
    public async Task SwaggerRelationshipsUseDefinitionProvenance()
    {
        var document = await LoadSchemas("""
            {"Node":{"type":"object","properties":{"parent":{"$ref":"#/definitions/Node"}}}}
            """, swagger: true);
        var relationship = Assert.Single(Extract(document).Edges.Relationships);
        Assert.Equal("schema:/definitions/Node", relationship.TargetEntityId);
        Assert.Equal("/definitions/Node/properties/parent/$ref", relationship.SourcePointer);
    }

    [Fact]
    public async Task EmptyCatalogAndCancellationAreHandled()
    {
        var document = await Sample();
        Assert.Empty(new RelationshipExtractor().Extract(document, [], default).Relationships);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new RelationshipExtractor().Extract(document, [], cancellation.Token));
    }
}
