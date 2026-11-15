using System.Text.Json;
using Ontology.Builder;
using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.Extraction;

public class EntityExtractorTests
{
    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "seldon.sln"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "samples", "entity-properties-openapi.yaml");
    }

    private static async Task<ApiDocument> Sample()
    {
        var result = await new OpenApiDocumentLoader().LoadAsync(SamplePath(), default);
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        return Assert.IsType<ApiDocument>(result.Document);
    }

    [Fact]
    public async Task ExtractsNamedAndInlineObjectsWithoutPromotingNestedValuesOrPrimitives()
    {
        var result = new EntityExtractor().Extract(await Sample(), default);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, result.Entities.Count);
        Assert.Contains(result.Entities, e => e.Id == "schema:/components/schemas/Base");
        Assert.Contains(result.Entities, e => e.Name.StartsWith("Inline:/paths/", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Entities, e => e.Name is "Code" or "detail");
        var item = result.Entities.Single(e => e.Name == "Item");
        Assert.Equal("A generic item", item.Description);
        Assert.Equal("/components/schemas/Item", item.Provenance!.SourcePointer);
        Assert.Empty(item.ActionIds);
        var id = item.Properties.Single(p => p.Name == "id");
        Assert.True(id.Required);
        Assert.Equal("uuid", id.Format);
        Assert.Equal("Stable identifier", id.Description);
        Assert.Equal("/components/schemas/Base/properties/id", id.Provenance!.SourcePointer);
        var nested = item.Properties.Single(p => p.Name == "detail");
        Assert.Equal("object", nested.DataType);
        Assert.True(Assert.Single(nested.Properties).Required);
    }

    [Fact]
    public async Task ExtractsEnumsFormatsNullableTypesArraysAndReferences()
    {
        var item = new EntityExtractor().Extract(await Sample(), default).Entities.Single(e => e.Name == "Item");
        var status = item.Properties.Single(p => p.Name == "status");
        Assert.True(status.Required);
        Assert.Equal("string", status.DataType);
        Assert.Equal("schema:/components/schemas/Code", status.ReferencedSchemaId);
        Assert.Equal(new[] { "active", "archived" }, status.EnumValues.Select(v => v.GetString()));
        Assert.All(item.Properties.Single(p => p.Name == "score").EnumValues, v => Assert.Equal(JsonValueKind.Number, v.ValueKind));
        Assert.True(item.Properties.Single(p => p.Name == "note").Nullable);
        Assert.Equal("string", item.Properties.Single(p => p.Name == "note").DataType);
        var children = item.Properties.Single(p => p.Name == "children");
        Assert.Equal("array", children.DataType);
        Assert.Equal("schema:/components/schemas/Item", children.Items!.ReferencedSchemaId);
        Assert.Empty(children.Items.Properties);
        Assert.Equal("string", item.Properties.Single(p => p.Name == "tags").Items!.DataType);
    }

    [Fact]
    public async Task ExtractedMetadataSurvivesSnapshotAndJsonRoundTripDeterministically()
    {
        var document = await Sample();
        var extractor = new EntityExtractor();
        var result = extractor.Extract(document, default);
        var reversed = new ApiDocument(document.Version, document.Title, document.Schemas.Reverse(), document.Operations,
            document.References, document.Root);
        async Task<byte[]> Export(IEnumerable<IEntityType> entities)
        {
            using var stream = new MemoryStream();
            await new JsonOntologySerializer().WriteAsync(new InMemoryOntology("test", "Test", entities, [], []), stream, default);
            return stream.ToArray();
        }
        var bytes = await Export(result.Entities);
        Assert.Equal(bytes, await Export(extractor.Extract(reversed, default).Entities));
        using var input = new MemoryStream(bytes);
        var imported = await new JsonOntologySerializer().ReadAsync(input, default);
        Assert.Equal(bytes, await Export(imported.Entities));
        Assert.Empty(imported.Relationships);
        Assert.Empty(imported.Actions);
        Assert.Equal("uuid", imported.FindEntity("schema:/components/schemas/Item")!.Properties.Single(p => p.Name == "id").Format);
        Assert.True(imported.FindEntity("schema:/components/schemas/Item")!.Properties.Single(p => p.Name == "detail").Properties.Single().Required);
    }

    // Minimal parser-neutral input isolates inference edge cases from parser validation policies.
    private static ApiDocument Document(string schemas)
    {
        using var json = JsonDocument.Parse("{\"components\":{\"schemas\":" + schemas + "}}");
        var root = json.RootElement;
        var catalog = new List<ApiSchema>();
        var references = new List<ApiReference>();
        void Walk(JsonElement node, string pointer, string? name = null)
        {
            catalog.Add(new(name, new("test.json", pointer), node.Clone()));
            if (node.ValueKind != JsonValueKind.Object) return;
            if (node.TryGetProperty("$ref", out var reference))
                references.Add(new(reference.GetString()!, new("test.json", pointer + "/$ref"), reference.GetString()!.StartsWith("#/") ? reference.GetString()![1..] : null));
            if (node.TryGetProperty("properties", out var properties))
                foreach (var property in properties.EnumerateObject()) Walk(property.Value, pointer + "/properties/" + property.Name);
            if (node.TryGetProperty("allOf", out var all))
                foreach (var (child, i) in all.EnumerateArray().Select((child, i) => (child, i))) Walk(child, pointer + "/allOf/" + i);
        }
        foreach (var schema in root.GetProperty("components").GetProperty("schemas").EnumerateObject())
            Walk(schema.Value, "/components/schemas/" + schema.Name, schema.Name);
        return new("3.1", "Test", catalog, [], references, root);
    }

    [Fact]
    public void AllOfUnionsRequiredFlagsAndWarnsForConflictingProperties()
    {
        var document = Document("""
            {"Thing":{"allOf":[
              {"type":"object","properties":{"id":{"type":"string"}}},
              {"required":["id"],"properties":{"id":{"type":"integer"}}}
            ]}}
            """);
        var result = new EntityExtractor().Extract(document, default);
        var property = Assert.Single(Assert.Single(result.Entities).Properties);
        Assert.True(property.Required);
        Assert.Equal("unknown", property.DataType);
        Assert.Contains(result.Diagnostics, d => d.Code == "conflicting-allOf-property");
    }

    [Fact]
    public void UnsupportedUnionsAndUnresolvedReferencesAreConservative()
    {
        var result = new EntityExtractor().Extract(Document("""
            {"Thing":{"type":"object","properties":{
              "choice":{"oneOf":[{"type":"string"},{"type":"number"}]},
              "maybe":{"anyOf":[{"type":"string"},{"type":"number"}]},
              "external":{"$ref":"remote.yaml#/Other"}
            }}}
            """), default);
        Assert.All(Assert.Single(result.Entities).Properties, p => Assert.Equal("unknown", p.DataType));
        Assert.Contains(result.Diagnostics, d => d.Code == "unsupported-oneOf");
        Assert.Contains(result.Diagnostics, d => d.Code == "unsupported-anyOf");
        Assert.Contains(result.Diagnostics, d => d.Code == "unresolved-schema-reference");
    }

    [Fact]
    public void CyclicAllOfTerminatesWithDiagnostic()
    {
        var result = new EntityExtractor().Extract(Document("""
            {"Thing":{"type":"object","allOf":[{"$ref":"#/components/schemas/Thing"}],"properties":{"id":{"type":"string"}}}}
            """), default);
        Assert.Single(Assert.Single(result.Entities).Properties);
        Assert.Contains(result.Diagnostics, d => d.Code == "cyclic-composition");
    }

    [Fact]
    public void CancellationIsHonored()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new EntityExtractor().Extract(Document("{}"), cancellation.Token));
    }

    [Theory]
    [InlineData("minimal-openapi.yaml", "schema:/components/schemas/Item")]
    [InlineData("minimal-openapi31.yaml", "schema:/components/schemas/Item")]
    [InlineData("minimal-swagger.json", "schema:/definitions/Item")]
    public async Task ExtractsAcrossAllSupportedSpecificationFamilies(string fixture, string id)
    {
        var path = Path.Combine(Path.GetDirectoryName(SamplePath())!, fixture);
        var loaded = await new OpenApiDocumentLoader().LoadAsync(path, default);
        Assert.False(loaded.HasErrors);
        var extracted = new EntityExtractor().Extract(loaded.Document!, default);
        Assert.Contains(extracted.Entities, e => e.Id == id && e.Name == "Item");
    }

    [Fact]
    public void RichPropertyMetadataIsDefensivelyCopied()
    {
        PropertyDefinition property;
        var nested = new List<IPropertyDefinition> { new PropertyDefinition("label", "string") };
        using (var json = JsonDocument.Parse("[1,true,null,\"text\"]"))
        {
            var values = json.RootElement.EnumerateArray().ToList();
            property = new PropertyDefinition("detail", "object", enumValues: values, properties: nested);
            values.Clear();
        }
        nested.Clear();
        Assert.Equal(4, property.EnumValues.Count);
        Assert.Single(property.Properties);
        Assert.Equal("text", property.EnumValues.Last().GetString());
        Assert.Throws<NotSupportedException>(() => ((ICollection<JsonElement>)property.EnumValues).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<IPropertyDefinition>)property.Properties).Clear());
    }

    [Fact]
    public async Task StableIdsDoNotDependOnDocumentPath()
    {
        var document = await Sample();
        var relocated = new ApiDocument(document.Version, document.Title,
            document.Schemas.Select(s => s with { Provenance = s.Provenance with { Document = "moved.yaml" } }),
            document.Operations, document.References, document.Root);
        var extractor = new EntityExtractor();
        Assert.Equal(extractor.Extract(document, default).Entities.Select(e => e.Id),
            extractor.Extract(relocated, default).Entities.Select(e => e.Id));
    }
}
