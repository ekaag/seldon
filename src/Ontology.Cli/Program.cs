using Microsoft.Extensions.DependencyInjection;
using Ontology.Core;
using Ontology.Infrastructure;

if (args.Length != 3 || args[0] != "inspect" || args[1] != "--input" || string.IsNullOrWhiteSpace(args[2]))
{
    Console.Error.WriteLine("Usage: ontology-builder inspect --input <OpenAPI/Swagger YAML or JSON file>");
    return 2;
}
using var services = new ServiceCollection().AddSingleton<IApiDocumentLoader, OpenApiDocumentLoader>().BuildServiceProvider();
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var result = await services.GetRequiredService<IApiDocumentLoader>().LoadAsync(args[2], cancellation.Token);
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
    Console.Error.WriteLine("Inspection canceled.");
    return 130;
}
