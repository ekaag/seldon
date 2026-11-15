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

## Explicit partial import

`inspect` and `build` accept `--allow-partial`. Without this flag, the default
strict behavior above is unchanged. With it:

- Missing internal references at cataloged **schema** locations produce
  `partial-unresolved-schema` warnings and retain null resolution targets. No
  replacement types or relationships are invented; affected property types may
  remain unknown. Missing references to other structures are still errors.
- The misplaced `info.externalDocs` property and parser list-shape errors in
  security requirements produce `partial-parser-metadata` warnings. The original
  syntax is retained; this is not a source-file repair.
- Operations whose effective security (operation override or root inheritance)
  is not an array of objects with string-array scopes are omitted from the
  operation catalog with `partial-operation-skipped`. Security is not normalized
  to empty scopes, anonymous access, or guessed requirements. Other valid operations
  can still be exported.
- `partial-import-enabled` is always included in diagnostics. Successful bundles
  display a partial-import notice in `index.html`; diagnostics travel separately
  in `diagnostics.json`. A standalone `ontology.json` does not embed those diagnostics.

Malformed JSON/YAML, size/depth limits, unsupported specification versions, unrelated
parser errors, and missing non-schema reference targets still fail. No external
documents are fetched. Exit 0 means a partial inspection/export succeeded, not that
the source specification passed complete validation. Warnings are written to stderr;
PowerShell may format them as native-command error records even when `$LASTEXITCODE`
is 0.

The synthetic `samples/partial-openapi.json` fixture exercises these cases without
depending on an external API file. Square's supplied input exposed the same three
issues: misplaced `externalDocs`, null OAuth scopes, and absent `AppFeeAllocation`
and `CurrencyExchange` definitions. See the [OpenAPI 3.0 specification](https://spec.openapis.org/oas/v3.0.0.html)
for the Info Object and Security Requirement Object formats.
