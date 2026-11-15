namespace Ontology.Core;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>A machine-readable code and human-readable explanation with optional source evidence.</summary>
public sealed record OntologyDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    SourceProvenance? Provenance = null);
