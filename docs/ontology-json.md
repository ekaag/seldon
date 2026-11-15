# Ontology JSON 1.0 (Slice 1.3)

`JsonOntologySerializer` implements `IOntologySerializer` using caller-owned streams.
The root object contains `schemaVersion` (currently exactly `"1.0"`) and `ontology`.
The DTOs in `src/Ontology.Infrastructure/Serialization/OntologyExport.cs` define the
transport shape independently of the domain interfaces.

The ontology contains `id`, `name`, `entities`, `relationships`, and `actions`.
All current domain fields are preserved, including descriptions, required flags,
source document/pointer provenance, relationship evidence/cardinality/source property,
operation IDs, and request/response schema IDs. Optional fields may be omitted or
null; required fields and arrays must be present and non-null. Arrays cannot contain
null elements. An empty source pointer represents the document root.

Output is indented UTF-8 JSON. Entities, relationships, and actions are sorted by ID;
properties by name; ID arrays by value. All comparisons are ordinal. Input order
does not affect output bytes. Strings use the default safe JSON escaping.

Import rejects unsupported versions (including future major versions), unknown or
duplicate JSON members, malformed types, missing required fields, duplicate IDs,
and invalid domain references. Errors use `JsonException`; stream I/O and cancellation
errors propagate. Streams remain open. The maximum JSON nesting depth is 64.

Request/response schema IDs need not identify entities: primitive schemas are valid.
Build diagnostics remain in `OntologyBuildResult` and are intended for the separate
`diagnostics.json` output; `IOntologySerializer` transports an ontology only.
Action parameters/security metadata and richer schema metadata will extend the
contracts and DTOs in their respective extraction slices. No OpenAPI loading,
CLI export command, or viewer is implemented in this slice.
