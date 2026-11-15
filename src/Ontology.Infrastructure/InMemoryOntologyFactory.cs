using Ontology.Core;

namespace Ontology.Infrastructure;

public sealed class InMemoryOntologyFactory : IOntologyFactory
{
    public IOntology Create(string id, string name, IEnumerable<IEntityType> entities,
        IEnumerable<IRelationshipType> relationships, IEnumerable<IActionDefinition> actions) =>
        new InMemoryOntology(id, name, entities, relationships, actions);
}
