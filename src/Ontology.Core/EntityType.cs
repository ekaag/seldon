namespace Ontology.Core;

public sealed class EntityType : IEntityType
{
    public EntityType(string id, string name, IEnumerable<IPropertyDefinition> properties,
        IEnumerable<string> actionIds, string? description = null, SourceProvenance? provenance = null)
    {
        Id = ModelCollections.Text(id, nameof(id));
        Name = ModelCollections.Text(name, nameof(name));
        // Snapshot interface implementations as well as the containing collection.
        Properties = ModelCollections.Copy(ModelCollections.Copy(properties, nameof(properties))
            .Select(PropertyDefinition.Snapshot), nameof(properties));
        if (Properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Properties.Count)
            throw new ArgumentException("Entity contains duplicate property names.", nameof(properties));
        ActionIds = ModelCollections.Ids(actionIds, nameof(actionIds));
        Description = description;
        Provenance = provenance;
    }

    public string Id { get; }
    public string Name { get; }
    public string? Description { get; }
    public SourceProvenance? Provenance { get; }
    public IReadOnlyCollection<IPropertyDefinition> Properties { get; }
    public IReadOnlyCollection<string> ActionIds { get; }
}
