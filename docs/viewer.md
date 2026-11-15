# Local ontology viewer (Slice 1.9)

1. Export your API specification using the CLI:

   ```powershell
   dotnet run --project src/Ontology.Cli -- build --input "samples/actions-openapi.yaml" --output "out/actions"
   ```

2. Open `out/actions/index.html` in a modern browser (double-click the file in Explorer).
3. The graph loads automatically from the bundled export. To replace it, choose
   **Load ontology.json**, then select another exported JSON file.
4. Search or select an entity in the list or graph. Inspect properties, nested values, enum values,
   provenance, and associated actions. Expand an action for parameters,
   request/response schemas, security declarations, and association evidence.

The viewer assets and generated `ontology-data.js` must stay together for automatic
loading. The data script stores a JSON string encoded by .NET's JSON serializer;
the viewer parses and validates it rather than executing source text from the API.
It is a snapshot of `ontology.json` at export time. Rebuild or use the picker after
editing JSON manually. You can also use `viewer/index.html`
from the repository to inspect any export. No backend, CDN, internet connection,
package installation, or static server is required. The picker reads the selected
file locally; it does not upload it. The viewer makes no network requests and uses
classic scripts so file URLs do not require module loading or `fetch` permissions.
The CLI bundles the viewer assets and generated data script alongside both JSON files.

Only Seldon JSON version `1.0` is accepted. Malformed structures, duplicate catalog
IDs, dangling entity/action/relationship references, and oversized input show clear
errors while retaining a previously loaded ontology. Schema IDs may identify
primitive schemas and therefore are not required to match entity IDs. Empty
ontologies and empty search results have dedicated states. Text is rendered with
`textContent` and element APIs; source content never becomes executable markup.

Limits: 16 MiB input, property nesting depth 32, 100,000 property nodes. The viewer
validates the fields it consumes; it is not a complete JSON Schema validator and
does not reject unknown additive metadata. Use the .NET serializer for stricter import.

## Graph exploration (Slice 1.10)

The SVG graph shows entities as labeled nodes and relationships as directed curves,
with relationship names and cardinality. Click a node for properties and actions;
click an edge for source/target, property, evidence, cardinality, and source pointer.
Source and target buttons navigate back to the corresponding entities. Entity details
also list their incoming/outgoing relationships as accessible buttons.

Solid green edges mean explicit schema evidence. Dashed amber edges and text labels
mean unverified/candidate evidence. Structural-inference and unknown evidence kinds
are conservatively shown as candidates too. Composition markers retain their source
meaning; they are not interpreted as business relationships.

Search filters both the entity list and graph. **Focus selected + neighbors** limits
the graph to the selected entity and directly connected entities in either direction.
This visual traversal creates no inverse edges. Focus remains anchored until changed;
search further narrows it. **Show all** clears focus and search. The unbound-action
section is always independent of graph/entity filters; expand any operation to inspect
its full metadata. Graph nodes/edges support Tab, Enter, and Space.

The deterministic grid uses curved parallel edges and self-loops and requires no
animation, graph library, backend, or internet. The graph scrolls horizontally and
vertically. For large catalogs, drawing is capped at 200 nodes and 500 visible edges;
the status explains limits and search/focus can narrow the selection. The entity list,
details, and source ontology retain the full catalog. Dense graphs can have crossing
edges or overlapping labels; focus is the recommended way to inspect them.

Try the airline fixture:

```powershell
dotnet run --project src/Ontology.Cli -- build --input samples/airline-reservation-openapi.yaml --output out/airline
```

Load `out/airline/ontology.json`. Select Booking, inspect its flight/passenger edges,
focus its neighbors, expand its actions, and inspect `getHealth` under unbound actions.
The extractor reuses named entity identities for operation schema references;
only genuine anonymous object schemas receive generated inline identities.

Automated checks (Node.js 18+):

```powershell
node --test tests/viewer/viewer.test.cjs
```

Tests cover validation, rendering via a DOM double, graph selection, keyboard activation,
neighbor focus, parallel edges/self-loops, candidate styling, the checked-in airline
CLI export fixture, unbound actions, filtering, nested properties, action metadata,
empty states, unsafe source text, replacement failures, and file limits.
Real-browser QA remains blocked: the browser access host is unavailable (and the prior
native attempt could not confidently determine the current Windows browser URL). Local file picker,
visual layout, and responsive behavior still need manual browser verification.
