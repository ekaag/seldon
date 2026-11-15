namespace Ontology.Core;

/// <summary>Original document identity and JSON Pointer; neither implies network resolution.</summary>
public sealed record SourceProvenance(string Document, string SourcePointer);
