# General-Purpose Semantic Assistant — Incremental Build Specification

## 1. Mission
Build a vertical-agnostic assistant that discovers **one enterprise ontology** from connected sources, answers natural-language questions by hydrating source data, generates dynamic UX/CX from a declarative schema, gathers missing inputs incrementally, and safely executes and verifies authorized actions. **Ontology federation is out of scope.**

Primary proof domain: **airline reservations**. The core must contain no airline-specific business logic or hardcoded screens. Use a mock airline API and synthetic fixtures initially.

## 2. Non-negotiable principles
- **Contract-first:** define small, versioned interfaces and typed DTOs before implementation.
- **Dependency inversion and DI:** domain and application depend only on abstractions; adapters depend inward. Host is the composition root.
- **Cloud-agnostic:** no dependency on AWS, Azure, or GCP in domain/application. Local-first; deployment decisions deferred.
- **Provider-agnostic:** LLM, graph storage, relational storage, search, document parsing, connectors, execution engine, and UI transport behind interfaces.
- **Incremental, demonstrable slices:** finish one increment with tests and a runnable demo before starting the next.
- **Deterministic tests:** fake model responses, seeded synthetic data, stable fixtures. Real-model evaluations are separate.
- **Evidence before assertions:** semantic mappings and answers include source provenance, confidence, and validation status.
- **Safe execution:** read-only by default; explicit permissions, policy checks, validation, confirmation for consequential writes, idempotency, audit, and outcome verification.
- **LLM proposes; deterministic code validates:** model output is untrusted structured data, never executable source code or unrestricted queries.
- **No premature infrastructure:** begin with in-memory stores, local mock services, and a simple DI composition root; replace later.

## 3. Product boundaries
### In scope
- Connector to a mock REST API and discovery of OpenAPI-like schemas and capabilities.
- One metadata-first ontology: entity types, properties, relationship types, source mappings, capability definitions, constraints, evidence.
- Query planning, bounded source data hydration, grounded natural-language answers.
- Declarative, generated UI: text, cards, lists, tables, forms, selection, confirmations, progress, errors.
- Session state and multi-turn collection of missing input.
- Dynamic composition of available actions into plans and execution with verification.
- Optional later document ingestion: parsing, indexed evidence, extracted candidate facts and entity resolution.

### Explicitly out of scope for v1
- Federation of multiple ontologies, cross-company knowledge graphs, auto-discovered cross-graph links.
- Production airline integration or access to real passenger data.
- Arbitrary LLM-generated JavaScript/HTML, arbitrary SQL execution, unrestricted tool use.
- Autonomous financial charges, refunds, cancellations, or irreversible actions without explicit authorization and confirmation.
- Kubernetes, microservices, distributed queues, cloud-specific infrastructure, and complex plugin marketplaces.

## 4. Reference architecture
```text
User / Generic Renderer
         |
Experience Orchestrator <--> Session Store
         |               \
         |                UI Schema Generator
         v
Intent / Query Planner ------> Ontology Repository
         |                            ^
         v                            |
Data Hydration / Retrieval      Ontology Builder + Validator
         |                            ^
         v                            |
Source Query Adapter <------ Metadata Discovery
         ^                            ^
         |                            |
    Connector(s) --------------- Source API / Documents

Experience Orchestrator --> Workflow Planner --> Policy Gate
                                            --> Action Executor
                                            --> Outcome Verifier
```
Cross-cutting: identity, authorization, provenance, schema/version checks, telemetry, audit, cancellation, timeouts, and error handling.

## 5. Technology approach
- Preferred implementation: **C# / .NET**, current supported SDK available in the environment.
- Start as a modular monolith: domain library, contracts library, application library, adapters library, minimal host, and tests.
- Use native .NET DI. No provider SDK types in public contracts.
- First implementations: in-memory ontology/session repositories; fake inference model; mock HTTP airline API; deterministic local executor; JSON UI schema.
- Future adapters may use OpenAI/Anthropic/Gemini/local models, PostgreSQL/graph DBs, vector search, Temporal, AWS/Azure/GCP. **Do not select or integrate these until justified by an acceptance test.**

## 6. Proposed repository layout
```text
src/
  Assistant.Domain/              # entities, ontology, evidence, policies, plans
  Assistant.Contracts/           # interfaces and versioned DTOs
  Assistant.Application/         # discovery, inference, querying, CX, workflows
  Assistant.Adapters.InMemory/   # stores, fake model, fake execution
  Assistant.Adapters.Http/       # OpenAPI/HTTP discovery and invocation
  Assistant.Host/                # DI, local API, configuration
  Assistant.Web/                 # generic schema-driven renderer (later)
tests/
  Assistant.UnitTests/
  Assistant.ContractTests/
  Assistant.IntegrationTests/
  Assistant.EvaluationTests/     # optional real-model tests, not CI-gating initially
samples/
  MockAirlineApi/
  Fixtures/
  Documents/
docs/
  architecture.md
  decisions/
  milestones/
```

## 7. Core conceptual model
Keep schema definitions separate from live records:
- **SourceDescriptor:** id, kind, location reference, tenant/security scope, discovery metadata.
- **EntityType:** stable id, name, description, properties, version.
- **PropertyDefinition:** type, nullability, constraints, source field mapping, sensitivity.
- **RelationshipType:** from/to entity types, cardinality, join/mapping expression (typed, validated), evidence.
- **CapabilityDescriptor:** stable id, target entity type, read/write classification, typed inputs/outputs, preconditions, effects, permissions, side effects, source operation mapping.
- **EvidenceReference:** source id, document location/record field, version, extraction timestamp, validation status.
- **OntologySnapshot:** versioned collection of definitions and validated edges; candidate edges separate from active edges.
- **QueryPlan:** typed retrieval operations, dependencies, limits, authorization context, expected output shape.
- **ExperienceView:** versioned allowlisted UI component tree, typed input bindings, event/action identifiers; never arbitrary code.
- **ExperienceSession:** intent, ontology version, hydrated references, collected inputs, pending questions, workflow status.
- **WorkflowPlan:** typed DAG of known capabilities, bindings, preconditions, confirmation requirements, compensations where supported.
- **ExecutionRecord:** correlation id, authorization decision, inputs, outcomes, idempotency key, audit events, verification evidence.

All objects carry tenant/security scope where applicable. Never place credentials or secrets in ontology nodes.

## 8. Interfaces to introduce just-in-time
Define each contract before its first implementation; avoid inventing an enormous speculative interface set.

```csharp
public interface ISourceConnector
{
    Task<SourceDescriptor> DescribeAsync(CancellationToken ct);
    Task<IReadOnlyList<ResourceDescriptor>> DiscoverResourcesAsync(CancellationToken ct);
    Task<IReadOnlyList<CapabilityDescriptor>> DiscoverCapabilitiesAsync(CancellationToken ct);
}

public interface IOntologyRepository
{
    Task<OntologySnapshot?> GetAsync(string ontologyId, CancellationToken ct);
    Task SaveAsync(OntologySnapshot snapshot, CancellationToken ct);
}

public interface IInferenceModel
{
    Task<TOutput> InferAsync<TOutput>(InferenceRequest request, CancellationToken ct);
}

public interface IExperienceOrchestrator
{
    Task<ExperienceTurn> ProcessAsync(ExperienceRequest request, CancellationToken ct);
}
```

Introduce `ISourceQueryExecutor`, `IQueryPlanner`, `IUiSchemaGenerator`, `ISessionRepository`, `IWorkflowPlanner`, `IPolicyEvaluator`, `IActionExecutor`, `IOutcomeVerifier`, and `IContentExtractor` only when their increments start. Do not assume these sketches are frozen API contracts; evolve with tests and explicit versioning.

## 9. Ordered implementation backlog
**Rule:** implement only the next increment, pass its exit gate, then proceed. Each increment must have an interface, a fake/in-memory implementation, tests, and a runnable demo.

### Milestone 1 — Discover a single ontology
**1. Domain model and contracts**
- Implement minimal typed definitions for sources, entities, properties, relationships, capabilities, evidence, ontology snapshots.
- Verify: serialize/deserialize a synthetic Customer -> Booking -> Flight ontology; reject invalid IDs and dangling references.

**2. DI and host**
- Add composition root and in-memory repository, plus second test adapter.
- Verify: swap repository implementation without changing application services.

**3. Mock airline API and connector**
- Create synthetic endpoints: customers/passengers, bookings, flights, available alternatives, seats; read and mock write endpoints.
- Publish a machine-readable API schema. Connector discovers resources and operations.
- Verify: enumerate resources and input/output schemas with no airline logic in connector interface.

**4. Metadata discovery**
- Extract fields, types, identifiers, candidate foreign keys, operation signatures, constraints.
- Verify: deterministic catalog generated from the mock API specification.

**5. LLM ontology inference**
- Feed discovered metadata to `IInferenceModel`; require schema-constrained candidate entity/relationship/capability mappings.
- Start with fake model fixture; real model only in optional integration demo.
- Verify: inferred Passenger -> Booking -> Flight graph matches expected structure.

**6. Validation and ontology publication**
- Validate candidates against source schemas and permitted sample reads; reject unsupported mappings.
- Store immutable/versioned validated snapshot; retain candidate/evidence status.
- Verify: intentionally wrong relationship is not activated.

**Milestone 1 demo:** connect mock API, run discovery, inspect generated ontology with provenance.

### Milestone 2 — Ask questions and hydrate live data
**7. Read-only source query/hydration**
- Build typed query executor using ontology mappings, bounded reads, timeouts, authorization scope.
- Verify: retrieve a booking and related flight without copying all airline data into graph.

**8. Natural-language query planning and grounded answers**
- Interpret intent, produce typed query plan, validate against ontology and access policy, execute and summarize with evidence.
- Verify: "Show my upcoming flights" and "What is the status of booking B-1001?" return seeded correct results; unsupported answers explicitly say insufficient evidence.

**Milestone 2 demo:** CLI or simple API takes airline questions and returns sourced live answers.

### Milestone 3 — Dynamic UX/CX and incremental inputs
**9. Declarative UI schema and generic renderer**
- Define allowlisted UI primitives: text, cards, tables, forms, radio/select, date input, review, confirmation, progress, error.
- Implement generic renderer driven only by schema; validate schema and bind events by stable IDs.
- Verify: same renderer shows booking cards and an insurance-like sample form without domain-specific UI code.

**10. Experience orchestrator and session state**
- Multi-turn loop: understand intent -> query/hydrate -> choose view -> ask for missing input -> update session -> render next view.
- Persist session and pending input state; handle back, correction, expiry, validation errors.
- Verify: "Change my flight" -> choose booking -> choose alternative -> collect preference -> review; no real mutation yet.

**Milestone 3 demo:** interactive airline assistant dynamically generates forms/cards and collects incremental inputs; mock-only confirmation.

### Milestone 4 — Dynamic task completion
**11. Capability-driven workflow planner**
- Generate typed DAGs using only discovered, verified capabilities; bind required inputs, validate preconditions and effects.
- Verify: plan booking modification, fare-rule check, explicit confirmation, mutation, read-back verification; reject missing capability.

**12. Authorized executor and outcome verification**
- Policy evaluation, explicit confirmation for consequential writes, idempotency, state transitions, audit, retry policy, read-back verification, failure handling.
- Verify: mock flight change updates booking exactly once; duplicate request does not duplicate action; failed change is reported accurately.

**Milestone 4 demo:** complete a mock airline flight change through generated UX, with safe execution and verified result.

### Milestone 5 — Unstructured evidence (after core loop)
**13. Document parsing and indexing**
- Parse synthetic policy PDFs/text, preserve offsets/page/source, hybrid search adapter.
- Verify: retrieve exact baggage/fare-rule passages with provenance.

**14. Evidence-backed extraction and resolution**
- Extract candidate facts/relationships, validate against source, resolve contradictions and identities, keep assertion store separate from schema graph.
- Verify: answer a baggage question using policy text plus booking API, citing evidence.

**Milestone 5 demo:** a single dynamic airline CX uses both API records and unstructured fare/policy documents.

### Later (explicitly not now)
- Cross-ontology federation, continuous discovery across departments, multi-agent distributed execution, production cloud rollout.

## 10. End-to-end behavioral contract
```text
User request
  -> identify intent and authorization context
  -> traverse single ontology for entity types, relationships, capabilities
  -> generate and validate typed query plan
  -> hydrate only required live records and evidence
  -> generate declarative UI view
  -> render with generic component registry
  -> collect incremental user inputs and update session
  -> repeat until enough validated inputs exist
  -> generate capability-based workflow plan
  -> enforce policies, preview effects, request confirmation
  -> execute approved steps with idempotency and audit
  -> verify authoritative postcondition
  -> render success, partial completion, or actionable failure
```
No step may claim success until the authoritative source confirms the expected state.

## 11. Mock airline domain and seed data
- Entity types: Passenger, Booking, Flight, Seat, Fare, Policy.
- Relationships: Passenger owns Booking; Booking contains Flight; Flight offers Seat; Booking governed by Fare/Policy.
- Operations: find passenger, list bookings, get booking, list alternative flights, get fare rules, change booking, get updated booking.
- Seed at least two passengers, three bookings, several flights, one eligible alternative, one ineligible alternative, and a fare-change scenario.
- Add failure fixtures: nonexistent booking, unauthorized passenger, expired offer, price changed, upstream timeout, duplicate modification request.
- Synthetic data only. Never imply demo schedules/prices represent real airline availability.

## 12. Test strategy and quality gates
Every increment requires:
1. Build/lint pass.
2. Unit tests for normal, edge, and failure paths.
3. Contract tests for interface behavior and adapter substitutability.
4. Security/authorization negative tests where applicable.
5. Deterministic fixture-based acceptance test.
6. A one-command demo with clear expected output.
7. Updated README and short architecture decision record for any substantive tradeoff.

LLM-specific evaluations: measure schema validity, ontology relationship precision, query correctness, hallucinated capability rate, and robustness to ambiguous requests. Do not use model self-reported confidence as proof of correctness.

## 13. Definition of done for the complete initial product
- New mock source can be connected via an adapter and inspected without modifying core application code.
- System infers and validates a single ontology with provenance.
- Natural-language airline questions produce grounded answers using live synthetic data.
- A generic renderer displays generated booking/search/review/confirmation experiences.
- Session orchestrator gathers missing inputs over multiple turns and allows correction.
- Workflow planner uses only available verified capabilities.
- Authorized mock booking modification executes once and is independently verified.
- Negative tests cover unauthorized access, unsupported operations, malformed model output, missing fields, stale offers, and downstream failures.
- No AWS/Azure/GCP commitment, no hardcoded airline UI, no ontology federation.

## 14. Instructions for Codex (execution protocol)
You are implementing this specification incrementally in C#/.NET.

**Start with Increment 1 only. Do not implement future increments until explicitly requested.**

For each increment:
1. Inspect existing repository and summarize the current state.
2. State scope, proposed contracts, test cases, and expected demo in a brief plan.
3. Implement the smallest working slice with DI and replaceable adapters.
4. Run build and tests; fix failures.
5. Demonstrate the feature using deterministic sample data.
6. Report changed files, commands run, results, risks, and next increment.
7. Stop and wait for approval before proceeding.

Do not install a graph database, vector store, LLM SDK, cloud SDK, or orchestration framework merely for convenience. Do not put vendor-specific DTOs into domain/contracts. Do not hardcode airline workflows or screen layouts into the generic platform. Keep scope intentionally small.

### First task
Implement **Increment 1: Domain model and contracts** only. Include a minimal sample ontology and unit tests for object/relationship integrity, serialization, evidence references, and invalid graph rejection. Make it runnable with a single documented command.
