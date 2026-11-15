using System.Diagnostics;
using System.Text.Json;
using Ontology.Builder;
using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.EndToEnd;

public sealed class CliBuildTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "seldon-cli-" + Guid.NewGuid().ToString("N"));

    public CliBuildTests() => Directory.CreateDirectory(directory);

    private static string Root
    {
        get
        {
            var candidate = new DirectoryInfo(AppContext.BaseDirectory);
            while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "seldon.sln"))) candidate = candidate.Parent;
            return candidate!.FullName;
        }
    }
    private static string Sample(string name) => Path.Combine(Root, "samples", name);

    private static async Task<(int Code, string Output, string Error)> Run(params string[] args)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var configuration = framework.Parent!.Name;
        var dll = Path.Combine(Root, "src", "Ontology.Cli", "bin", configuration, framework.Name, "Ontology.Cli.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Root
        };
        start.ArgumentList.Add(dll);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        // Exercise exports without opening browser windows in the test runner.
        if (args.FirstOrDefault() == "build" && !args.Contains("--no-open")) start.ArgumentList.Add("--no-open");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        return (process.ExitCode, await stdout, await stderr);
    }

    [Theory]
    [InlineData("minimal-openapi.yaml")]
    [InlineData("minimal-openapi31.yaml")]
    [InlineData("minimal-swagger.json")]
    [InlineData("actions-openapi.yaml")]
    [InlineData("relationships-openapi.yaml")]
    [InlineData("airline-reservation-openapi.yaml")]
    public async Task BuildsRealSpecificationsAndExportRoundTrips(string sample)
    {
        var result = await Run("build", "--input", Sample(sample), "--output", directory);
        Assert.True(result.Code == 0, result.Error);
        Assert.Contains("Exported", result.Output);
        await using var stream = File.OpenRead(Path.Combine(directory, "ontology.json"));
        var ontology = await new JsonOntologySerializer().ReadAsync(stream, default);
        Assert.NotEmpty(ontology.Entities);
        if (sample == "actions-openapi.yaml") Assert.Equal(4, ontology.Actions.Count);
        if (sample == "relationships-openapi.yaml") Assert.Equal(6, ontology.Relationships.Count);
        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.json")));
        Assert.Equal(JsonValueKind.Array, diagnostics.RootElement.ValueKind);
        Assert.All(diagnostics.RootElement.EnumerateArray(), d => Assert.NotEqual("Error", d.GetProperty("severity").GetString()));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        var bundled = await File.ReadAllTextAsync(Path.Combine(directory, "ontology-data.js"));
        const string prefix = "globalThis.seldonOntologyJson = ";
        Assert.StartsWith(prefix, bundled);
        var bundledJson = JsonSerializer.Deserialize<string>(bundled[prefix.Length..].Trim().TrimEnd(';'));
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(directory, "ontology.json")), bundledJson);
        foreach (var asset in new[] { "index.html", "viewer.js", "viewer.css" })
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(Root, "viewer", asset)),
                await File.ReadAllBytesAsync(Path.Combine(directory, asset)));
    }

    [Fact]
    public async Task RepeatBuildsProduceIdenticalOntologyAndDiagnosticBytes()
    {
        var arguments = new[] { "build", "--input", Sample("relationships-openapi.yaml"), "--output", directory };
        Assert.Equal(0, (await Run(arguments)).Code);
        var ontology = await File.ReadAllBytesAsync(Path.Combine(directory, "ontology.json"));
        var diagnostics = await File.ReadAllBytesAsync(Path.Combine(directory, "diagnostics.json"));
        Assert.Equal(0, (await Run(arguments)).Code);
        Assert.Equal(ontology, await File.ReadAllBytesAsync(Path.Combine(directory, "ontology.json")));
        Assert.Equal(diagnostics, await File.ReadAllBytesAsync(Path.Combine(directory, "diagnostics.json")));
    }

    [Fact]
    public async Task FailedBuildReportsDiagnosticsAndRetainsPreviousOntology()
    {
        Assert.Equal(0, (await Run("build", "--input", Sample("minimal-openapi.yaml"), "--output", directory)).Code);
        var original = await File.ReadAllBytesAsync(Path.Combine(directory, "ontology.json"));
        var failed = await Run("build", "--input", Path.Combine(directory, "missing.yaml"), "--output", directory);
        Assert.Equal(1, failed.Code);
        Assert.Contains("Build failed", failed.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(directory, "ontology.json")));
        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.json")));
        Assert.Contains(diagnostics.RootElement.EnumerateArray(), d => d.GetProperty("severity").GetString() == "Error");
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task MalformedInputDoesNotExportAnOntology()
    {
        var input = Path.Combine(directory, "broken.yaml");
        await File.WriteAllTextAsync(input, "openapi: [broken");
        var output = Path.Combine(directory, "output");
        Assert.Equal(1, (await Run("build", "--input", input, "--output", output)).Code);
        Assert.False(File.Exists(Path.Combine(output, "ontology.json")));
        Assert.True(File.Exists(Path.Combine(output, "diagnostics.json")));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("unknown")]
    [InlineData("build --input x --output")]
    [InlineData("build --input x --input y --output z")]
    [InlineData("inspect --input x --output z")]
    [InlineData("build --input x --output y --extra z")]
    [InlineData("inspect --input x --allow-partial --allow-partial")]
    public async Task InvalidArgumentsHaveUsageExitCode(string arguments)
    {
        var result = await Run(arguments.Split(' '));
        Assert.Equal(2, result.Code);
        Assert.Contains("Usage:", result.Error);
    }

    [Fact]
    public async Task OutputErrorsReturnFailureWithoutTemporaryFiles()
    {
        var file = Path.Combine(directory, "not-a-directory");
        await File.WriteAllTextAsync(file, "preserve me");
        var result = await Run("build", "--output", file, "--input", Sample("minimal-openapi.yaml"));
        Assert.Equal(1, result.Code);
        Assert.Contains("Operation failed", result.Error);
        Assert.Equal("preserve me", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task InputCannotBeOverwrittenByExport()
    {
        var input = Path.Combine(directory, "ontology.json");
        var original = await File.ReadAllTextAsync(Sample("minimal-swagger.json"));
        await File.WriteAllTextAsync(input, original);
        var result = await Run("build", "--input", input, "--output", directory);
        Assert.Equal(2, result.Code);
        Assert.Equal(original, await File.ReadAllTextAsync(input));
    }

    [Fact]
    public async Task InspectCommandStillWorks()
    {
        var result = await Run("inspect", "--input", Sample("minimal-openapi.yaml"));
        Assert.Equal(0, result.Code);
        Assert.Contains("Operations: 1", result.Output);
    }

    [Fact]
    public async Task PartialModeWorksForInspectAndBuildWhileDefaultRemainsStrict()
    {
        var input = Sample("partial-openapi.json");
        var original = await File.ReadAllBytesAsync(input);
        Assert.Equal(1, (await Run("inspect", "--input", input)).Code);
        var inspected = await Run("inspect", "--input", input, "--allow-partial");
        Assert.Equal(0, inspected.Code);
        Assert.Contains("Operations: 1", inspected.Output);
        Assert.Contains("partial-import-enabled", inspected.Error);
        var built = await Run("build", "--input", input, "--output", directory, "--allow-partial");
        Assert.Equal(0, built.Code);
        Assert.Contains("Partial import: this bundle", await File.ReadAllTextAsync(Path.Combine(directory, "index.html")));
        await using var stream = File.OpenRead(Path.Combine(directory, "ontology.json"));
        var ontology = await new JsonOntologySerializer().ReadAsync(stream, default);
        Assert.Equal("listItems", Assert.Single(ontology.Actions).OperationId);
        var item = Assert.Single(ontology.Entities);
        Assert.Equal("unknown", item.Properties.Single(p => p.Name == "details").DataType);
        Assert.Null(item.Properties.Single(p => p.Name == "details").ReferencedSchemaId);
        Assert.Empty(ontology.Relationships);
        using var diagnostics = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "diagnostics.json")));
        Assert.Contains(diagnostics.RootElement.EnumerateArray(), d => d.GetProperty("code").GetString() == "partial-unresolved-schema");
        Assert.Equal(original, await File.ReadAllBytesAsync(input));
    }

    [Fact]
    public async Task BuilderPersistsSuccessfulOntologyAndPropagatesCancellation()
    {
        var repository = new InMemoryOntologyRepository();
        var builder = new OntologyBuilder(new OpenApiDocumentLoader(), new EntityExtractor(), new RelationshipExtractor(),
            new ActionExtractor(), new InMemoryOntologyFactory(), repository);
        var built = await builder.BuildAsync(Sample("relationships-openapi.yaml"), default);
        Assert.False(built.HasErrors);
        Assert.NotNull(built.Ontology);
        var stored = await repository.GetAsync(built.Ontology.Id, default);
        Assert.Equal(6, stored!.Relationships.Count);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => builder.BuildAsync(Sample("minimal-openapi.yaml"), cancellation.Token));
    }

    [Fact]
    public async Task AirlinePipelineHasExpectedTypesEdgesActionsAndStableRoundTrip()
    {
        var arguments = new[] { "build", "--input", Sample("airline-reservation-openapi.yaml"), "--output", directory };
        var first = await Run(arguments);
        Assert.True(first.Code == 0, first.Error);
        var filename = Path.Combine(directory, "ontology.json");
        var bytes = await File.ReadAllBytesAsync(filename);
        var diagnostics = await File.ReadAllBytesAsync(Path.Combine(directory, "diagnostics.json"));
        using var input = new MemoryStream(bytes);
        var serializer = new JsonOntologySerializer();
        var ontology = await serializer.ReadAsync(input, default);
        Assert.Equal(new[] { "Booking", "Flight", "Passenger" }, ontology.Entities.Select(e => e.Name));
        Assert.Equal(4, ontology.Relationships.Count);
        Assert.Contains(ontology.Relationships, r => r.SourceEntityId == "schema:/components/schemas/Booking" &&
            r.TargetEntityId == "schema:/components/schemas/Flight" && r.EvidenceKind == "ExplicitSchemaRef" && r.Cardinality == "one");
        Assert.Contains(ontology.Relationships, r => r.Name == "passengers" && r.Cardinality == "many");
        Assert.Equal("unknown", Assert.Single(ontology.Relationships, r => r.EvidenceKind == "Heuristic").Cardinality);
        Assert.Equal(3, ontology.Actions.Count);
        Assert.Empty(ontology.Actions.Single(a => a.OperationId == "getHealth").EntityIds);
        var list = ontology.Actions.Single(a => a.OperationId == "listPassengerBookings");
        Assert.Equal(new[] { "schema:/components/schemas/Booking", "schema:/components/schemas/Passenger" }, list.EntityIds);
        Assert.Contains("operation:createBooking", ontology.FindEntity("schema:/components/schemas/Booking")!.ActionIds);
        using var output = new MemoryStream();
        await serializer.WriteAsync(ontology, output, default);
        Assert.Equal(bytes, output.ToArray());
        Assert.Equal(0, (await Run(arguments)).Code);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(filename));
        Assert.Equal(diagnostics, await File.ReadAllBytesAsync(Path.Combine(directory, "diagnostics.json")));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
