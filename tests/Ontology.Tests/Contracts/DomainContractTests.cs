using Ontology.Core;

namespace Ontology.Tests.Contracts;

public class DomainContractTests
{
    [Fact]
    public void BuildResultSnapshotsDiagnosticsAndExposesReadOnlyCollection()
    {
        var diagnostic = new OntologyDiagnostic("unsupported-feature", DiagnosticSeverity.Warning, "Unsupported feature.");
        var diagnostics = new List<OntologyDiagnostic> { diagnostic };
        var result = new OntologyBuildResult(null, diagnostics);

        diagnostics.Clear();

        Assert.Equal(diagnostic, Assert.Single(result.Diagnostics));
        Assert.Throws<NotSupportedException>(() => ((ICollection<OntologyDiagnostic>)result.Diagnostics).Clear());
        Assert.Null(result.Ontology);
        Assert.False(result.HasErrors);
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Info, false)]
    [InlineData(DiagnosticSeverity.Warning, false)]
    [InlineData(DiagnosticSeverity.Error, true)]
    public void BuildResultReportsErrorSeverity(DiagnosticSeverity severity, bool expected)
    {
        var result = new OntologyBuildResult(null, [new("test", severity, "Test diagnostic.")]);

        Assert.Equal(expected, result.HasErrors);
    }

    [Fact]
    public void BuildResultRejectsInvalidDiagnosticCollections()
    {
        Assert.Throws<ArgumentNullException>(() => new OntologyBuildResult(null, null!));
        Assert.Throws<ArgumentException>(() => new OntologyBuildResult(null, [null!]));
    }

    [Fact]
    public void DiagnosticsRetainStructuredSourceEvidence()
    {
        var provenance = new SourceProvenance("sample.json", "/components/schemas/Item");
        var diagnostic = new OntologyDiagnostic("schema-warning", DiagnosticSeverity.Warning, "Review schema.", provenance);

        Assert.Equal("schema-warning", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Review schema.", diagnostic.Message);
        Assert.Equal(provenance, diagnostic.Provenance);
        var source = Assert.IsType<SourceProvenance>(diagnostic.Provenance);
        Assert.Equal("sample.json", source.Document);
        Assert.Equal("/components/schemas/Item", source.SourcePointer);
    }

    [Fact]
    public void DomainInterfacesExposeOnlyReadOnlyProperties()
    {
        Type[] contracts = [typeof(IOntology), typeof(IEntityType), typeof(IPropertyDefinition),
            typeof(IRelationshipType), typeof(IActionDefinition)];

        foreach (var contract in contracts)
        {
            Assert.All(contract.GetProperties(), property =>
            {
                Assert.NotNull(property.GetMethod);
                Assert.Null(property.SetMethod);
                if (property.PropertyType.IsGenericType)
                {
                    Assert.Equal(typeof(IReadOnlyCollection<>), property.PropertyType.GetGenericTypeDefinition());
                }
            });
        }
    }
}
