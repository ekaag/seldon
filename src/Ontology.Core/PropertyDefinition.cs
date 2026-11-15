namespace Ontology.Core;

public sealed class PropertyDefinition : IPropertyDefinition
{
    public PropertyDefinition(string name, string dataType, bool required = false,
        string? description = null, SourceProvenance? provenance = null)
    {
        Name = ModelCollections.Text(name, nameof(name));
        DataType = ModelCollections.Text(dataType, nameof(dataType));
        Required = required;
        Description = description;
        Provenance = provenance;
    }

    public string Name { get; }
    public string DataType { get; }
    public bool Required { get; }
    public string? Description { get; }
    public SourceProvenance? Provenance { get; }
}
