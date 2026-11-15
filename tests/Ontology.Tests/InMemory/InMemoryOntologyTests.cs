using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.InMemory;

public class InMemoryOntologyTests
{
    private static EntityType Entity(string id = "Item", string[]? actions = null) =>
        new(id, id, [new PropertyDefinition("id", "string", true)], actions ?? []);

    private static ActionDefinition Action(string id = "get", string[]? entities = null) =>
        new(id, id, "GET", "/items", entities ?? [], ["scalar:string"], "/paths/items", requestSchemaId: "scalar:string");

    private static RelationshipType Edge(string source = "Item", string target = "Other", string? property = "id") =>
        new("edge", source, target, "related", "one", "ExplicitSchemaRef", "/schemas/Item/id", property);

    private static InMemoryOntology Catalog(IEnumerable<IEntityType>? entities = null,
        IEnumerable<IRelationshipType>? relationships = null, IEnumerable<IActionDefinition>? actions = null) =>
        new("sample", "Sample", entities ?? [], relationships ?? [], actions ?? []);

    [Fact]
    public void ManuallyConstructedOntologySupportsLookupAndPreservesMetadata()
    {
        var source = new SourceProvenance("sample.json", "/schemas/Item");
        var entity = new EntityType("Item", "Item", [new PropertyDefinition("id", "string", true, "Identifier", source)],
            ["get"], "An item", source);
        IOntology ontology = Catalog([entity, Entity("Other")], [Edge()], [Action(entities: ["Item"])]);

        Assert.Equal("sample", ontology.Id);
        Assert.Equal("Sample", ontology.Name);
        var found = Assert.IsType<EntityType>(ontology.FindEntity("Item"));
        Assert.Equal("An item", found.Description);
        Assert.Equal(source, found.Provenance);
        var property = Assert.Single(found.Properties);
        Assert.True(property.Required);
        Assert.Equal("Identifier", property.Description);
        Assert.Equal(source, property.Provenance);
        Assert.Equal("get", Assert.Single(found.ActionIds));
        Assert.Equal("Other", Assert.Single(ontology.Relationships).TargetEntityId);
        Assert.Equal("scalar:string", Assert.Single(ontology.Actions).RequestSchemaId);
        Assert.Null(ontology.FindEntity("missing"));
        Assert.Null(ontology.FindEntity("item"));
    }

    [Fact]
    public void EmptyOntologyAndUnboundActionsAreValid()
    {
        Assert.Empty(Catalog().Entities);
        Assert.Empty(Assert.Single(Catalog(actions: [Action()]).Actions).EntityIds);
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("relationship")]
    [InlineData("action")]
    public void DuplicateIdsAreRejected(string kind)
    {
        Assert.Throws<ArgumentException>(() => Catalog(
            kind == "entity" ? [Entity(), Entity()] : [Entity(), Entity("Other")],
            kind == "relationship" ? [Edge(), Edge()] : [],
            kind == "action" ? [Action(), Action()] : []));
    }

    [Theory]
    [InlineData("missing", "Other", "id")]
    [InlineData("Item", "missing", "id")]
    [InlineData("Item", "Other", "missing")]
    public void InvalidRelationshipReferencesAreRejected(string source, string target, string property)
    {
        Assert.Throws<ArgumentException>(() => Catalog([Entity(), Entity("Other")], [Edge(source, target, property)]));
    }

    [Fact]
    public void SelfReferencesAndRelationshipsWithoutSourcePropertyAreValid()
    {
        Assert.Single(Catalog([Entity()], [Edge("Item", "Item", null)]).Relationships);
    }

    [Fact]
    public void DanglingActionAndEntityReferencesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Catalog([Entity(actions: ["missing"])]));
        Assert.Throws<ArgumentException>(() => Catalog(actions: [Action(entities: ["missing"])]));
    }

    [Fact]
    public void CollectionsAndCustomMutableModelsAreSnapshotted()
    {
        var property = new MutableProperty();
        var properties = new List<IPropertyDefinition> { property };
        var actionIds = new List<string> { "get" };
        var entity = new EntityType("Item", "Item", properties, actionIds);
        var entityIds = new List<string> { "Item" };
        var responses = new List<string> { "Item" };
        var action = new ActionDefinition("get", "get", "GET", "/items", entityIds, responses, "");
        var entities = new List<IEntityType> { entity };
        var edges = new List<IRelationshipType> { Edge("Item", "Item") };
        var actions = new List<IActionDefinition> { action };
        var ontology = Catalog(entities, edges, actions);
        property.Name = "changed";
        properties.Clear();
        actionIds.Clear();
        entityIds.Clear();
        responses.Clear();
        entities.Clear();
        edges.Clear();
        actions.Clear();

        Assert.Equal("id", Assert.Single(Assert.Single(ontology.Entities).Properties).Name);
        ReadOnly(ontology.Entities);
        ReadOnly(ontology.Relationships);
        ReadOnly(ontology.Actions);
        ReadOnly(entity.Properties);
        ReadOnly(entity.ActionIds);
        ReadOnly(action.EntityIds);
        ReadOnly(action.ResponseSchemaIds);
        Assert.Single(action.EntityIds);
        Assert.Single(action.ResponseSchemaIds);
    }

    [Fact]
    public void InvalidModelInputsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Entity(" "));
        Assert.Throws<ArgumentException>(() => new PropertyDefinition("id", ""));
        Assert.Throws<ArgumentException>(() => new EntityType("Item", "Item",
            [new PropertyDefinition("id", "string"), new PropertyDefinition("id", "number")], []));
        Assert.Throws<ArgumentException>(() => Entity(actions: ["get", "get"]));
        Assert.Throws<ArgumentException>(() => Action(entities: [" "]));
        Assert.Throws<ArgumentNullException>(() => new InMemoryOntology("id", "name", null!, [], []));
        Assert.Throws<ArgumentException>(() => Catalog([null!]));
    }

    [Fact]
    public async Task RepositorySavesGetsReplacesAndIsolatesInstances()
    {
        IOntologyRepository repository = new InMemoryOntologyRepository();
        Assert.Null(await repository.GetAsync("sample", default));
        await repository.SaveAsync(Catalog([Entity()]), default);
        Assert.NotNull((await repository.GetAsync("sample", default))?.FindEntity("Item"));
        Assert.Null(await repository.GetAsync("Sample", default));
        await repository.SaveAsync(Catalog([Entity("Replacement")]), default);
        var replaced = await repository.GetAsync("sample", default);
        Assert.NotNull(replaced?.FindEntity("Replacement"));
        Assert.Null(replaced?.FindEntity("Item"));
        Assert.Null(await new InMemoryOntologyRepository().GetAsync("sample", default));
    }

    [Fact]
    public async Task RepositoryCopiesCustomOntologiesAndFailedSavePreservesPreviousValue()
    {
        var repository = new InMemoryOntologyRepository();
        var input = new MutableOntology();
        input.Items.Add(Entity());
        await repository.SaveAsync(input, default);
        input.Items.Clear();
        Assert.Single((await repository.GetAsync("sample", default))!.Entities);
        input.Items.Add(Entity(actions: ["missing"]));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.SaveAsync(input, default));
        Assert.Empty(Assert.Single((await repository.GetAsync("sample", default))!.Entities).ActionIds);
    }

    [Fact]
    public async Task CanceledOperationsDoNotChangeRepository()
    {
        var repository = new InMemoryOntologyRepository();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.SaveAsync(Catalog(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetAsync("sample", cancellation.Token));
        Assert.Null(await repository.GetAsync("sample", default));
    }

    private static void ReadOnly<T>(IReadOnlyCollection<T> values) =>
        Assert.Throws<NotSupportedException>(() => ((ICollection<T>)values).Clear());

    private sealed class MutableProperty : IPropertyDefinition
    {
        public string Name { get; set; } = "id";
        public string DataType => "string";
        public bool Required => true;
        public string? Description => null;
        public SourceProvenance? Provenance => null;
    }

    private sealed class MutableOntology : IOntology
    {
        public string Id => "sample";
        public string Name => "Sample";
        public List<IEntityType> Items { get; } = [];
        public IReadOnlyCollection<IEntityType> Entities => Items;
        public IReadOnlyCollection<IRelationshipType> Relationships => Array.Empty<IRelationshipType>();
        public IReadOnlyCollection<IActionDefinition> Actions => Array.Empty<IActionDefinition>();
        public IEntityType? FindEntity(string entityId) => Items.FirstOrDefault(e => e.Id == entityId);
    }
}
