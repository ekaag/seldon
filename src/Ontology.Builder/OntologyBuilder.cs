using Ontology.Core;

namespace Ontology.Builder;

public sealed class OntologyBuilder(IApiDocumentLoader loader, IEntityExtractor entityExtractor,
    IRelationshipExtractor relationshipExtractor, IActionExtractor actionExtractor,
    IOntologyFactory factory, IOntologyRepository repository) : IOntologyBuilder
{
    public async Task<OntologyBuildResult> BuildAsync(string inputPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var diagnostics = new List<OntologyDiagnostic>();
        OntologyBuildResult Result(IOntology? ontology) => new(ontology, diagnostics
            .OrderBy(d => d.Provenance?.SourcePointer, StringComparer.Ordinal)
            .ThenBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal).ThenBy(d => d.Message, StringComparer.Ordinal));
        try
        {
            var loaded = await loader.LoadAsync(inputPath, ct);
            diagnostics.AddRange(loaded.Diagnostics);
            if (loaded.Document is null && !loaded.HasErrors)
                diagnostics.Add(new("missing-api-document", DiagnosticSeverity.Error, "The loader returned no API document.", new(inputPath, "")));
            if (loaded.Document is null || loaded.HasErrors) return Result(null);
            var entities = entityExtractor.Extract(loaded.Document, ct);
            diagnostics.AddRange(entities.Diagnostics);
            var relationships = relationshipExtractor.Extract(loaded.Document, entities.Entities, ct);
            diagnostics.AddRange(relationships.Diagnostics);
            var actions = actionExtractor.Extract(loaded.Document, entities.Entities, ct);
            diagnostics.AddRange(actions.Diagnostics);
            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return Result(null);
            var id = Path.GetFileNameWithoutExtension(inputPath);
            if (string.IsNullOrWhiteSpace(id)) id = "ontology";
            var ontology = factory.Create(id, string.IsNullOrWhiteSpace(loaded.Document.Title) ? id : loaded.Document.Title,
                actions.Entities, relationships.Relationships, actions.Actions);
            await repository.SaveAsync(ontology, ct);
            return Result(ontology);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
        {
            diagnostics.Add(new("ontology-build-failed", DiagnosticSeverity.Error, exception.Message, new(inputPath, "")));
            return Result(null);
        }
    }
}
