# Action extraction (Slice 1.7)

`ActionExtractor` implements `IActionExtractor` using only Core contracts. Pass the
loaded document and extracted entities. The result contains all actions, replacement
entity snapshots with associated `ActionIds`, and structured diagnostics. Use the
returned entities when constructing `InMemoryOntology`:

```csharp
var entities = entityExtractor.Extract(document, ct);
var relationships = relationshipExtractor.Extract(document, entities.Entities, ct);
var actions = actionExtractor.Extract(document, entities.Entities, ct);
IOntology ontology = new InMemoryOntology("catalog", document.Title,
    actions.Entities, relationships.Relationships, actions.Actions);
await serializer.WriteAsync(ontology, destination, ct);
```

Keep loader and all extraction diagnostics together. No operation is invoked.

## Catalog and metadata

Every path operation is retained, including unbound actions. The loader also catalogs
callback and OpenAPI 3.1 webhook operations with their source pointers; their `Path`
contains the callback expression or webhook name. Callback traversal is limited to
depth 32 and 10,000 catalog entries. Expressions are data and are never evaluated.

An action retains method, path, operation ID, summary, description, tags, source
pointer, effective parameters, request schemas/media types and required flag,
response status codes/descriptions/schemas/media types, and security requirements.
Parameter metadata includes name, location, required flag, description, type, format,
schema ID, and source pointer. Path-level parameters are inherited and operation-level
parameters override the same location/name (header names are case insensitive).
Overridden schemas do not contribute associations. Swagger body parameters and
OpenAPI request bodies normalize into request schemas. Parameters using `content`
contribute schema evidence but are not treated as request bodies.

Internal parameter, body, response, and schema references resolve locally. Schema
IDs are canonical source-pointer IDs, including primitives and inline schemas. With
multiple distinct request schemas, `RequestSchemaId` is null and `RequestSchemas`
preserves every binding. `ResponseSchemaIds` is a distinct catalog; `Responses`
preserves status and media-type context, including responses without a schema.

Security is a collection of immutable cloned JSON requirement objects (scheme names
mapped to scope arrays). Root security is inherited unless an operation explicitly
specifies security. An explicit empty array overrides root security. Empty objects
are preserved as anonymous alternatives. JSON output canonicalizes scheme and scope
ordering. These declarations are metadata, not credentials or an authentication
implementation; security scheme definitions and full parameter serialization styles
are not yet exported. No safety classification is inferred from HTTP methods.

## Association and identity

Explicit schema references and inline entity schemas are primary evidence. Arrays
and composition are traversed where needed; union alternatives are only possible
types and produce diagnostics. Named object schemas stop traversal so their entire
relationship graph is not implicitly bound to an operation.

Route context can supplement schema evidence for a different entity. Fallback matches
an entire route segment against a named entity (case insensitive), or the entity name
plus `s`. Declared path parameters ending in `id` can match their remaining stem.
Only unique matches bind; ambiguous matches are diagnosed. Fallback evidence is
`RouteHeuristic`/`ParameterHeuristic` and emits an unverified-association warning.
`Associations` retains entity ID, evidence kind, and source pointer. `EntityIds` and
the returned entities' `ActionIds` reflect the same bindings. Unmatched operations
remain unbound and emit `unbound-action` diagnostics.

Unique operation IDs produce `operation:` plus URI-escaped operation ID. Missing,
blank, or duplicate IDs use `operation:pointer:` plus URI-escaped source pointer;
duplicates generate diagnostics. IDs and serialization are independent of catalog
traversal order and source file location. Metadata collections are read-only snapshots.

The existing in-development JSON 1.0 DTOs gain optional action fields. Old input
without them still loads; new fields are validated and preserved on round trip.
`samples/actions-openapi.yaml` demonstrates multiple entities, a write operation,
parameter overrides, root security, a public override, and unbound actions.
Slice 1.8 wires the full CLI `build --input ... --output ...` pipeline; see the README.
