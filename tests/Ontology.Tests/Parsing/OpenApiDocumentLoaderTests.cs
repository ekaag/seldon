using System.Text.Json.Nodes;
using System.Text.Json;
using Ontology.Core;
using Ontology.Infrastructure;

namespace Ontology.Tests.Parsing;

public sealed class OpenApiDocumentLoaderTests
{
    private readonly OpenApiDocumentLoader loader = new();

    private static string Sample(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "seldon.sln"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "samples", name);
    }

    private async Task<ApiDocumentLoadResult> LoadText(string text, string extension = ".json")
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            await File.WriteAllTextAsync(path, text);
            return await loader.LoadAsync(path, default);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("minimal-openapi.yaml", "3.0")]
    [InlineData("minimal-openapi31.yaml", "3.1")]
    [InlineData("minimal-swagger.json", "2.0")]
    public async Task VersionFixturesLoadAndTheirJsonFormAlsoLoads(string fixture, string version)
    {
        var result = await loader.LoadAsync(Sample(fixture), default);
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var document = Assert.IsType<ApiDocument>(result.Document);
        Assert.Equal(version, document.Version);
        Assert.Equal("Minimal Catalog", document.Title);
        Assert.Single(document.Operations);
        Assert.Single(document.Schemas, s => s.Name is not null);
        Assert.True(document.Schemas.Count >= 4);
        Assert.All(document.References, reference => Assert.NotNull(reference.TargetPointer));
        var jsonResult = await LoadText(document.Root.GetRawText());
        Assert.False(jsonResult.HasErrors, string.Join("; ", jsonResult.Diagnostics.Select(d => d.Message)));
        Assert.Equal(document.Schemas.Count, jsonResult.Document!.Schemas.Count);
        Assert.Equal(document.Operations.Count, jsonResult.Document.Operations.Count);
    }

    [Fact]
    public async Task SwaggerYamlLoads()
    {
        var result = await LoadText("""
            swagger: '2.0'
            info:
              title: YAML Swagger
              version: '1'
            paths:
              /health:
                get:
                  responses:
                    '200':
                      description: OK
            """, ".yaml");
        Assert.False(result.HasErrors);
        Assert.Equal("2.0", result.Document!.Version);
        Assert.Single(result.Document.Operations);
    }

    [Fact]
    public async Task NonstandardSwaggerSchemaListsLoadWithWarningsAndOriginalProvenance()
    {
        var result = await LoadText("""
            {"swagger":"2.0","info":{"title":"Legacy schemas","version":"1"},"paths":{},
             "definitions":{"User":{"type":"object","properties":{
                "label":{"type":["null","string"]},
                "variant":{"items":[{"type":"object","properties":{"label":{"type":["string","null"]}}},{"type":"null"}]}
             }}}}
            """);
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);
        Assert.Contains(result.Diagnostics, d => d.Code == "swagger-schema-compatibility" && d.Provenance!.SourcePointer == "#/definitions/User/properties/label/type");
        Assert.Contains(result.Document.Schemas, s => s.Provenance.SourcePointer == "/definitions/User/properties/variant/items/0/properties/label");
        Assert.Equal(2, result.Document.Root.GetProperty("definitions").GetProperty("User").GetProperty("properties").GetProperty("variant").GetProperty("items").GetArrayLength());
        var extraction = new Ontology.Builder.EntityExtractor().Extract(result.Document, default);
        var user = Assert.Single(extraction.Entities);
        Assert.Equal("string", user.Properties.Single(p => p.Name == "label").DataType);
        Assert.True(user.Properties.Single(p => p.Name == "label").Nullable);
        Assert.Equal("unknown", user.Properties.Single(p => p.Name == "variant").DataType);
        Assert.Contains(extraction.Diagnostics, d => d.Code == "unsupported-items-list");
    }

    [Theory]
    [InlineData("{\"type\":[\"string\",42]}")]
    [InlineData("{\"items\":[42]}")]
    [InlineData("{\"items\":42}")]
    public async Task InvalidSwaggerSchemaShapesRemainFatal(string schema)
    {
        var result = await LoadText("{\"swagger\":\"2.0\",\"info\":{\"title\":\"Invalid\",\"version\":\"1\"},\"paths\":{},\"definitions\":{\"Bad\":" + schema + "}}");
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Code == "openapi-parser-error");
    }

    [Fact]
    public async Task InlineRequestSchemasNullableTypesAndRecursiveReferencesAreRetained()
    {
        var result = await loader.LoadAsync(Sample("minimal-openapi31.yaml"), default);
        var document = result.Document!;
        Assert.Contains(document.Schemas, s => s.Provenance.SourcePointer.Contains("/requestBody/content/application~1json/schema"));
        var nullable = document.Schemas.Single(s => s.Provenance.SourcePointer.EndsWith("/properties/label"));
        Assert.Contains("null", nullable.Definition.GetProperty("type").GetRawText());
        Assert.Contains(document.References, r => r.TargetPointer == "/components/schemas/Item");
        Assert.Equal("object", document.ResolveReference("#/components/schemas/Item")!.Value.GetProperty("type").GetString());
        Assert.Null(document.ResolveReference("https://example.invalid/schema.json"));
    }

    [Theory]
    [InlineData("https://127.0.0.1:1/never-request.json#/Thing")]
    [InlineData("file:///does-not-exist.yaml#/Thing")]
    [InlineData("other.yaml#/Thing")]
    public async Task ExternalReferencesAreNotLoadedAndProduceWarnings(string reference)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(Sample("minimal-swagger.json")))!;
        root["paths"]!["/items"]!["get"]!["responses"]!["200"]!["schema"]!["items"]!["$ref"] = reference;
        var result = await LoadText(root.ToJsonString());
        Assert.Contains(result.Diagnostics, d => d.Code == "external-reference-unsupported" && d.Severity == DiagnosticSeverity.Warning);
        Assert.False(result.HasErrors, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Document);
        Assert.Contains(result.Document.References, r => r.Value == reference && r.TargetPointer is null);
    }

    [Fact]
    public async Task MissingLocalReferencesAreErrors()
    {
        var json = (await File.ReadAllTextAsync(Sample("minimal-swagger.json"))).Replace("#/definitions/Item", "#/definitions/Missing");
        var result = await LoadText(json);
        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Missing"));
    }

    [Fact]
    public async Task PartialImportPreservesMissingSchemasAndOmitsMalformedSecurityOperations()
    {
        var strict = await loader.LoadAsync(Sample("partial-openapi.json"), default);
        Assert.True(strict.HasErrors);
        var partial = await new OpenApiDocumentLoader(new(AllowPartial: true)).LoadAsync(Sample("partial-openapi.json"), default);
        Assert.False(partial.HasErrors, string.Join("; ", partial.Diagnostics.Select(d => d.Message)));
        var document = Assert.IsType<ApiDocument>(partial.Document);
        Assert.Equal("listItems", Assert.Single(document.Operations).Definition.GetProperty("operationId").GetString());
        Assert.Contains(partial.Diagnostics, d => d.Code == "partial-import-enabled");
        Assert.Contains(partial.Diagnostics, d => d.Code == "partial-parser-metadata");
        Assert.Contains(partial.Diagnostics, d => d.Code == "partial-operation-skipped" && d.Provenance!.SourcePointer == "/paths/~1revocations/post");
        Assert.Contains(partial.Diagnostics, d => d.Code == "partial-unresolved-schema");
        Assert.Null(document.References.Single(r => r.Value.EndsWith("MissingDetails", StringComparison.Ordinal)).TargetPointer);
        Assert.Null(document.ResolveReference("#/components/schemas/MissingDetails"));
        Assert.Equal(JsonValueKind.Null, document.Root.GetProperty("paths").GetProperty("/revocations").GetProperty("post").GetProperty("security")[0].GetProperty("bearerAuth").ValueKind);
        Assert.True(document.Root.GetProperty("info").TryGetProperty("externalDocs", out _));
    }

    [Theory]
    [InlineData("{", "document-load-failed")]
    [InlineData("{\"openapi\":\"9.0.0\",\"info\":{\"title\":\"X\",\"version\":\"1\"},\"paths\":{}}", "document-load-failed")]
    [InlineData("{\"openapi\":\"3.0.0\",\"info\":{\"title\":\"X\",\"version\":\"1\"},\"paths\":{\"/items\":{\"$ref\":\"#/components/pathItems/Missing\"}}}", "unresolved-reference")]
    public async Task PartialImportDoesNotIgnoreStructuralFailures(string text, string code)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(path, text);
            var result = await new OpenApiDocumentLoader(new(AllowPartial: true)).LoadAsync(path, default);
            Assert.True(result.HasErrors);
            Assert.Contains(result.Diagnostics, d => d.Code == code && d.Severity == DiagnosticSeverity.Error);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{", ".json")]
    [InlineData("openapi: [broken", ".yaml")]
    [InlineData("{}", ".json")]
    [InlineData("[]", ".json")]
    [InlineData("null", ".json")]
    [InlineData("{\"openapi\":\"9.0.0\",\"info\":{\"title\":\"X\",\"version\":\"1\"},\"paths\":{}}", ".json")]
    public async Task InvalidDocumentsProduceDiagnostics(string text, string extension)
    {
        var result = await LoadText(text, extension);
        Assert.True(result.HasErrors);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task MissingFileAndOversizedInputsAreReported()
    {
        var missing = await loader.LoadAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), default);
        Assert.True(missing.HasErrors);
        Assert.Null(missing.Document);
        var oversized = await LoadText(new string(' ', OpenApiDocumentLoader.MaximumInputBytes + 1));
        Assert.Contains(oversized.Diagnostics, d => d.Code == "input-too-large");
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.LoadAsync(Sample("minimal-openapi.yaml"), cancellation.Token));
    }

    [Fact]
    public async Task EscapedPointersResolveWhileParserNameValidationIsPreserved()
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(Sample("minimal-swagger.json")))!;
        root["definitions"]!["A/B~C"] = root["definitions"]!["Item"]!.DeepClone();
        root["paths"]!["/items"]!["get"]!["responses"]!["200"]!["schema"]!["items"]!["$ref"] = "#/definitions/A~1B~0C";
        var result = await LoadText(root.ToJsonString());
        Assert.Contains(result.Diagnostics, d => d.Code == "openapi-parser-error" && d.Message.Contains("regular expression"));
        Assert.Equal("object", result.Document!.ResolveReference("#/definitions/A~1B~0C")!.Value.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("openapi: 3.0.3\ninfo: &i {title: Test, version: '1'}\npaths: {}\nx-copy: *i")]
    [InlineData("openapi: 3.0.3\nopenapi: 3.1.0\ninfo: {title: Test, version: '1'}\npaths: {}")]
    public async Task UnsupportedAliasesAndDuplicateYamlKeysAreReported(string yaml)
    {
        var result = await LoadText(yaml, ".yaml");
        Assert.True(result.HasErrors);
        Assert.Null(result.Document);
    }
}
