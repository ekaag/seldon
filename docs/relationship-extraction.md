# Relationship extraction (Slice 1.6)

`Ontology.Builder.RelationshipExtractor` implements `IRelationshipExtractor`. Pass
the loaded `ApiDocument` and the entities returned by `EntityExtractor`. The result
contains a read-only relationship collection plus structured diagnostics.

```csharp
var entityResult = entityExtractor.Extract(document, ct);
var relationshipResult = relationshipExtractor.Extract(document, entityResult.Entities, ct);
IOntology ontology = new InMemoryOntology("catalog", document.Title,
    entityResult.Entities, relationshipResult.Relationships, []);
await serializer.WriteAsync(ontology, destination, ct);
```

Keep loader, entity, and relationship diagnostics together at the composition root.
The sample `samples/relationships-openapi.yaml` produces six relationships: four
property references, one deduplicated `allOf` marker, and one heuristic candidate.

## Structural rules

- A property referencing an extracted entity produces `ExplicitSchemaRef` evidence
  with cardinality `one`. This means a single-valued field, not required presence.
- References under arrays produce `many`, including nested arrays and object values
  within arrays. Named primitive references do not become relationships.
- Nested property references retain a readable property path as their label and
  their precise source pointer. `SourcePropertyName` identifies the owning entity's
  top-level property so it satisfies the existing ontology integrity contract.
- Direct references in `allOf` produce a marker named `allOf`, with explicit evidence,
  no source property, and `unknown` cardinality. This is composition evidence rather
  than an inferred business relationship or a claim of strict OO inheritance.
- Inherited properties are already flattened by entity extraction and retain their
  declaration provenance. No inverse or transitive relationships are invented.

## Foreign-key candidates

Only string/integer fields ending in `id` (case insensitive) are considered. Remove
the suffix and trailing `_`/`-`, then match the complete remaining name against named
entity names, case insensitively. Bare `id`, anonymous entity names, fuzzy/plural
matches, primitive schema references, and already explicitly referenced fields are
excluded. A unique match creates `Heuristic` evidence and an
`heuristic-relationship-candidate` warning saying the relationship is unverified.
Scalar candidate cardinality is `unknown`; a candidate inside a collection is `many`.
Ambiguous matches produce `ambiguous-foreign-key` diagnostics and no edge. No match
produces no edge. Candidate evidence must not be treated as confirmed structural evidence.

## Identity, validation, and limits

Relationship IDs encode source entity, canonical semantic property path (or `allOf`),
target entity, and evidence kind separately. Repeated references to the same base
deduplicate; separate roles/properties remain separate edges. IDs and output order
do not depend on entity catalog traversal order or source file location.

Every emitted endpoint must exist in the supplied entity catalog. Missing object
targets produce diagnostics rather than dangling edges. The existing in-memory
validation and JSON serializer preserve endpoint IDs, source property, labels,
cardinality, evidence, and source pointers. No additional schema version is introduced.

Traversal is bounded to depth 32 and 100,000 visits and honors cancellation. `oneOf`,
`anyOf`, map-value relationships, and indirect/transitive composition interpretation
are not inferred. Upstream extraction diagnostics continue to describe unsupported
or conflicting schema constructs. CLI build/export and action extraction remain for
their later slices; the inspect command is unchanged.
