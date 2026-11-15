# Seldon decision tracker

This log records key product, architecture, implementation, and process decisions across iterations. The build specification remains [seldon-build.md](seldon-build.md); implementation status lives in [progress-tracking.md](progress-tracking.md).

Last updated: 2026-11-14

## Decision index

| ID | Date | Decision | Status | Basis |
| --- | --- | --- | --- | --- |
| D-001 | 2026-11-13 | Build incrementally and start with Increment 1 only | Accepted | Specification sections 9 and 14 |
| D-002 | 2026-11-13 | Keep the core domain independent; use synthetic airline data as proof | Accepted | Specification sections 1–3 and 11 |
| D-003 | 2026-11-13 | Use C#/.NET with inward dependencies and replaceable adapters | Accepted | Specification sections 2, 5, and 6 |
| D-004 | 2026-11-13 | Validate model proposals and verify consequential action outcomes | Accepted | Specification sections 2, 10, and 12 |
| D-005 | 2026-11-13 | Maintain persistent progress and decision logs | Accepted | User instruction, 2026-11-13 |
| D-006 | 2026-11-14 | Preserve known nonstandard Swagger schema forms with explicit compatibility warnings | Accepted | Slack import failure and implementation judgment |

## Decisions

### D-006 — Narrow compatibility for legacy Swagger schemas

- **Date/status:** 2026-11-14, Accepted.
- **Context:** The Slack Swagger 2.0 sample includes recognized scalar type-name lists and object schema lists under `items`, which Microsoft's parser rejects. Our extraction pipeline uses original source JSON, independently of the typed parser model.
- **Decision:** Downgrade only the exact corresponding parser shape errors at cataloged Swagger schema positions to explicit compatibility warnings. Preserve original definitions, catalog list branches for provenance, and warn that ambiguous item-list extraction is partial. Keep all other parser errors fatal.
- **Reason:** Allow useful inspection/export of real-world legacy files without rewriting user input, dropping source evidence, or pretending ambiguous schemas are fully understood.
- **Alternatives:** Rewriting `items` lists into `oneOf` guesses intent; globally ignoring parser errors hides malformed documents. Both are rejected.
- **Consequences:** Nullable scalar type lists can be extracted; item lists are not treated as unions or object entities by assumption. A successful export can contain incomplete metadata and warnings. This does not expand Swagger 2.0's conformance rules.

### D-001 — Incremental delivery and scope approval

- **Context:** The specification defines 14 ordered increments and requires demonstrable, tested slices.
- **Decision:** Implement Increment 1 first. Complete its applicable exit gates, report results, and wait for explicit approval before starting the next increment.
- **Reason:** Establish a trustworthy foundation and keep each iteration reviewable.
- **Consequences:** Future features may inform design, but are not implemented early. Tracking setup does not count as completing Increment 1.

### D-002 — Domain-independent core and synthetic proof domain

- **Context:** Airline reservations prove a general-purpose semantic assistant.
- **Decision:** Keep airline business logic and fixtures outside the generic core. Model one enterprise ontology; defer federation. Use synthetic data only.
- **Reason:** The platform must support another source/domain without rewriting its core services.
- **Consequences:** Airline-specific screens and workflows cannot be hardcoded into generic orchestration or rendering. Ontology definitions remain separate from live source records.

### D-003 — Implementation architecture and deferred infrastructure

- **Context:** The specification prefers C#/.NET, a modular monolith, contract-first development, and local-first adapters.
- **Decision:** Follow that architecture, use native .NET DI where needed, and introduce interfaces just before their first implementation. Select the target framework after inspecting the available supported SDK. Start with in-memory/fake adapters as their increments begin.
- **Reason:** Preserve substitutability without speculative infrastructure or abstractions.
- **Consequences:** No cloud, model SDK, graph database, vector store, or orchestration framework is selected now. Vendor DTOs must not enter public domain/contracts. The exact project split and target framework remain to be decided during implementation.

### D-004 — Evidence, deterministic validation, and verified execution

- **Context:** Inferred mappings and generated plans may be incorrect; source mutations require reliable safeguards.
- **Decision:** Treat model output as untrusted structured proposals. Validate mappings and plans in deterministic code, retain provenance, and require authorization, confirmation, idempotency, audit, and authoritative outcome verification as execution capabilities are introduced.
- **Reason:** Neither model confidence nor an attempted operation establishes correctness or success.
- **Consequences:** Tests start with deterministic fixtures/fake responses. Real-model evaluations are separate. Missing evidence and failed verification must be reported accurately.

### D-005 — Persistent iteration records

- **Context:** The product will be built over many iterations, and the user requested durable tracking files.
- **Decision:** Maintain `progress-tracking.md` for specification-aligned completion evidence and `decisiontracker.md` for key choices and their reasons. Update both when relevant work or decisions occur.
- **Reason:** Preserve continuity and distinguish completed work from plans.
- **Consequences:** Progress entries include verification evidence and remaining work. Decision IDs are stable; superseded decisions remain in the log and link to their replacement.

## Logging protocol

Add an entry when a choice materially affects scope, contracts, architecture, dependencies, security, behavior, or acceptance criteria. Routine implementation details do not require entries.

Use the next sequential ID and record:

- Date and title.
- Status: Proposed, Accepted, Superseded, or Rejected.
- Context and source of authority (specification, user instruction, or implementation judgment).
- Decision and reason, including meaningful alternatives when relevant.
- Consequences, limitations, and affected increment/files.
- Replacement decision ID if superseded.

Decisions inherited from the specification above are documented baselines, not claims that the corresponding functionality has been implemented.
