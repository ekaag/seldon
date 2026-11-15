using Ontology.Core;

namespace Ontology.Infrastructure;

/// <summary>An immutable, validated snapshot. Identifiers are case-sensitive and ordinal.</summary>
public sealed class InMemoryOntology : IOntology
{
    private readonly Dictionary<string, IEntityType> entitiesById;

    public InMemoryOntology(string id, string name, IEnumerable<IEntityType> entities,
        IEnumerable<IRelationshipType> relationships, IEnumerable<IActionDefinition> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Entities = Snapshot(entities, e => (IEntityType)new EntityType(e.Id, e.Name, e.Properties, e.ActionIds, e.Description, e.Provenance));
        Relationships = Snapshot(relationships, r => (IRelationshipType)new RelationshipType(r.Id, r.SourceEntityId,
            r.TargetEntityId, r.Name, r.Cardinality, r.EvidenceKind, r.SourcePointer, r.SourcePropertyName));
        Actions = Snapshot(actions, a => (IActionDefinition)new ActionDefinition(a.Id, a.Name, a.HttpMethod,
            a.Path, a.EntityIds, a.ResponseSchemaIds, a.SourcePointer, a.OperationId, a.RequestSchemaId,
            a.Summary, a.Description, a.Tags, a.Parameters, a.RequestSchemas, a.Responses, a.Security,
            a.SecurityInherited, a.RequestBodyRequired, a.Associations));
        entitiesById = Index(Entities, e => e.Id);
        Index(Relationships, r => r.Id);
        var actionsById = Index(Actions, a => a.Id);

        foreach (var relationship in Relationships)
        {
            Require(entitiesById.ContainsKey(relationship.SourceEntityId), $"Relationship '{relationship.Id}' has unknown source '{relationship.SourceEntityId}'.");
            Require(entitiesById.ContainsKey(relationship.TargetEntityId), $"Relationship '{relationship.Id}' has unknown target '{relationship.TargetEntityId}'.");
            if (relationship.SourcePropertyName is { } property)
                Require(entitiesById[relationship.SourceEntityId].Properties.Any(p => p.Name == property),
                    $"Relationship '{relationship.Id}' has unknown source property '{property}'.");
        }
        foreach (var entity in Entities)
            foreach (var actionId in entity.ActionIds)
                Require(actionsById.ContainsKey(actionId), $"Entity '{entity.Id}' references unknown action '{actionId}'.");
        foreach (var action in Actions)
        {
            foreach (var entityId in action.EntityIds)
                Require(entitiesById.ContainsKey(entityId), $"Action '{action.Id}' references unknown entity '{entityId}'.");
            foreach (var association in action.Associations)
                Require(action.EntityIds.Contains(association.EntityId, StringComparer.Ordinal),
                    $"Action '{action.Id}' has association evidence for unbound entity '{association.EntityId}'.");
        }
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyCollection<IEntityType> Entities { get; }
    public IReadOnlyCollection<IRelationshipType> Relationships { get; }
    public IReadOnlyCollection<IActionDefinition> Actions { get; }

    public IEntityType? FindEntity(string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return entitiesById.GetValueOrDefault(entityId);
    }

    private static IReadOnlyCollection<T> Snapshot<T>(IEnumerable<T> source, Func<T, T> copy)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Array.AsReadOnly(source.Select(item => item is null
            ? throw new ArgumentException("Collection cannot contain null entries.") : copy(item)).ToArray());
    }

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items, Func<T, string> id)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
            Require(result.TryAdd(id(item), item), $"Duplicate identifier '{id(item)}'.");
        return result;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new ArgumentException(message);
    }
}
