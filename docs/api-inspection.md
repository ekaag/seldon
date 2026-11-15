# API document inspection (Slice 1.4)

From the repository root:

```powershell
dotnet run --project src/Ontology.Cli -- inspect --input samples/minimal-openapi.yaml
dotnet run --project src/Ontology.Cli -- inspect --input samples/minimal-openapi31.yaml
dotnet run --project src/Ontology.Cli -- inspect --input samples/minimal-swagger.json
```

Replace the input path with your own local specification. This command prints title,
specification family, schema count (with a separate named-schema count), operation
count, and diagnostics. Exit codes: 0 success (warnings allowed), 1 load/validation
failure, 2 usage error, 130 cancellation. Ctrl+C cancels inspection.

Microsoft.OpenApi.YamlReader 3.10.2 parses OpenAPI 3.0.x/3.1.x and Swagger 2.0.
Tests exercise all three families in YAML and JSON. YamlDotNet preserves the original
YAML syntax as JSON data so source pointers and external reference strings are not
changed by OpenAPI model reserialization. No library-specific types escape Infrastructure.

`IApiDocumentLoader` returns read-only schema, operation, and reference catalogs,
source provenance, original syntax as JSON, and structured parser diagnostics.
Schema counts include named, nested, primitive, array-item, and inline body schemas;
they are not ontology entity counts. Operations retain their syntax for later extraction.
`ApiDocument.ResolveReference` resolves cataloged internal JSON Pointer references,
including escaped names and reference chains, without recursively expanding schemas.
Recursive object graphs remain references; alias-only reference cycles return null.

External HTTP, relative-file, and absolute-file references are not loaded. They
produce warnings and retain their original text. Missing internal targets are errors.
Non-pointer anchors are unsupported and warned about. References inside examples,
defaults, enum values, and extension payloads are treated as data.

Limits: 16 MiB input, depth 64, 200,000 YAML parser events, one YAML document.
YAML aliases are explicitly unsupported; use internal `$ref` instead. Files ending
in `.json` use JSON parsing; other extensions use YAML parsing. JSON pointers refer
to the original source structure, not YAML line numbers. Specification versions are
reported as families (2.0, 3.0, 3.1). Parser diagnostics do not constitute exhaustive
OpenAPI conformance validation. Slice 1.7 adds webhook/callback operation catalogs.
Microsoft's parser validates Swagger definition names using its normalized component
rules, so names containing `/` or `~` can produce parser errors even though the
source reference catalog correctly resolves their escaped JSON Pointers.

This slice inspects syntax only. Ontology inference and the `build --output` command
remain for Slices 1.5–1.8; inspection does not write `ontology.json`.

## Legacy Swagger schema compatibility

Some Swagger 2.0 exports (including the Slack sample) contain JSON Schema-style
type lists and schema lists under `items`. These are not valid Swagger 2.0 schema
forms. For nonempty lists of recognized type names or object-valued item schemas
at cataloged schema locations, the corresponding parser shape errors become
`swagger-schema-compatibility` warnings. Original source definitions and pointers
are preserved; input files are not rewritten. Schema branches in `items` lists
are cataloged for provenance/reference resolution.

A single non-null type plus `null` retains its concrete type and nullability.
Multiple non-null types remain unknown as before. `items` lists are ambiguous:
they are not automatically reinterpreted as unions, and extraction reports
`unsupported-items-list` with potentially partial metadata. All unrelated parser
errors, malformed list members, and missing internal references remain fatal.
