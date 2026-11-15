namespace Ontology.Core;

/// <summary>Build output. A null ontology represents a build that could not produce a catalog.</summary>
public sealed class OntologyBuildResult
{
    public OntologyBuildResult(IOntology? ontology, IEnumerable<OntologyDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var snapshot = diagnostics.ToArray();
        if (snapshot.Any(diagnostic => diagnostic is null))
        {
            throw new ArgumentException("Diagnostics cannot contain null entries.", nameof(diagnostics));
        }

        Ontology = ontology;
        Diagnostics = Array.AsReadOnly(snapshot);
    }

    public IOntology? Ontology { get; }
    public IReadOnlyCollection<OntologyDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
}
