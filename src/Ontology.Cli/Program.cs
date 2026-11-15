using Microsoft.Extensions.DependencyInjection;
using Ontology.Builder;
using Ontology.Cli;
using Ontology.Core;
using Ontology.Infrastructure;

var arguments = CliArguments.Parse(args);
if (arguments is null)
{
    Console.Error.WriteLine("Usage: ontology-builder inspect --input <OpenAPI/Swagger YAML or JSON file>");
    Console.Error.WriteLine("       ontology-builder build --input <file> --output <directory> [--no-open]");
    return 2;
}
using var services = new ServiceCollection()
    .AddSingleton<IApiDocumentLoader, OpenApiDocumentLoader>()
    .AddSingleton<IEntityExtractor, EntityExtractor>()
    .AddSingleton<IRelationshipExtractor, RelationshipExtractor>()
    .AddSingleton<IActionExtractor, ActionExtractor>()
    .AddSingleton<IOntologyFactory, InMemoryOntologyFactory>()
    .AddSingleton<IOntologyRepository, InMemoryOntologyRepository>()
    .AddSingleton<IOntologySerializer, JsonOntologySerializer>()
    .AddSingleton<IOntologyBuilder, OntologyBuilder>()
    .BuildServiceProvider();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (arguments.Command == "build")
    {
        var input = Path.GetFullPath(arguments.Input);
        var output = Path.GetFullPath(arguments.Output!);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (new[] { "ontology.json", "diagnostics.json", "index.html", "viewer.js", "viewer.css" }.Any(name => string.Equals(input, Path.Combine(output, name), comparison)))
        {
            Console.Error.WriteLine("The input specification cannot be an export destination.");
            return 2;
        }
        var built = await services.GetRequiredService<IOntologyBuilder>().BuildAsync(arguments.Input, cancellation.Token);
        foreach (var diagnostic in built.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code} [{diagnostic.Provenance?.SourcePointer}]: {diagnostic.Message}");
        await ExportFiles.WriteAsync(output, built, services.GetRequiredService<IOntologySerializer>(), cancellation.Token);
        if (built.HasErrors || built.Ontology is null)
        {
            Console.Error.WriteLine("Build failed. See diagnostics.json; any previous ontology.json was retained.");
            return 1;
        }
        Console.WriteLine($"Exported {Path.Combine(output, "ontology.json")}");
        Console.WriteLine($"Viewer: {Path.Combine(output, "index.html")} (choose Load ontology.json)");
        Console.WriteLine($"Entities: {built.Ontology.Entities.Count}; relationships: {built.Ontology.Relationships.Count}; actions: {built.Ontology.Actions.Count}");
        if (arguments.OpenBrowser) ChromeViewerLauncher.Open(Path.Combine(output, "index.html"));
        return 0;
    }
    var result = await services.GetRequiredService<IApiDocumentLoader>().LoadAsync(arguments.Input, cancellation.Token);
    if (result.Document is { } document)
    {
        Console.WriteLine($"Title: {document.Title}");
        Console.WriteLine($"Specification: {document.Version}");
        Console.WriteLine($"Schemas: {document.Schemas.Count} (named: {document.Schemas.Count(s => s.Name is not null)}; includes nested/inline schemas)");
        Console.WriteLine($"Operations: {document.Operations.Count}");
    }
    foreach (var diagnostic in result.Diagnostics)
        Console.Error.WriteLine($"{diagnostic.Severity} {diagnostic.Code} [{diagnostic.Provenance?.SourcePointer}]: {diagnostic.Message}");
    return result.Document is null || result.HasErrors ? 1 : 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Operation canceled.");
    return 130;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"Operation failed: {exception.Message}");
    return 1;
}
