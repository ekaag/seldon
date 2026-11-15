# Entity and property extraction (Slice 1.5)

`Ontology.Builder.EntityExtractor` implements Core's `IEntityExtractor`. It consumes
the `ApiDocument` from `IApiDocumentLoader` and returns an `EntityExtractionResult`
with immutable entities and source-backed diagnostics. Loader diagnostics remain
on the load result; callers should check them before extracting.

Named object schemas become entities. Inline request/response object schemas and
anonymous object items in root arrays receive generated names. Primitive and array
schemas do not themselves become entities. Anonymous objects inside properties
remain nested value objects, including array-item objects.

IDs are `schema:` plus the canonical, escaped source JSON Pointer, such as
`schema:/components/schemas/Item` or `schema:/definitions/Item`. Inline IDs use their
full source pointer. They do not depend on file paths, traversal order, or hashes.
Swagger and OpenAPI use their respective source locations, so converting between
specification families can change IDs. Each entity retains its original name and
source document/pointer.

Properties preserve required flags, descriptions, formats, nullable markers,
typed JSON enum values, nested properties, array items, and referenced schema IDs.
Reference IDs can point to primitive schemas and are not relationship assertions.
Named object references are retained without recursively copying their properties.
Operation references to named object schemas reuse the canonical named entity instead
of producing duplicate inline entities (verified in the airline end-to-end test).
Inherited properties retain the pointer where they were declared; a referencing
property retains its own pointer. All collections are read-only snapshots.

`allOf` unions property discovery and required flags. Conflicting definitions of a
property produce a diagnostic and an `unknown` type rather than silently choosing
one constraint. `oneOf`/`anyOf` are not merged and generate warnings. Multiple
non-null 3.1 types are represented as `unknown` with a warning. Missing/external
references and cyclic composition also produce diagnostics. Expansion is bounded
to depth 32 and 100,000 schema visits; cancellation propagates.

The JSON serializer preserves the added property metadata (optional additive fields
in the in-development 1.0 format); older 1.0 input without those fields still loads.
New fields are `format`, `nullable`, `referencedSchemaId`, `enumValues`, `items`, and
`properties`. This is an extraction model, not an exhaustive JSON Schema validator:
numeric/string constraints, map constraints, and complex composition intersections
are not fully modeled.

Usage from C#:

```csharp
var loaded = await loader.LoadAsync(inputPath, ct);
if (loaded.HasErrors || loaded.Document is null)
    throw new InvalidOperationException("Review load diagnostics first.");
var extracted = new EntityExtractor().Extract(loaded.Document, ct);
IOntology ontology = new InMemoryOntology("catalog", loaded.Document.Title,
    extracted.Entities, [], []);
await using var output = File.Create("ontology.json");
await serializer.WriteAsync(ontology, output, ct);
```

`loader` and `serializer` are the existing Core interfaces; concrete adapters belong
in Infrastructure. The tests exercise the full loader → extraction → in-memory →
JSON round trip. This slice does not infer relationships/actions or change the CLI;
the CLI build/export command is provided by Slice 1.8 (see the README).
