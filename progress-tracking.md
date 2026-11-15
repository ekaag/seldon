# Seldon progress tracking

Source of scope and acceptance criteria: [seldon-build.md](seldon-build.md).

Last updated: 2026-11-14

## Current state

The repository now contains the Ontology.Core, Builder, Infrastructure, and CLI projects, tests, and an exported graph viewer. The original increment table below has not yet been reconciled with this later implementation; it is a planning baseline, not current completion evidence. The latest verified work is recorded in the iteration history.

**Next implementation scope:** Increment 1 only, as required by specification section 14. Later increments require explicit approval before work starts.

## Increment status

Statuses: Not started, In progress, Blocked, Complete. An increment is Complete only when its applicable acceptance criteria and section 12 quality gates have been satisfied, with evidence recorded below.

| Milestone | Increment | Scope (specification section 9) | Status | Evidence |
| --- | --- | --- | --- | --- |
| 1: Discover a single ontology | 1 | Domain model and contracts | Not started | No implementation yet |
| 1 | 2 | DI and host | Not started | — |
| 1 | 3 | Mock airline API and connector | Not started | — |
| 1 | 4 | Metadata discovery | Not started | — |
| 1 | 5 | LLM ontology inference | Not started | — |
| 1 | 6 | Validation and ontology publication | Not started | — |
| 2: Ask questions and hydrate live data | 7 | Read-only source query/hydration | Not started | — |
| 2 | 8 | Natural-language query planning and grounded answers | Not started | — |
| 3: Dynamic UX/CX and incremental inputs | 9 | Declarative UI schema and generic renderer | Not started | — |
| 3 | 10 | Experience orchestrator and session state | Not started | — |
| 4: Dynamic task completion | 11 | Capability-driven workflow planner | Not started | — |
| 4 | 12 | Authorized executor and outcome verification | Not started | — |
| 5: Unstructured evidence | 13 | Document parsing and indexing | Not started | — |
| 5 | 14 | Evidence-backed extraction and resolution | Not started | — |

## Next increment: 1 — Domain model and contracts

Reference: specification sections 7, 9 (Increment 1), 12, and 14 (First task).

- [ ] Inspect the available .NET SDK and select a supported target; record the decision.
- [ ] Define minimal typed domain definitions and versioned contracts for sources, entities, properties, relationships, capabilities, evidence, and ontology snapshots.
- [ ] Provide a deterministic synthetic Passenger/Customer → Booking → Flight sample ontology; resolve the sample naming inconsistency in the specification and log the choice.
- [ ] Validate object and relationship integrity, including invalid IDs and dangling references.
- [ ] Test evidence references and rejection of invalid graphs.
- [ ] Verify JSON serialization/deserialization round trips.
- [ ] Add applicable unit, contract, negative, and fixture-based acceptance tests.
- [ ] Document and run a single-command deterministic demo with expected output.
- [ ] Run build and tests; record commands and results.
- [ ] Update README, this tracker, and the decision log; record any applicable quality gate exceptions with reasons.
- [ ] Report completion and stop before Increment 2 pending explicit approval.

## Iteration history

### 2026-11-14 — Square import and explicit partial mode

- Investigated the supplied external Square input and saved errors. Confirmed misplaced `info.externalDocs`, null OAuth security scopes, and absent `AppFeeAllocation`/`CurrencyExchange` schema definitions.
- User selected explicit `--allow-partial` while retaining strict validation by default (D-007).
- Added `ApiDocumentLoadOptions`, CLI flag parsing and composition, targeted metadata compatibility, unresolved-schema warnings, and omission of malformed-security operations. Source files remain untouched; no replacement schema or authentication assumptions are created.
- Added a visible partial-import notice to generated viewers and documented semantics/limits in README and `docs/api-inspection.md`.
- Added `samples/partial-openapi.json` and regression coverage for strict rejection, partial inspection/build, input preservation, unknown property types, no invented relationships, skipped malformed-security actions, viewer notice, invalid flags, and structural failures remaining fatal.
- `dotnet test tests/Ontology.Tests --no-restore`: 134 passed, 0 failed. Build is included in the test command.
- Final actual Square partial export succeeded (exit 0) at `out/square-partial`: 1,276 entities, 2,197 relationships, 331 actions; 887 warnings, no errors. Verified the viewer contains the partial-import notice. The revoke operation is omitted due to invalid security.
- Actual Square `inspect` without the flag still returns exit 1 with seven fatal diagnostics, as intended. `git diff --check` found no whitespace errors. The externally located input was read only; the generated output is inside the workspace.
- Follow-up: `out/myapi/diagnostics.json` still contained the earlier strict-mode errors. Rebuilt the Square input into `out/myapi` with `--allow-partial --no-open`: exit 0, no errors, 887 warnings, partial-mode marker present; exported counts unchanged (1,276 entities, 2,197 relationships, 331 actions).

### 2026-11-14 — Slack Swagger compatibility fix

- Investigated `out/error.txt`: Swagger 2.0 parser rejects type arrays and schema arrays under `items` in the supplied Slack specification.
- Added narrow warning-based compatibility for those recognized forms at cataloged schema locations. Preserved original schema JSON, provenance, and the existing user edit to the sample. Unsupported item lists remain explicitly partial; no union meaning is invented. Decision: D-006.
- Added loader regression cases covering source preservation, nested branch cataloging, nullable type extraction, ambiguous item-list warnings, and malformed forms remaining fatal.
- Final `dotnet test tests/Ontology.Tests --no-restore`: 128 passed, 0 failed, including nullable extraction and ambiguous item-list assertions. `git diff --check` found no whitespace errors.
- `dotnet run --no-build --project src/Ontology.Cli -- build --input samples/slack_web_openapi_v2.json --output out/slack-verified --no-open`: exit 0; 193 entities, 47 relationships, 174 actions. Output contains warnings about unsupported/partial semantics; successful export does not imply full Swagger conformance or exhaustive ontology extraction.
- Generated separate verification output rather than replacing `out/myapi`. Diagnostics contain 257 warnings and no errors. Updated `docs/api-inspection.md` with behavior and limitations.

### 2026-11-13 — Planning baseline and tracking setup

- Reviewed the specification and explained the intended product, architecture boundaries, and Increment 1 scope.
- Created this tracker and [decisiontracker.md](decisiontracker.md).
- Repository inspection found only `seldon-build.md` before these tracking files were added.
- Validation: checked the trackers against the specification's 14 increments and execution protocol. No build or tests run; this iteration changes documentation only.
- Outstanding: all implementation and acceptance gates remain open. No milestone demo is complete.

## Update protocol

For each implementation iteration:

1. Update the date, current state, increment status, and relevant checklist items.
2. Append a dated history entry with concrete changes, file references, commands run, results, remaining issues, and next scope.
3. Link relevant decision IDs from `decisiontracker.md`.
4. Record blockers and unverified behavior explicitly. Never mark planned or untested work as complete.
5. Keep earlier history intact; record scope changes with a specification reference and decision entry.
