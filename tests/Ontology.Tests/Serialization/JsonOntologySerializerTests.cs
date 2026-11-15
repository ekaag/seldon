using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.Serialization;

public class JsonOntologySerializerTests
{
    private readonly JsonOntologySerializer serializer = new();

    private static IOntology Sample(bool reverse = false)
    {
        var provenance = new SourceProvenance("spec.json", "/schemas/A");
        IPropertyDefinition[] properties = [new PropertyDefinition("z", "number"),
            new PropertyDefinition("a", "string", true, "Identifier <script> & café", provenance)];
        string[] ids = ["z", "a"];
        IEntityType[] entities = [new EntityType("z", "Z", [], []),
            new EntityType("a", "A", reverse ? properties.Reverse() : properties,
                reverse ? ids.Reverse() : ids, "Description", provenance)];
        IActionDefinition[] actions = ids.Select(id => (IActionDefinition)new ActionDefinition(id, id,
            "POST", "/items/{id}", reverse ? ids.Reverse() : ids,
            reverse ? ids.Reverse() : ids, "/paths/items", "operation-" + id, "primitive:string")).ToArray();
        IRelationshipType[] edges = ids.Select(id => (IRelationshipType)new RelationshipType(id,
            "a", "z", "related", "many", "Heuristic", "/schemas/A/a", "a")).ToArray();
        return new InMemoryOntology("sample", "Sample", reverse ? entities.Reverse() : entities,
            reverse ? edges.Reverse() : edges, reverse ? actions.Reverse() : actions);
    }

    private async Task<string> Write(IOntology ontology)
    {
        using var stream = new MemoryStream();
        await serializer.WriteAsync(ontology, stream, default);
        Assert.True(stream.CanWrite);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private async Task<IOntology> Read(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await serializer.ReadAsync(stream, default);
        Assert.True(stream.CanRead);
        return result;
    }

    [Fact]
    public async Task RoundTripPreservesAllCurrentFieldsAndProducesIdenticalBytes()
    {
        var json = await Write(Sample());
        var restored = await Read(json);
        Assert.Equal(json, await Write(restored));
        Assert.Contains("\n", json);
        var entity = restored.FindEntity("a")!;
        Assert.Equal("Description", entity.Description);
        Assert.Equal(new SourceProvenance("spec.json", "/schemas/A"), entity.Provenance);
        var property = entity.Properties.Single(p => p.Name == "a");
        Assert.True(property.Required);
        Assert.Equal("Identifier <script> & café", property.Description);
        Assert.Equal(entity.Provenance, property.Provenance);
        var edge = restored.Relationships.First();
        Assert.Equal("Heuristic", edge.EvidenceKind);
        Assert.Equal("many", edge.Cardinality);
        Assert.Equal("a", edge.SourcePropertyName);
        Assert.Equal("primitive:string", restored.Actions.First().RequestSchemaId);
        Assert.Equal("operation-a", restored.Actions.First().OperationId);
    }

    [Fact]
    public async Task CollectionInputOrderDoesNotAffectOutput()
    {
        Assert.Equal(await Write(Sample()), await Write(Sample(reverse: true)));
    }

    [Fact]
    public async Task EmptyOntologyAndUnboundActionRoundTrip()
    {
        var ontology = new InMemoryOntology("empty", "Empty", [], [],
            [new ActionDefinition("health", "Health", "GET", "/health", [], [], "")]);
        var restored = await Read(await Write(ontology));
        Assert.Empty(restored.Entities);
        Assert.Empty(restored.Relationships);
        Assert.Empty(Assert.Single(restored.Actions).EntityIds);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("1.1")]
    [InlineData("garbage")]
    public async Task UnsupportedVersionsHaveUsefulErrors(string version)
    {
        var json = (await Write(Sample())).Replace("\"1.0\"", $"\"{version}\"");
        var error = await Assert.ThrowsAsync<JsonException>(() => Read(json));
        Assert.Contains("schemaVersion", error.Message);
        Assert.Contains(version, error.Message);
    }

    [Theory]
    [InlineData("missing-entities")]
    [InlineData("null-entities")]
    [InlineData("null-entity")]
    [InlineData("null-property")]
    [InlineData("missing-required")]
    [InlineData("unknown-member")]
    [InlineData("duplicate-id")]
    [InlineData("dangling-relationship")]
    [InlineData("dangling-action")]
    [InlineData("dangling-entity")]
    [InlineData("null-id")]
    [InlineData("blank-id")]
    [InlineData("wrong-type")]
    public async Task InvalidImportsAreRejected(string fault)
    {
        var root = JsonNode.Parse(await Write(Sample()))!;
        var ontology = root["ontology"]!;
        switch (fault)
        {
            case "missing-entities": ontology.AsObject().Remove("entities"); break;
            case "null-entities": ontology["entities"] = null; break;
            case "null-entity": ontology["entities"]![0] = null; break;
            case "null-property": ontology["entities"]![0]!["properties"]![0] = null; break;
            case "missing-required": ontology["entities"]![0]!["properties"]![0]!.AsObject().Remove("required"); break;
            case "unknown-member": ontology["surprise"] = true; break;
            case "duplicate-id": ontology["entities"]!.AsArray().Add(ontology["entities"]![0]!.DeepClone()); break;
            case "dangling-relationship": ontology["relationships"]![0]!["targetEntityId"] = "missing"; break;
            case "dangling-action": ontology["entities"]![0]!["actionIds"]![0] = "missing"; break;
            case "dangling-entity": ontology["actions"]![0]!["entityIds"]![0] = "missing"; break;
            case "null-id": ontology["id"] = null; break;
            case "blank-id": ontology["id"] = " "; break;
            case "wrong-type": ontology["entities"] = 42; break;
        }
        await Assert.ThrowsAsync<JsonException>(() => Read(root.ToJsonString()));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\"}")]
    public async Task MalformedEnvelopesAreRejected(string json)
    {
        await Assert.ThrowsAnyAsync<JsonException>(() => Read(json));
    }

    [Fact]
    public async Task CancellationIsHonoredAndStreamsStayOpen()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serializer.WriteAsync(Sample(), stream, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serializer.ReadAsync(stream, cancellation.Token));
        Assert.Equal(0, stream.Length);
        Assert.True(stream.CanRead);
    }
}
