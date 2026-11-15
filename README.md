# Seldon ontology builder

Requires the .NET 10 SDK. Node.js 18+ is needed only for viewer tests. From the repository root:

```powershell
dotnet build seldon.sln
dotnet test seldon.sln --no-build
node --test tests/viewer/viewer.test.cjs
dotnet run --project src/Ontology.Cli -- build --input "samples/actions-openapi.yaml" --output "out/actions"
```

For your own specification:

```powershell
dotnet run --project src/Ontology.Cli -- build --input "C:\path\to\your-api.yaml" --output "out/my-api"
```

The command writes `ontology.json`, `diagnostics.json`, `index.html`, `viewer.js`,
`viewer.css`, and `ontology-data.js` (a safely encoded copy for automatic local loading). The ontology contains
entities, properties, relationships, and associated/unbound actions. Inspect the
JSON in your editor, or open the output folder's `index.html` to see the connected graph immediately.
See [viewer instructions](docs/viewer.md). No server is required.

After a successful build, the CLI opens the exported `index.html` in Google Chrome.
Click an entity node for properties and actions, or an edge for relationship evidence.
The file picker lets you load a different ontology. If Chrome is unavailable,
the CLI prints a warning and retains the successful export. Use `--no-open` for
automated/headless runs:

```powershell
dotnet run --project src/Ontology.Cli -- build --input samples/actions-openapi.yaml --output out/actions --no-open
```

OpenAPI 3.0/3.1 and Swagger 2.0 are accepted in YAML or JSON. You can inspect counts
without exporting:

```powershell
dotnet run --project src/Ontology.Cli -- inspect --input "samples/minimal-openapi.yaml"
```

## Build behavior

The CLI composition root registers the loader, three extractors, ontology factory,
in-memory repository, builder, and JSON serializer through Core interfaces. Builder
depends only on Core. Successful builds validate and store an ontology in the
process-local repository before exporting; the repository does not persist after exit.

Ontology IDs use the input filename without its extension; names use the API title.
Entity/action/relationship IDs follow the extraction rules in the documents below.
Repeat builds of the same input path/content produce identical JSON bytes, including
diagnostics. No timestamps or random export IDs appear in final output. Provenance
preserves the supplied input path, so changing that path can change JSON bytes.

Warnings are printed and exported without failing a build. Fatal errors produce
nonzero exit codes and `diagnostics.json` when the output directory is writable.
A failed build retains any previous `ontology.json`; check the exit code and current
diagnostics before using an existing output. Invalid arguments/input-output collisions
are rejected before exporting. Output I/O failures are reported on stderr.

All six files are staged under unique temporary filenames in the destination directory
and renamed only after generation succeeds. Each rename replaces one complete file;
the bundle is not a single transaction, so interruption or an I/O error between renames
can leave outputs from different runs. Temporary files are cleaned on normal failures
and cancellation; a forcibly terminated process can leave them behind. Existing
exports are replaced on successful builds. Concurrent writers to one directory are
not coordinated.

Exit codes: **0** success (warnings allowed), **1** build/I/O failure, **2** invalid
arguments, **130** cancellation. Ctrl+C requests cancellation. In PowerShell, inspect
`$LASTEXITCODE` immediately after the command.

## Current limits

External references are never fetched; they produce warnings. YAML aliases are
unsupported. Inference is deterministic and conservative, and heuristic relationships
or action associations remain explicitly unverified. Unsupported/complex schema
semantics produce diagnostics or partial metadata as described in the extraction docs.
Actions are metadata only: no API operations, authentication, or workflows execute.

- [API inspection and parser limits](docs/api-inspection.md)
- [Entity/property extraction](docs/entity-extraction.md)
- [Relationship extraction](docs/relationship-extraction.md)
- [Action extraction](docs/action-extraction.md)
- [JSON format](docs/ontology-json.md)

## Reproduce the airline demonstration

```powershell
dotnet run --project src/Ontology.Cli -- build --input samples/airline-reservation-openapi.yaml --output out/airline
```

Expected result: **3 entities, 4 relationships, 3 actions**, with warnings but no
errors. The types are Booking, Flight, and Passenger. A reference to an existing
named type reuses that entity rather than creating an inline duplicate.

Open `out/airline/index.html` in your browser. Its graph loads automatically. Then:

1. Select **Booking**. Inspect required properties and expand `createBooking`.
2. Select the **flight · one** edge. Its evidence is `ExplicitSchemaRef`.
3. Select **passengers · many**. Its source pointer identifies the array item reference.
4. Select the dashed **flightId · unknown** edge. It is an unverified `Heuristic`.
5. Select **Passenger**, then **Focus selected + neighbors**. Use **Show all** to reset.
6. Search for **Flight**, then clear the search. Expand **getHealth** under unbound actions.

The viewer works locally and never calls the API. These are schema types, not actual
passenger/booking records. You can move the entire output folder to another machine;
keep all six files together. Automatic loading uses the export-time snapshot;
after editing `ontology.json` manually, use the file picker or rebuild to refresh it.

[Demo results, expected warnings, verification status, and remaining limits](docs/demo-results.md)
document the final Slice 1.11 checks. **Automated verification is complete; real-browser
file-picker and visual verification remain pending because browser access is unavailable.**
No browser screenshots were captured. See [ontologybuilder.md](ontologybuilder.md)
for the original scope.
