namespace Ontology.Core;

public sealed class PropertyDefinition : IPropertyDefinition
{
    public PropertyDefinition(string name, string dataType, bool required = false,
        string? description = null, SourceProvenance? provenance = null, string? format = null,
        bool nullable = false, string? referencedSchemaId = null,
        IEnumerable<System.Text.Json.JsonElement>? enumValues = null, IPropertyDefinition? items = null,
        IEnumerable<IPropertyDefinition>? properties = null)
    {
        Name = ModelCollections.Text(name, nameof(name));
        DataType = ModelCollections.Text(dataType, nameof(dataType));
        Required = required;
        Description = description;
        Provenance = provenance;
        Format = format;
        Nullable = nullable;
        ReferencedSchemaId = referencedSchemaId;
        EnumValues = Array.AsReadOnly((enumValues ?? []).Select(value => value.Clone()).ToArray());
        Items = items is null ? null : Snapshot(items);
        Properties = ModelCollections.Copy((properties ?? []).Select(Snapshot), nameof(properties));
        if (Properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != Properties.Count)
            throw new ArgumentException("Duplicate nested property names.", nameof(properties));
    }

    public string Name { get; }
    public string DataType { get; }
    public bool Required { get; }
    public string? Description { get; }
    public SourceProvenance? Provenance { get; }
    public string? Format { get; }
    public bool Nullable { get; }
    public string? ReferencedSchemaId { get; }
    public IReadOnlyCollection<System.Text.Json.JsonElement> EnumValues { get; }
    public IPropertyDefinition? Items { get; }
    public IReadOnlyCollection<IPropertyDefinition> Properties { get; }

    internal static IPropertyDefinition Snapshot(IPropertyDefinition property)
    {
        ArgumentNullException.ThrowIfNull(property);
        // Instances of this sealed class already own immutable, recursively copied values.
        return property is PropertyDefinition ? property : new PropertyDefinition(property.Name, property.DataType,
            property.Required, property.Description, property.Provenance, property.Format, property.Nullable,
            property.ReferencedSchemaId, property.EnumValues, property.Items, property.Properties);
    }
}
