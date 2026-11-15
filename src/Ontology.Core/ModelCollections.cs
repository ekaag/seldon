namespace Ontology.Core;

internal static class ModelCollections
{
    internal static string Text(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    internal static IReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        var items = values.ToArray();
        if (items.Any(item => item is null))
            throw new ArgumentException("Collection cannot contain null entries.", name);
        return Array.AsReadOnly(items);
    }

    internal static IReadOnlyCollection<string> Ids(IEnumerable<string> values, string name)
    {
        var items = Copy(values, name);
        foreach (var item in items) Text(item, name);
        if (items.Distinct(StringComparer.Ordinal).Count() != items.Count)
            throw new ArgumentException("Collection contains duplicate identifiers.", name);
        return items;
    }
}
