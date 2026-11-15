# Task 1 — Ontology Builder (OpenAPI/Swagger → In-Memory Ontology → HTML Viewer)

## 1. Objective

Build a **standalone, domain-agnostic** .NET component that reads an OpenAPI/Swagger API description, constructs an ontology of **entities, properties, relationships, and entity-associated actions**, stores it behind interfaces using an **in-memory implementation**, serializes the ontology as portable JSON, and provides a **static HTML viewer** that loads the JSON and displays a navigable entity/relationship graph.

This is the **first task only** in the larger general-purpose assistant. Do not implement natural-language queries, live API calls, LLM integrations, workflow execution, databases, or cloud deployment.

### Required end-to-end path

```text
OpenAPI 3.x YAML/JSON or Swagger 2.0 YAML/JSON
    → document loader/parser
    → schema and operation normalization
    → ontology inference / relationship resolution
    → IOntology (in-memory implementation)
    → versioned JSON serialization
    → viewer/index.html + ontology.json
    → interactive entity/relationship/action exploration
```

## 2. Technology and design constraints

- C# / .NET (use the repository's current supported .NET SDK; if starting fresh, use .NET 8 or later).
- **Interface-first**: domain models and contracts must not depend on the parser, serializer, CLI, or viewer.
- Use **dependency injection** at the composition root. Keep implementations replaceable.
- In-memory ontology is the **only repository implementation** for this task.
- Use an established OpenAPI parsing library if suitable; do not write a full YAML/OpenAPI parser from scratch.
- No airline-specific classes, rules, schema names, or hardcoded relationships.
- No LLM required. Use deterministic extraction and explicit, conservative heuristics. Preserve evidence and uncertainty.
- HTML viewer must be usable without a backend service. It may use local static JavaScript and CSS; prefer bundled dependencies or native browser APIs. It must not execute arbitrary code from the source specification.
- Do not make network requests to resolve remote `$ref` values in this task. Support local/internal references; warn on unsupported external references.
- All parsing, inference, serialization, and visualization inputs must be treated as untrusted.

## 3. Inputs and outputs

### Input

A file path to an OpenAPI/Swagger document:

```bash
ontology-builder build --input samples/airline-reservation-openapi.yaml --output out/airline
```

Accept:
- OpenAPI 3.0.x and 3.1.x, YAML or JSON.
- Swagger/OpenAPI 2.0, YAML or JSON.
- Internal `$ref` references; handle nested schemas and arrays.
- Schemas in `components.schemas` (OpenAPI 3) or `definitions` (Swagger 2), plus schemas reachable only through operation request/response bodies.

### Output

```text
out/airline/
  ontology.json       # portable, versioned ontology representation
  index.html          # static interactive ontology viewer
  viewer.js           # if needed
  viewer.css          # if needed
  diagnostics.json    # parsing/inference warnings and unsupported features
```

The `index.html` page should offer a **Load ontology.json** file picker. Do not rely exclusively on `fetch('./ontology.json')`, because browser `file://` restrictions can prevent it. Optionally auto-load when served via an HTTP static server. The exported JSON must also be loadable independently by future components.

## 4. Domain contracts (design first)

Define small, extensible interfaces; names below are suggested and may be refined with a short justification.

```csharp
public interface IOntology
{
    string Id { get; }
    string Name { get; }
    IReadOnlyCollection<IEntityType> Entities { get; }
    IReadOnlyCollection<IRelationshipType> Relationships { get; }
    IReadOnlyCollection<IActionDefinition> Actions { get; }
    IEntityType? FindEntity(string entityId);
}

public interface IEntityType
{
    string Id { get; }
    string Name { get; }
    string? Description { get; }
    IReadOnlyCollection<IPropertyDefinition> Properties { get; }
    IReadOnlyCollection<string> ActionIds { get; }
}

public interface IPropertyDefinition
{
    string Name { get; }
    string DataType { get; }
    bool Required { get; }
    string? Description { get; }
}

public interface IRelationshipType
{
    string Id { get; }
    string SourceEntityId { get; }
    string TargetEntityId { get; }
    string Name { get; }
    string Cardinality { get; }
    string EvidenceKind { get; } // ExplicitSchemaRef | StructuralInference | Heuristic
    string SourcePointer { get; } // OpenAPI JSON Pointer or equivalent
}

public interface IActionDefinition
{
    string Id { get; }
    string Name { get; }
    string HttpMethod { get; }
    string Path { get; }
    string? OperationId { get; }
    IReadOnlyCollection<string> EntityIds { get; }
    string? RequestSchemaId { get; }
    IReadOnlyCollection<string> ResponseSchemaIds { get; }
    string SourcePointer { get; }
}

public interface IOntologyBuilder
{
    Task<OntologyBuildResult> BuildAsync(string inputPath, CancellationToken ct);
}

public interface IOntologyRepository
{
    Task SaveAsync(IOntology ontology, CancellationToken ct);
    Task<IOntology?> GetAsync(string id, CancellationToken ct);
}

public interface IOntologySerializer
{
    Task WriteAsync(IOntology ontology, Stream destination, CancellationToken ct);
    Task<IOntology> ReadAsync(Stream source, CancellationToken ct);
}
```

Implement these with immutable/read-only public models where practical, backed by `InMemoryOntology` and `InMemoryOntologyRepository`. Do not expose mutable internal collections. Include an `OntologyBuildResult` with ontology plus structured diagnostics.

**Important:** The ontology represents **types and capabilities**, not actual passenger/booking records. For example, `Booking → Flight` is a schema relationship; it is not a specific customer's booking.

## 5. Entity extraction rules

1. Parse the input and normalize OpenAPI/Swagger version differences.
2. Generate a stable entity ID from a canonical schema reference; preserve the original schema name and source pointer.
3. Convert named object schemas into entity candidates. Handle inline object schemas using stable generated IDs when needed.
4. Extract scalar properties, required flags, descriptions, enum values, formats, and arrays. Preserve nested value objects without turning every anonymous nested object into a top-level entity unnecessarily.
5. Resolve local `$ref` values and arrays of `$ref` values.
6. Support `allOf` composition at least for property/relationship discovery; warn rather than silently misrepresent unsupported `oneOf`/`anyOf` semantics.
7. Preserve source provenance for every entity/property where available.
8. Avoid treating primitive schemas as entities.
9. Use stable deterministic ordering for serialized output.

## 6. Relationship inference rules

Prioritize reliable structural evidence:

- **Explicit schema reference**: an entity property references another named entity (`$ref`).
- **Array of references**: an entity has a collection of another entity (`type: array`, `items: {$ref: ...}`).
- **Composition/inheritance**: capture an explicit typed relationship or inheritance marker, not an invented business relationship.
- **Foreign-key-like fields** (e.g., `passengerId`): may produce a *candidate* relationship only when the target entity can be unambiguously resolved. Label as `Heuristic` and include diagnostics/evidence; never silently upgrade to confirmed.
- Do **not** invent inverse relationships unless they are separately supported; viewer may visually traverse edges in either direction without asserting inverse semantics.

For each relationship record: source entity, target entity, source property (if applicable), label, cardinality (`one`, `many`, `unknown`), evidence kind, source pointer, and confidence/verification status if modeled. Duplicate references should be deduplicated deterministically without collapsing distinct semantic properties.

## 7. Action extraction and entity association

Read every HTTP operation and extract:

- HTTP method and route template.
- `operationId`, summary, description, tags.
- Path/query/header parameters and whether required.
- Request-body schema and response schemas/status codes.
- Security requirements (metadata only; do not authenticate or invoke).
- Source pointer for traceability.

Associate actions with entity types using **explicit schema references** and **route/parameter evidence**. Examples:

```text
GET    /passengers/{passengerId}                 → Passenger: get
GET    /passengers/{passengerId}/bookings        → Passenger: listBookings; Booking: related output
GET    /bookings/{bookingId}                     → Booking: get
POST   /bookings/{bookingId}/change-flight       → Booking: changeFlight
POST   /bookings/{bookingId}/seat                → Booking: selectSeat
```

The association must be generic, not based on hardcoded words such as `passenger`, `flight`, or `booking`. Prefer schema evidence; route-token matching can be a documented fallback. An operation may relate to multiple entities. If an operation cannot be confidently associated, keep it as an **unbound action** in the ontology with a diagnostic, rather than dropping it or guessing.

Do not infer action safety merely from HTTP method; preserve method and explicit API metadata for later policy evaluation. **Do not execute actions in this task.**

## 8. Serialization contract

Produce human-readable JSON with a stable schema version:

```json
{
  "schemaVersion": "1.0",
  "ontology": {
    "id": "sample-airline",
    "name": "Sample Airline API",
    "entities": [
      {
        "id": "schema:Booking",
        "name": "Booking",
        "properties": [{"name":"id","dataType":"string","required":true}],
        "actionIds": ["operation:getBooking"]
      }
    ],
    "relationships": [],
    "actions": [
      {
        "id": "operation:getBooking",
        "name": "getBooking",
        "httpMethod": "GET",
        "path": "/bookings/{bookingId}",
        "entityIds": ["schema:Booking"]
      }
    ]
  }
}
```

This JSON is **illustrative, not a complete schema**. Define the full JSON shape in code, including provenance, parameters, cardinality, diagnostics, and source information. Ensure `Serialize → Deserialize → Serialize` preserves semantic content and yields deterministic output for identical input. Reject incompatible future major versions with a useful error.

## 9. HTML ontology viewer

Build a **generic, read-only** viewer for exported ontology JSON.

Required features:

1. Load a local `ontology.json` using a file picker; validate schema version and show clear errors.
2. Draw a graph with **entity nodes** and **relationship edges**, labeled with relationship names and cardinality.
3. Clicking an entity opens a details panel with its name, description, properties, and associated actions.
4. Clicking a relationship shows source/target, evidence, property, cardinality, and source pointer.
5. Clicking an action shows HTTP method, path, operation ID, parameters, request/response schemas, and security metadata.
6. Search/filter entities by name and optionally focus the graph on one entity and its neighbors.
7. Distinguish confirmed structural relationships from heuristic/candidate relationships visually and in text.
8. Show unbound actions in a separate list.
9. Handle empty ontologies, malformed JSON, dangling references, and moderately sized graphs without crashing.
10. Render source-controlled text safely (`textContent`, not unsanitized `innerHTML`). No executable content from YAML/JSON.

A simple SVG graph or a bundled graph library is acceptable. Prefer clarity and reliable interaction over sophisticated force-layout animations. The viewer must not require a running .NET API.

## 10. Required solution and project structure

The solution **must be named `seldon.sln`** (lowercase `seldon`). Use the provided empty `seldon.sln` as the starting point, or create an equivalent solution with `dotnet new sln --name seldon --format sln` if the installed SDK supports that syntax. Do not create `OntologyBuilder.sln` or rename the solution.

Create **exactly five .NET projects** in the solution initially:

| Project | Type | Responsibility |
|---|---|---|
| `Ontology.Core` | Class library | Domain interfaces, immutable ontology contracts, models, diagnostics and provenance |
| `Ontology.Builder` | Class library | Build orchestration, OpenAPI/Swagger normalization, entity/relationship/action inference |
| `Ontology.Infrastructure` | Class library | In-memory ontology/repository, JSON serialization, OpenAPI parser adapter |
| `Ontology.Cli` | Console app | CLI entry point, argument parsing, dependency-injection composition root |
| `Ontology.Tests` | xUnit test project | Unit, contract and end-to-end tests (organize into folders rather than extra projects) |

The viewer is **not** a .NET project; it is a static HTML/CSS/JavaScript folder.

```text
seldon.sln
ontologybuilder.md
src/
  Ontology.Core/
    Ontology.Core.csproj
  Ontology.Builder/
    Ontology.Builder.csproj
  Ontology.Infrastructure/
    Ontology.Infrastructure.csproj
  Ontology.Cli/
    Ontology.Cli.csproj
viewer/
  index.html
  viewer.js
  viewer.css
samples/
  airline-reservation-openapi.yaml
  minimal-openapi.yaml
  minimal-swagger.json
tests/
  Ontology.Tests/
    Ontology.Tests.csproj
```

### 10.1 Create projects and register them in `seldon.sln`

Run these commands from the repository root. **If `seldon.sln` already exists, do not overwrite it.** If it does not exist, create it first using `dotnet new sln --name seldon --format sln` (or the appropriate installed SDK syntax to produce `.sln`, not `.slnx`).

```bash
dotnet new classlib -n Ontology.Core -o src/Ontology.Core
dotnet new classlib -n Ontology.Builder -o src/Ontology.Builder
dotnet new classlib -n Ontology.Infrastructure -o src/Ontology.Infrastructure
dotnet new console -n Ontology.Cli -o src/Ontology.Cli
dotnet new xunit -n Ontology.Tests -o tests/Ontology.Tests

dotnet sln seldon.sln add src/Ontology.Core/Ontology.Core.csproj
dotnet sln seldon.sln add src/Ontology.Builder/Ontology.Builder.csproj
dotnet sln seldon.sln add src/Ontology.Infrastructure/Ontology.Infrastructure.csproj
dotnet sln seldon.sln add src/Ontology.Cli/Ontology.Cli.csproj
dotnet sln seldon.sln add tests/Ontology.Tests/Ontology.Tests.csproj
```

### 10.2 Establish project dependencies

```bash
dotnet add src/Ontology.Builder/Ontology.Builder.csproj reference src/Ontology.Core/Ontology.Core.csproj
dotnet add src/Ontology.Infrastructure/Ontology.Infrastructure.csproj reference src/Ontology.Core/Ontology.Core.csproj
dotnet add src/Ontology.Cli/Ontology.Cli.csproj reference src/Ontology.Core/Ontology.Core.csproj
dotnet add src/Ontology.Cli/Ontology.Cli.csproj reference src/Ontology.Builder/Ontology.Builder.csproj
dotnet add src/Ontology.Cli/Ontology.Cli.csproj reference src/Ontology.Infrastructure/Ontology.Infrastructure.csproj
dotnet add tests/Ontology.Tests/Ontology.Tests.csproj reference src/Ontology.Core/Ontology.Core.csproj
dotnet add tests/Ontology.Tests/Ontology.Tests.csproj reference src/Ontology.Builder/Ontology.Builder.csproj
dotnet add tests/Ontology.Tests/Ontology.Tests.csproj reference src/Ontology.Infrastructure/Ontology.Infrastructure.csproj
```

Dependency rules:
- `Ontology.Core` references no other Seldon project or infrastructure package.
- `Ontology.Builder` references `Ontology.Core` only. It uses interfaces for document parsing and ontology persistence; concrete adapters live in Infrastructure.
- `Ontology.Infrastructure` references `Ontology.Core` only, implementing parsing, repository and serialization contracts. It must **not** reference Builder.
- `Ontology.Cli` references Core, Builder and Infrastructure and registers dependencies.
- `Ontology.Tests` may reference all production libraries; it need not reference CLI initially.
- No circular project references. The static viewer consumes serialized JSON and does not reference .NET assemblies.

### 10.3 Validate the solution skeleton

```bash
dotnet sln seldon.sln list
dotnet restore seldon.sln
dotnet build seldon.sln --no-restore
dotnet test seldon.sln --no-build
```

**Acceptance criteria:** all five projects appear in `dotnet sln ... list`; the solution builds; tests execute successfully; project references follow the dependency rules; the solution filename remains `seldon.sln`. The `Ontology.Core` contracts belong in Slice 1.1; parser, inference, serializer, CLI behavior and viewer functionality belong to later slices.

## 11. Implementation slices — do them in this exact order

**Stop after each slice, run tests, report the results, and wait for approval before starting the next slice.**

### Slice 1.1 — Solution skeleton and domain contracts
- Use `seldon.sln` and create/register the five projects exactly as described in Section 10.
- Add the project references and verify the dependency direction.
- Create projects and references.
- Define entity, property, relationship, action, ontology, builder, repository, and serializer interfaces.
- Define structured diagnostics and provenance types.
- **Tests:** contract invariants; dependency-direction tests or code review.
- **Done:** solution compiles; no OpenAPI parser or UI yet.

### Slice 1.2 — In-memory ontology implementation
- Implement concrete ontology and repository behind interfaces.
- Add ID-based lookups, duplicate detection, and referential-integrity validation.
- **Tests:** add/find entities; relationships reference valid entities; duplicate IDs rejected; collection immutability.
- **Done:** manually constructed sample ontology works in memory.

### Slice 1.3 — JSON serializer/deserializer
- Define versioned export DTOs and mapping to/from interfaces.
- Implement stable JSON output and strict validation on import.
- **Tests:** round-trip, stable ordering, invalid version, missing references.
- **Done:** manually constructed ontology exports and reimports correctly.

### Slice 1.4 — OpenAPI/Swagger file loader
- Read YAML/JSON from disk, detect spec version, parse operations/schemas, collect parser diagnostics.
- Resolve local `$ref` references; warn for unsupported external references.
- **Tests:** valid OpenAPI 3.0, 3.1 and Swagger 2.0 fixtures; malformed input; unsupported references.
- **Done:** CLI can inspect an input file and report its schema/operation counts.

### Slice 1.5 — Entity and property extraction
- Map named object schemas, fields, required flags, arrays, and basic composition into ontology entities.
- **Tests:** nested objects, enums, arrays, `$ref`, `allOf`, deterministic IDs.
- **Done:** exported JSON contains entities/properties, no relationships/actions required yet.

### Slice 1.6 — Relationship extraction
- Derive explicit reference edges and conservative heuristic candidates, with evidence and cardinality.
- **Tests:** one-to-one, one-to-many, inheritance, ambiguous foreign keys, duplicate relationships.
- **Done:** ontology graph has inspectable, provenance-backed edges.

### Slice 1.7 — Action extraction and association
- Extract operations, parameters, request/response schema references, and security metadata.
- Associate operations with entity types where evidence is sufficient; retain unbound actions.
- **Tests:** entity-bound read/write operations, multiple entities, unmatched operation, missing operationId.
- **Done:** ontology includes complete operation catalog and entity-associated actions.

### Slice 1.8 — CLI build/export
- Wire parser, builder, repository, and serializer via DI.
- Implement `ontology-builder build --input ... --output ...`.
- Write ontology and diagnostics atomically where practical; use nonzero exit codes on fatal failures.
- **Tests:** CLI smoke test, invalid input path, deterministic repeat builds.
- **Done:** one command generates a complete `ontology.json`.

### Slice 1.9 — HTML viewer foundation
- Create static HTML/JS/CSS, local file picker, JSON validation, entity list, and details panel.
- **Tests:** viewer loads a sample export, handles malformed files and empty lists (manual browser test acceptable initially).
- **Done:** user can inspect entities/properties/actions without a server.

### Slice 1.10 — Graph rendering and interactions
- Add entity nodes, relationship edges, labels, click-through details, search/focus, candidate styling, and unbound action list.
- **Tests:** graph for sample airline spec, click node/edge/action, filter, no script injection from descriptions.
- **Done:** user can visually navigate ontology and inspect action metadata.

### Slice 1.11 — End-to-end demonstration and documentation
- Run the sample airline OpenAPI spec through CLI.
- Export the JSON and open the viewer using its file picker.
- Document commands, limitations, and example screenshots (if available).
- **Tests:** full pipeline; serializer round-trip; deterministic output; expected entities/relationships/actions asserted from fixture.
- **Done:** a fresh developer can reproduce the demo from README alone.

## 12. Acceptance criteria for Task 1

Task 1 is complete only when all are true:

- [ ] An OpenAPI/Swagger YAML or JSON file is accepted as input.
- [ ] Parser handles OpenAPI 3.0.x, 3.1.x, and Swagger 2.0, or explicitly reports a version-specific limitation with tests (do not claim support without tests).
- [ ] Domain and repository are defined through interfaces and an in-memory implementation.
- [ ] Entity types and properties are extracted without domain-specific rules.
- [ ] Relationships have stable IDs, cardinality, evidence, and source pointers.
- [ ] Operations are extracted and associated with entities where supported; unbound actions remain visible.
- [ ] JSON is versioned, portable, deterministic, and round-trip readable.
- [ ] Viewer loads exported JSON through a file picker when opened locally.
- [ ] Viewer displays a graph and allows inspecting entities, relationships, and actions.
- [ ] Malformed inputs and unsupported features produce understandable diagnostics.
- [ ] No API calls, authentication, LLM services, databases, or cloud services are required.
- [ ] Automated tests pass; sample airline end-to-end demo is documented.

## 13. Explicit non-goals

- Querying live enterprise data or invoking discovered HTTP operations.
- Semantic matching using an LLM.
- Natural-language interaction, dynamic CX forms, or agent orchestration.
- Ontology federation/merging across APIs.
- Production access control or execution authorization.
- Persistent graph database, vector index, message queue, or distributed deployment.
- A generic Swagger editor or API testing client.

## 14. Codex operating instructions

1. Read this entire file before changing code.
2. Work on **Slice 1.1 only** unless the user explicitly approves the next slice.
3. Before implementation, briefly list files to create and contracts to define.
4. Keep public contracts minimal and extension-friendly; avoid speculative abstractions.
5. Add tests alongside each slice; execute them and report results honestly.
6. Do not introduce external services or credentials.
7. Do not hardcode airline entities, relationship names, or endpoint paths in production code.
8. At the end of each slice, report: changed files, test command/results, known limitations, and the next slice.
9. Ask for approval before proceeding.

### Initial Codex prompt

> Read `ontologybuilder.md` completely. Implement **Slice 1.1 only**: create/register the five projects in `seldon.sln`, establish the required project references, define domain contracts, diagnostic/provenance types, and contract tests. Follow interface-first design and dependency direction. Do not implement the parser, inference engine, serializer, CLI commands, or HTML viewer yet. Run the tests and report files changed, results, and any design decisions. Stop and wait for my approval.
