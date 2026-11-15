using System.Collections.Concurrent;
using Ontology.Core;

namespace Ontology.Infrastructure;

/// <summary>Stores validated immutable snapshots for this repository instance's lifetime.</summary>
public sealed class InMemoryOntologyRepository : IOntologyRepository
{
    private readonly ConcurrentDictionary<string, IOntology> ontologies = new(StringComparer.Ordinal);

    /// <summary>Atomically replaces an existing ontology with the same ID after validation.</summary>
    public Task SaveAsync(IOntology ontology, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(ontology);
        var snapshot = new InMemoryOntology(ontology.Id, ontology.Name, ontology.Entities, ontology.Relationships, ontology.Actions);
        ct.ThrowIfCancellationRequested();
        ontologies[snapshot.Id] = snapshot;
        return Task.CompletedTask;
    }

    public Task<IOntology?> GetAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Task.FromResult(ontologies.GetValueOrDefault(id));
    }
}
