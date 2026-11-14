# Seldon progress tracking

Source of scope and acceptance criteria: [seldon-build.md](seldon-build.md).

Last updated: 2026-11-13

## Current state

The build specification has been reviewed and the product understanding summarized. Progress and decision tracking are established. No application code, solution, tests, or runnable demo has been implemented yet.

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
