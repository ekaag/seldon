const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const { parseExport, buildGraph, mount } = require('../../viewer/viewer.js');

function fixture() {
  return { schemaVersion: '1.0', ontology: { id: 'sample', name: 'Test catalog', entities: [
    { id: 'entity:A', name: 'Alpha', description: 'A schema type', properties: [
      { name: 'id', dataType: 'string', required: true, format: 'uuid', enumValues: ['one', 'two'], provenance: { document: 'api.yaml', sourcePointer: '/schemas/A/id' } },
      { name: 'detail', dataType: 'object', required: false, properties: [{ name: 'value', dataType: 'number', required: false }] }
    ], actionIds: ['action:get'] },
    { id: 'entity:B', name: 'Beta', properties: [], actionIds: [] }
  ], relationships: [], actions: [
    { id: 'action:get', name: 'getAlpha', httpMethod: 'GET', path: '/alpha', operationId: 'getAlpha', sourcePointer: '/paths/alpha/get', entityIds: ['entity:A'], responseSchemaIds: ['entity:A'],
      parameters: [{ name: 'id', location: 'path', required: true, dataType: 'string' }], requestSchemas: [],
      responses: [{ statusCode: '200', description: 'OK', schemas: [{ schemaId: 'entity:A', mediaType: 'application/json', sourcePointer: '/schema' }] }],
      security: [{ oauth: ['read'] }], securityInherited: true, associations: [{ entityId: 'entity:A', evidenceKind: 'ExplicitSchemaRef', sourcePointer: '/schema' }] }
  ] } };
}

// A tiny DOM double exercises our render and event code without browser dependencies.
class Element {
  constructor(tag = 'div') { this.tag = tag; this.children = []; this.dataset = {}; this.attributes = {}; this.events = {}; this.value = ''; this.disabled = false; this._text = ''; }
  set textContent(value) { this._text = String(value); this.children = []; }
  get textContent() { return this._text + this.children.map(node => node.textContent).join(' '); }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this._text = ''; this.children = [...nodes]; }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(name, callback) { this.events[name] = callback; }
  querySelectorAll(tag) { return this.children.flatMap(child => [...(child.tag === tag ? [child] : []), ...child.querySelectorAll(tag)]); }
}
function dom() {
  const elements = new Map();
  return { createElement: tag => new Element(tag), createElementNS: (_, tag) => new Element(tag), getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); } };
}

test('valid 1.0 document indexes entities and actions', () => {
  const result = parseExport(JSON.stringify(fixture()));
  assert.equal(result.entities.size, 2); assert.equal(result.actions.size, 1);
});
test('malformed JSON and unsupported versions have readable errors', () => {
  assert.throws(() => parseExport('{'), /Malformed JSON/);
  const input = fixture(); input.schemaVersion = '2.0';
  assert.throws(() => parseExport(JSON.stringify(input)), /Unsupported schemaVersion/);
});
for (const [name, mutate, expected] of [
  ['missing entities', d => delete d.ontology.entities, /entities must be an array/],
  ['null entity', d => d.ontology.entities.push(null), /must be an object/],
  ['duplicate entity', d => d.ontology.entities.push(d.ontology.entities[0]), /Duplicate entity ID/],
  ['dangling action', d => d.ontology.entities[0].actionIds.push('missing'), /Unknown action reference/],
  ['dangling entity', d => d.ontology.actions[0].entityIds.push('missing'), /Unknown entity reference/],
  ['bad property', d => d.ontology.entities[0].properties[0].required = 'yes', /must be a boolean/],
  ['bad response', d => d.ontology.actions[0].responses.push(null), /must be an object/],
  ['bad security', d => d.ontology.actions[0].security = [{ oauth: 42 }], /must be an array/],
  ['bad association', d => d.ontology.actions[0].associations[0].entityId = 'missing', /unbound entity/],
  ['dangling edge', d => d.ontology.relationships.push({ id: 'edge', name: 'related', cardinality: 'one', evidenceKind: 'Heuristic', sourcePointer: '', sourceEntityId: 'entity:A', targetEntityId: 'missing' }), /Dangling relationship/]
]) test(`rejects ${name}`, () => { const input = fixture(); mutate(input); assert.throws(() => parseExport(JSON.stringify(input)), expected); });

test('renders properties, nested values, actions and security metadata', () => {
  const document = dom(); mount(document).loadText(JSON.stringify(fixture()), 'ontology.json');
  const text = document.getElementById('details').textContent;
  for (const expected of ['Alpha', 'uuid', 'Required', 'Enum:', 'detail', 'value', 'GET /alpha', 'Parameters', 'Responses', '200', 'Security', 'oauth', 'read']) assert.ok(text.includes(expected), expected);
  assert.equal(document.getElementById('entity-count').textContent, '2');
  assert.equal(document.getElementById('entity-list').querySelectorAll('button').length, 2);
});
test('search filters names and IDs and handles no matches', () => {
  const document = dom(); mount(document).loadText(JSON.stringify(fixture()), 'sample.json');
  const search = document.getElementById('entity-search'); search.value = 'entity:B'; search.events.input();
  assert.equal(document.getElementById('entity-list').querySelectorAll('button').length, 1);
  document.getElementById('entity-list').querySelectorAll('button')[0].events.click();
  assert.ok(document.getElementById('details').textContent.includes('Beta'));
  assert.ok(document.getElementById('details').textContent.includes('No properties defined'));
  search.value = 'nothing'; search.events.input();
  assert.match(document.getElementById('entity-list').textContent, /No matching entities/);
});
test('empty ontology renders a clear state', () => {
  const input = fixture(); input.ontology.entities = []; input.ontology.actions = [];
  const document = dom(); mount(document).loadText(JSON.stringify(input), 'empty.json');
  assert.match(document.getElementById('details').textContent, /No entities defined/);
  assert.match(document.getElementById('entity-list').textContent, /no entities/);
});
test('invalid replacement leaves the previous view intact and shows an error', async () => {
  const document = dom(); mount(document).loadText(JSON.stringify(fixture()), 'valid.json');
  const file = document.getElementById('ontology-file');
  await file.events.change({ target: { files: [{ size: 1, name: 'bad.json', text: async () => '{' }] } });
  assert.match(document.getElementById('message').textContent, /previous ontology is still displayed/);
  assert.equal(document.getElementById('ontology-name').textContent, 'Test catalog');
});
test('uses literal text for untrusted names and descriptions', () => {
  const input = fixture(); const payload = '<img src=x onerror=alert(1)>';
  input.ontology.entities[0].name = payload; input.ontology.entities[0].description = payload;
  const document = dom(); mount(document).loadText(JSON.stringify(input), payload);
  const buttons = document.getElementById('entity-list').querySelectorAll('button');
  buttons.find(b => b.dataset.entityId === 'entity:A').events.click();
  assert.ok(document.getElementById('details').textContent.includes(payload));
  assert.equal(document.getElementById('details').querySelectorAll('img').length, 0);
  assert.equal(document.getElementById('details').querySelectorAll('script').length, 0);
});
test('oversized input is rejected before reading', async () => {
  const document = dom(); mount(document); let read = false;
  await document.getElementById('ontology-file').events.change({ target: { files: [{ size: 17 * 1024 * 1024, text: async () => { read = true; return ''; } }] } });
  assert.equal(read, false); assert.match(document.getElementById('message').textContent, /16 MiB/);
});
test('classic local script has no network loading or unsafe HTML assignment', () => {
  const script = readFileSync(resolve(__dirname, '../../viewer/viewer.js'), 'utf8');
  const html = readFileSync(resolve(__dirname, '../../viewer/index.html'), 'utf8');
  assert.doesNotMatch(script, /innerHTML|\beval\(|\bfetch\(|XMLHttpRequest/);
  assert.doesNotMatch(html, /type="module"|https?:\/\//);
  assert.match(html, /connect-src 'none'/);
});

function graphFixture() {
  const input = fixture();
  input.ontology.entities.push({ id: 'entity:C', name: 'Gamma', properties: [], actionIds: [] });
  input.ontology.entities.push({ id: 'entity:D', name: 'Isolated', properties: [], actionIds: [] });
  input.ontology.relationships = [
    { id: 'edge:1', sourceEntityId: 'entity:A', targetEntityId: 'entity:B', name: 'owner', cardinality: 'one', evidenceKind: 'ExplicitSchemaRef', sourcePropertyName: 'owner', sourcePointer: '/schemas/A/owner/$ref' },
    { id: 'edge:2', sourceEntityId: 'entity:A', targetEntityId: 'entity:B', name: 'reviewerId', cardinality: 'unknown', evidenceKind: 'Heuristic', sourcePropertyName: 'reviewerId', sourcePointer: '/schemas/A/reviewerId' },
    { id: 'edge:3', sourceEntityId: 'entity:B', targetEntityId: 'entity:C', name: 'children', cardinality: 'many', evidenceKind: 'ExplicitSchemaRef', sourcePointer: '/schemas/B/children/items/$ref' },
    { id: 'edge:4', sourceEntityId: 'entity:A', targetEntityId: 'entity:A', name: 'parent', cardinality: 'one', evidenceKind: 'ExplicitSchemaRef', sourcePointer: '/schemas/A/parent/$ref' }
  ];
  input.ontology.actions.push({ id: 'action:health', name: 'Health', httpMethod: 'GET', path: '/health', entityIds: [], responseSchemaIds: [], sourcePointer: '/paths/health/get' });
  return input;
}
test('graph creates distinct curves for parallel edges and self-loops', () => {
  const graph = buildGraph(parseExport(JSON.stringify(graphFixture())));
  assert.equal(graph.nodes.length, 4); assert.equal(graph.edges.length, 4);
  assert.notEqual(graph.edges[0].path, graph.edges[1].path);
  assert.ok(graph.edges.find(e => e.relationship.id === 'edge:4').path.includes(' C '));
  for (const edge of graph.edges) { assert.ok(Number.isFinite(edge.x)); assert.ok(Number.isFinite(edge.y)); }
});
test('neighbor focus traverses both directions without inventing edges', () => {
  const model = parseExport(JSON.stringify(graphFixture()));
  const graph = buildGraph(model, '', 'entity:B');
  assert.deepEqual(graph.nodes.map(n => n.entity.id), ['entity:A', 'entity:B', 'entity:C']);
  assert.equal(model.ontology.relationships.length, 4);
  assert.equal(buildGraph(model, 'Alpha', 'entity:B').nodes.length, 1);
  assert.equal(buildGraph(model, 'missing').edges.length, 0);
});
test('SVG controls select entity and relationship details and mark candidates', () => {
  const document = dom(); mount(document).loadText(JSON.stringify(graphFixture()), 'graph.json');
  let groups = document.getElementById('graph').querySelectorAll('g');
  const candidate = groups.find(g => g.dataset.relationshipId === 'edge:2');
  assert.match(candidate.attributes.class, /candidate/);
  assert.match(candidate.attributes['aria-label'], /unverified candidate/);
  candidate.events.click();
  assert.match(document.getElementById('details').textContent, /Unverified \/ candidate/);
  assert.match(document.getElementById('details').textContent, /reviewerId/);
  assert.match(document.getElementById('details').textContent, /unknown/);
  assert.match(document.getElementById('details').textContent, /schemas\/A\/reviewerId/);
  groups.find(g => g.dataset.entityId === 'entity:B').events.keydown({ key: 'Enter', preventDefault() {} });
  assert.match(document.getElementById('details').textContent, /Beta/);
  document.getElementById('focus-entity').events.click();
  assert.match(document.getElementById('graph-status').textContent, /Focus: Beta/);
  assert.equal(document.getElementById('graph').querySelectorAll('g').filter(g => g.dataset.entityId).length, 3);
  document.getElementById('reset-graph').events.click();
  assert.equal(document.getElementById('graph').querySelectorAll('g').filter(g => g.dataset.entityId).length, 4);
});
test('unbound actions remain inspectable even without any entities', () => {
  const input = graphFixture(); input.ontology.entities = []; input.ontology.relationships = []; input.ontology.actions = input.ontology.actions.filter(a => a.entityIds.length === 0);
  const document = dom(); mount(document).loadText(JSON.stringify(input), 'empty.json');
  assert.match(document.getElementById('unbound-actions').textContent, /GET \/health/);
  assert.match(document.getElementById('unbound-heading').textContent, /\(1\)/);
  assert.equal(document.getElementById('unbound-actions').querySelectorAll('details').length, 1);
});
test('SVG names and edge labels never become markup', () => {
  const input = graphFixture(); const payload = '<script>alert(1)</script>';
  input.ontology.relationships[0].name = payload; input.ontology.entities[0].name = payload;
  const document = dom(); mount(document).loadText(JSON.stringify(input), 'unsafe.json');
  const graph = document.getElementById('graph');
  assert.equal(graph.querySelectorAll('script').length, 0);
  assert.ok(graph.textContent.includes(payload));
});
test('large graph rendering is bounded and deterministic', () => {
  const input = fixture(); input.ontology.entities = Array.from({ length: 250 }, (_, i) => ({ id: 'e:' + i, name: 'Entity ' + String(i).padStart(3, '0'), properties: [], actionIds: [] })); input.ontology.actions = [];
  input.ontology.relationships = Array.from({ length: 600 }, (_, i) => ({ id: 'r:' + i, name: 'link', sourceEntityId: 'e:' + (i % 180), targetEntityId: 'e:' + ((i + 1) % 180), cardinality: 'many', evidenceKind: 'ExplicitSchemaRef', sourcePointer: '' }));
  const model = parseExport(JSON.stringify(input)); const graph = buildGraph(model);
  assert.equal(graph.nodes.length, 200); assert.equal(graph.edges.length, 500);
  assert.equal(graph.matchingCount, 250); assert.equal(graph.edgeCount, 600);
  assert.deepEqual(buildGraph(model), graph);
});
test('airline CLI export renders a navigable graph, actions, candidates and unbound operations', () => {
  const json = readFileSync(resolve(__dirname, 'fixtures/airline-ontology.json'), 'utf8');
  const model = parseExport(json); const graph = buildGraph(model);
  assert.equal(model.entities.size, 3); assert.equal(graph.edges.length, 4); assert.equal(model.actions.size, 3);
  for (const name of ['Passenger', 'Booking', 'Flight']) assert.ok(graph.nodes.some(n => n.entity.name === name));
  assert.ok(graph.edges.some(e => e.relationship.name === 'flight' && e.relationship.cardinality === 'one'));
  assert.ok(graph.edges.some(e => e.relationship.cardinality === 'many'));
  assert.ok(graph.edges.some(e => e.relationship.evidenceKind === 'Heuristic'));
  const document = dom(); mount(document).loadText(json, 'airline-ontology.json');
  const booking = document.getElementById('graph').querySelectorAll('g').find(g => g.dataset.entityId === 'schema:/components/schemas/Booking');
  booking.events.click();
  assert.match(document.getElementById('details').textContent, /createBooking/);
  assert.match(document.getElementById('details').textContent, /listPassengerBookings/);
  assert.match(document.getElementById('unbound-actions').textContent, /getHealth/);
});
