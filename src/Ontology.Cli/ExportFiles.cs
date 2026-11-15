using System.Text.Json;
using System.Text.Json.Serialization;
using Ontology.Core;

namespace Ontology.Cli;

internal static class ExportFiles
{
    private static readonly JsonSerializerOptions DiagnosticOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal static async Task WriteAsync(string directory, OntologyBuildResult result, IOntologySerializer serializer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var token = Guid.NewGuid().ToString("N");
        var diagnosticTemp = Path.Combine(directory, ".diagnostics-" + token + ".tmp");
        var ontologyTemp = Path.Combine(directory, ".ontology-" + token + ".tmp");
        var viewerTemps = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await using (var output = new FileStream(diagnosticTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                await JsonSerializer.SerializeAsync(output, result.Diagnostics, DiagnosticOptions, ct);
            if (!result.HasErrors && result.Ontology is { } ontology)
            {
                await using (var output = new FileStream(ontologyTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                    await serializer.WriteAsync(ontology, output, ct);
                foreach (var name in new[] { "index.html", "viewer.js", "viewer.css" })
                {
                    var temporary = Path.Combine(directory, ".viewer-" + token + "-" + name + ".tmp");
                    viewerTemps.Add(name, temporary);
                    await using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "viewer", name));
                    await using var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true);
                    await source.CopyToAsync(destination, ct);
                }
            }
            ct.ThrowIfCancellationRequested();
            File.Move(diagnosticTemp, Path.Combine(directory, "diagnostics.json"), true);
            if (!result.HasErrors && result.Ontology is not null)
            {
                File.Move(ontologyTemp, Path.Combine(directory, "ontology.json"), true);
                foreach (var (name, temporary) in viewerTemps)
                    File.Move(temporary, Path.Combine(directory, name), true);
            }
        }
        finally
        {
            // Only these uniquely named temporary files are owned by this export.
            File.Delete(diagnosticTemp);
            File.Delete(ontologyTemp);
            foreach (var temporary in viewerTemps.Values) File.Delete(temporary);
        }
    }
}
