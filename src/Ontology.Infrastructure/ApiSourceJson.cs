using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Ontology.Infrastructure;

/// <summary>Retains source syntax independently of OpenAPI model serialization.</summary>
internal static class ApiSourceJson
{
    internal static async Task<JsonDocument> ReadAsync(Stream input, string format, CancellationToken ct)
    {
        if (format == "json")
            return await JsonDocument.ParseAsync(input, new JsonDocumentOptions { MaxDepth = 64 }, ct);
        using var textReader = new StreamReader(input, leaveOpen: true);
        var text = await textReader.ReadToEndAsync(ct);
        // Enforce limits before composing the YAML representation model.
        var parser = new Parser(new StringReader(text));
        var depth = 0;
        var events = 0;
        while (parser.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            if (++events > 200_000) throw new JsonException("YAML exceeds the node limit.");
            if (parser.Current is AnchorAlias) throw new JsonException("YAML aliases are unsupported; use local $ref references.");
            if (parser.Current is MappingStart or SequenceStart && ++depth > 64)
                throw new JsonException("YAML exceeds the maximum depth of 64.");
            if (parser.Current is MappingEnd or SequenceEnd) depth--;
        }
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        if (stream.Documents.Count != 1) throw new JsonException("Exactly one YAML document is required.");
        JsonNode? Convert(YamlNode node)
        {
            ct.ThrowIfCancellationRequested();
            if (node is YamlMappingNode map)
            {
                var result = new JsonObject();
                foreach (var entry in map.Children)
                {
                    if (entry.Key is not YamlScalarNode key || key.Value is null)
                        throw new JsonException("YAML mapping keys must be strings.");
                    if (result.ContainsKey(key.Value)) throw new JsonException($"Duplicate YAML key '{key.Value}'.");
                    result.Add(key.Value, Convert(entry.Value));
                }
                return result;
            }
            if (node is YamlSequenceNode sequence)
                return new JsonArray(sequence.Children.Select(Convert).ToArray());
            if (node is not YamlScalarNode scalar) throw new JsonException("Unsupported YAML node.");
            var value = scalar.Value ?? "";
            if (scalar.Style == ScalarStyle.Plain)
            {
                if (value is "null" or "Null" or "NULL" or "~" or "") return null;
                if (bool.TryParse(value, out var boolean)) return JsonValue.Create(boolean);
                // Parse numbers with JSON's grammar rather than YAML-specific numeric coercions.
                if (value.Length > 0 && (char.IsDigit(value[0]) || value[0] == '-'))
                {
                    try
                    {
                        var number = JsonNode.Parse(value);
                        if (number is JsonValue && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return number;
                    }
                    catch (JsonException) { }
                }
            }
            return JsonValue.Create(value);
        }
        return JsonDocument.Parse((Convert(stream.Documents[0].RootNode) ?? throw new JsonException("Empty YAML document.")).ToJsonString(),
            new JsonDocumentOptions { MaxDepth = 64 });
    }
}
