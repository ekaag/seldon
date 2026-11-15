# Airline demonstration and verification

## Reproduction

Follow the commands in the repository README to build and test, then run:

```powershell
dotnet run --project src/Ontology.Cli -- build --input samples/airline-reservation-openapi.yaml --output out/airline
```

Output:

```text
out/airline/
  ontology.json
  diagnostics.json
  index.html
  viewer.js
  viewer.css
```

Open `index.html`, choose **Load ontology.json**, and select the JSON in that folder.
No backend or HTTP server is required. Neither callbacks nor API operations execute.

## Expected ontology

The synthetic sample describes schema types only:

| Entity | Stable ID |
| --- | --- |
| Booking | `schema:/components/schemas/Booking` |
| Flight | `schema:/components/schemas/Flight` |
| Passenger | `schema:/components/schemas/Passenger` |

| Source → target | Property | Cardinality | Evidence |
| --- | --- | --- | --- |
| Booking → Flight | flight | one | ExplicitSchemaRef |
| Booking → Passenger | passenger | one | ExplicitSchemaRef |
| Booking → Passenger | passengers | many | ExplicitSchemaRef |
| Booking → Flight | flightId | unknown | Heuristic; unverified candidate |

| Operation ID | Method/route | Associated entities |
| --- | --- | --- |
| createBooking | POST /bookings | Booking, by schema evidence |
| listPassengerBookings | GET /passengers/{passengerId}/bookings | Booking by response schema; Passenger by route/parameter heuristics |
| getHealth | GET /health | None; retained as an unbound action |

Expected diagnostics are four warnings: one foreign-key relationship candidate, two
route/parameter association evidence warnings for Passenger, and one unbound-action
warning for health. Multiple evidence items for one entity do not duplicate its binding.
Warnings do not fail the build. There should be no errors.

## Verification performed

| Check | Result / evidence |
| --- | --- |
| `dotnet build seldon.sln --no-restore` | Passed; zero warnings/errors |
| `dotnet test seldon.sln --no-build` | 124 passed, zero failed |
| `node --test tests/viewer/viewer.test.cjs` | 26 passed, zero failed |
| Real CLI → loader → extractors → ontology → JSON | Passed for all supported spec families and the airline fixture |
| Airline expectations | Exact 3 entity / 4 relationship / 3 action counts, identities, evidence, cardinality and action associations asserted |
| Serialize → deserialize → serialize | Exact byte equality asserted for the full airline ontology |
| Repeat builds | Exact ontology and diagnostics byte equality asserted |
| Portable output assets | Exported HTML/JS/CSS compared byte-for-byte with repository assets |
| Viewer graph and details | DOM-double tests against the checked-in actual airline export; nodes/edges, focus/filter, candidate labels and unbound action metadata verified |
| Malformed input / unsupported versions / dangling IDs | Rejection and diagnostics tested |
| Native file picker and browser layout | **Not verified**; browser access host unavailable |
| Browser screenshots | **Unavailable**; no screenshots fabricated |

The Node tests use a DOM double, not a browser engine. They verify event handlers and
generated content but cannot prove actual SVG layout, CSS behavior, file URL CSP
behavior, keyboard focus movement, or native file selection. The browser tool was
retried for this slice and reported that its access host is unavailable. The earlier
native fallback was stopped by Computer Use's URL-confidence policy. No workaround
was used to bypass that restriction.

Implementation and automated verification for Slices 1.1–1.11 are delivered. Treat
the full Task 1 acceptance as pending the manual browser checks above. Use the
README walkthrough to perform them; screenshots can be added once browser access
is available.

## Material limitations

- Local/internal JSON Pointer references only; external resources are not fetched.
  YAML aliases are rejected. Input size/depth and inference budgets are bounded.
- OpenAPI 3.0/3.1 and Swagger 2.0 are tested in YAML and JSON. Microsoft's parser can
  reject some Swagger definition names under normalized component naming rules.
- Basic `allOf` discovery is supported; conflicting fields become unknown with
  diagnostics. `oneOf`/`anyOf` semantics and full JSON Schema constraints are not
  exhaustively modeled. See the extraction docs for precise behavior.
- Foreign-key and route associations are conservative, unverified metadata.
  The system neither authenticates nor classifies/executes API actions.
- Security requirements/scopes are retained; full security scheme definitions and
  all parameter serialization styles are not exported yet.
- SVG graph display is capped at 200 nodes/500 visible edges. Full catalogs remain
  available in lists and JSON; use search/focus for dense graphs. Edge/label crossings
  are possible in the deterministic grid layout.
- The output bundle uses per-file atomic replacement, not a transactional directory
  swap. Failed builds retain a previous ontology. Check exit codes and diagnostics.
- Exact byte determinism assumes the same input path as well as content; provenance
  deliberately preserves that path. Ontology IDs use the source filename stem.

No services, credentials, LLMs, databases, or cloud deployment are needed.
