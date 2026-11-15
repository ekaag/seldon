(function (root) {
  'use strict';
  const MAX_BYTES = 16 * 1024 * 1024;
  function validateExport(data) {
    let nodes = 0;
    const check = (condition, message) => { if (!condition) throw new Error(message); };
    const object = (value, path) => check(value !== null && typeof value === 'object' && !Array.isArray(value), `${path} must be an object.`);
    const text = (value, path) => check(typeof value === 'string' && value.trim().length > 0, `${path} must be a non-empty string.`);
    const list = (value, path) => { check(Array.isArray(value), `${path} must be an array.`); return value; };
    const optionalList = (value, path) => value == null ? [] : list(value, path);
    const strings = (value, path) => list(value, path).forEach(v => text(v, path));
    const optionalText = (value, path) => check(value == null || typeof value === 'string', `${path} must be a string or null.`);
    const provenance = value => { if (value != null) { object(value, 'provenance'); optionalText(value.document, 'provenance.document'); optionalText(value.sourcePointer, 'provenance.sourcePointer'); } };
    function property(p, depth) {
      check(depth <= 32 && ++nodes <= 100000, 'Property nesting or count exceeds viewer limits.');
      object(p, 'property'); text(p.name, 'property.name'); text(p.dataType, 'property.dataType');
      check(typeof p.required === 'boolean', 'property.required must be a boolean.');
      optionalText(p.description, 'property.description'); optionalText(p.format, 'property.format');
      optionalText(p.referencedSchemaId, 'property.referencedSchemaId'); provenance(p.provenance);
      check(p.nullable == null || typeof p.nullable === 'boolean', 'property.nullable must be a boolean.');
      optionalList(p.enumValues, 'property.enumValues');
      optionalList(p.properties, 'property.properties').forEach(v => property(v, depth + 1));
      if (p.items != null) property(p.items, depth + 1);
    }
    function unique(items, label) {
      const result = new Map();
      items.forEach(item => { object(item, label); text(item.id, `${label}.id`); check(!result.has(item.id), `Duplicate ${label} ID: ${item.id}`); result.set(item.id, item); });
      return result;
    }
    object(data, 'Export'); check(data.schemaVersion === '1.0', `Unsupported schemaVersion: ${String(data.schemaVersion)}. Expected 1.0.`);
    object(data.ontology, 'ontology'); const ontology = data.ontology;
    text(ontology.id, 'ontology.id'); text(ontology.name, 'ontology.name');
    const entities = unique(list(ontology.entities, 'entities'), 'entity');
    const actions = unique(list(ontology.actions, 'actions'), 'action');
    list(ontology.relationships, 'relationships'); unique(ontology.relationships, 'relationship');
    entities.forEach(e => { text(e.name, 'entity.name'); optionalText(e.description, 'entity.description'); provenance(e.provenance); list(e.properties, 'entity.properties').forEach(p => property(p, 0)); strings(e.actionIds, 'entity.actionIds'); e.actionIds.forEach(id => check(actions.has(id), `Unknown action reference: ${id}`)); });
    actions.forEach(a => {
      ['name', 'httpMethod', 'path'].forEach(key => text(a[key], `action.${key}`));
      check(typeof a.sourcePointer === 'string', 'action.sourcePointer must be a string.');
      ['summary', 'description', 'operationId', 'requestSchemaId'].forEach(key => optionalText(a[key], `action.${key}`));
      strings(a.entityIds, 'action.entityIds'); strings(a.responseSchemaIds, 'action.responseSchemaIds');
      a.entityIds.forEach(id => check(entities.has(id), `Unknown entity reference: ${id}`));
      optionalList(a.tags, 'action.tags').forEach(tag => text(tag, 'tag'));
      optionalList(a.parameters, 'action.parameters').forEach(p => { object(p, 'parameter'); text(p.name, 'parameter.name'); text(p.location, 'parameter.location'); text(p.dataType, 'parameter.dataType'); check(typeof p.required === 'boolean', 'parameter.required must be a boolean.'); });
      function bindings(values) { optionalList(values, 'schema bindings').forEach(b => { object(b, 'schema binding'); text(b.schemaId, 'schemaId'); optionalText(b.mediaType, 'mediaType'); optionalText(b.sourcePointer, 'sourcePointer'); }); }
      bindings(a.requestSchemas);
      optionalList(a.responses, 'action.responses').forEach(r => { object(r, 'response'); text(r.statusCode, 'statusCode'); optionalText(r.description, 'response.description'); bindings(r.schemas); });
      optionalList(a.security, 'action.security').forEach(s => { object(s, 'security requirement'); Object.values(s).forEach(scopes => strings(scopes, 'security scopes')); });
      optionalList(a.associations, 'action.associations').forEach(v => { object(v, 'association'); check(a.entityIds.includes(v.entityId), 'Association references an unbound entity.'); text(v.evidenceKind, 'evidenceKind'); optionalText(v.sourcePointer, 'sourcePointer'); });
    });
    ontology.relationships.forEach(r => { ['name', 'cardinality', 'evidenceKind'].forEach(key => text(r[key], `relationship.${key}`)); check(entities.has(r.sourceEntityId) && entities.has(r.targetEntityId), `Dangling relationship: ${r.id}`); check(typeof r.sourcePointer === 'string', 'relationship.sourcePointer must be a string.'); });
    return { ontology, entities, actions };
  }
  function parseExport(text) {
    if (new TextEncoder().encode(text).length > MAX_BYTES) throw new Error('File exceeds the 16 MiB viewer limit.');
    let data;
    try { data = JSON.parse(text); } catch { throw new Error('Malformed JSON. Choose an exported ontology.json file.'); }
    return validateExport(data);
  }
  function buildGraph(model, query = '', focusId = null) {
    const term = query.trim().toLocaleLowerCase();
    const neighbors = new Set(focusId ? [focusId] : []);
    if (focusId) for (const r of model.ontology.relationships) {
      if (r.sourceEntityId === focusId) neighbors.add(r.targetEntityId);
      if (r.targetEntityId === focusId) neighbors.add(r.sourceEntityId);
    }
    const matching = [...model.entities.values()].filter(e => (!focusId || neighbors.has(e.id)) &&
      (e.name + ' ' + e.id).toLocaleLowerCase().includes(term)).sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
    const columns = Math.max(1, Math.ceil(Math.sqrt(Math.min(matching.length, 200))));
    const nodes = matching.slice(0, 200).map((entity, index) => ({ entity, x: 150 + (index % columns) * 280, y: 100 + Math.floor(index / columns) * 180 }));
    const positions = new Map(nodes.map(node => [node.entity.id, node]));
    const matchingEdges = model.ontology.relationships.filter(r => positions.has(r.sourceEntityId) && positions.has(r.targetEntityId)).sort((a, b) => a.id.localeCompare(b.id));
    const lanes = new Map();
    const edges = matchingEdges.slice(0, 500).map(relationship => {
      const a = positions.get(relationship.sourceEntityId), b = positions.get(relationship.targetEntityId);
      const key = JSON.stringify([a.entity.id, b.entity.id].sort());
      const lane = lanes.get(key) ?? 0; lanes.set(key, lane + 1);
      if (a === b) {
        const offset = 50 + lane * 24;
        return { relationship, path: `M ${a.x - 36} ${a.y - 34} C ${a.x - offset - 60} ${a.y - offset - 65}, ${a.x + offset + 60} ${a.y - offset - 65}, ${a.x + 36} ${a.y - 34}`, x: a.x, y: a.y - 74 - lane * 18 };
      }
      const dx = b.x - a.x, dy = b.y - a.y, distance = Math.hypot(dx, dy);
      const curve = (lane + 1) * 28 * (lane % 2 ? -1 : 1);
      const cx = (a.x + b.x) / 2 - dy / distance * curve;
      const cy = (a.y + b.y) / 2 + dx / distance * curve;
      const padding = Math.min(114 / Math.abs(dx / distance), 34 / Math.abs(dy / distance));
      return { relationship, path: `M ${a.x + dx / distance * padding} ${a.y + dy / distance * padding} Q ${cx} ${cy} ${b.x - dx / distance * padding} ${b.y - dy / distance * padding}`,
        x: (a.x + 2 * cx + b.x) / 4, y: (a.y + 2 * cy + b.y) / 4 };
    });
    return { nodes, edges, matchingCount: matching.length, edgeCount: matchingEdges.length,
      width: Math.max(600, columns * 280 + 20), height: Math.max(240, Math.ceil(nodes.length / columns) * 180 + 60) };
  }
  function mount(document, initialJson = root.seldonOntologyJson) {
    const get = id => document.getElementById(id);
    let model = null, selected = null, generation = 0, focusId = null;
    function element(tag, text, className) { const node = document.createElement(tag); if (text != null) node.textContent = text; if (className) node.className = className; return node; }
    function badge(text, warning) { return element('span', text, 'badge' + (warning ? ' warn' : '')); }
    function button(text, callback) { const node = element('button', text, 'detail-link'); node.type = 'button'; node.addEventListener('click', callback); return node; }
    function showRelationship(r) {
      const panel = get('details');
      panel.replaceChildren(element('p', 'RELATIONSHIP', 'eyebrow'), element('h2', r.name),
        badge(r.evidenceKind === 'ExplicitSchemaRef' ? 'Explicit schema evidence' : 'Unverified / candidate', r.evidenceKind !== 'ExplicitSchemaRef'));
      panel.append(element('h3', 'Source'), button(model.entities.get(r.sourceEntityId).name, () => showEntity(model.entities.get(r.sourceEntityId))),
        element('div', r.sourceEntityId, 'identifier'), element('h3', 'Target'), button(model.entities.get(r.targetEntityId).name, () => showEntity(model.entities.get(r.targetEntityId))), element('div', r.targetEntityId, 'identifier'));
      const fields = element('dl', null, 'relationship-fields');
      for (const [name, value] of [['Property', r.sourcePropertyName ?? 'None (composition or type-level edge)'], ['Cardinality', r.cardinality], ['Evidence', r.evidenceKind], ['Source pointer', r.sourcePointer], ['Relationship ID', r.id]]) fields.append(element('dt', name), element('dd', value));
      panel.append(fields);
    }
    function renderGraph() {
      const graph = buildGraph(model, get('entity-search').value, focusId);
      const container = get('graph'); container.replaceChildren();
      get('reset-graph').disabled = false; get('focus-entity').disabled = !selected;
      get('graph-status').textContent = `${graph.nodes.length} of ${graph.matchingCount} matching entities · ${graph.edges.length} of ${graph.edgeCount} visible relationships${focusId ? ' · Focus: ' + model.entities.get(focusId).name : ''}${graph.matchingCount > 200 || graph.edgeCount > 500 ? ' · Display limited; search or focus to narrow the graph.' : ''}`;
      if (!graph.nodes.length) { container.append(element('p', model.entities.size ? 'No entities match this search/focus.' : 'This ontology has no entities.', 'empty')); return; }
      function svg(tag, attributes = {}, text) {
        const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
        for (const [name, value] of Object.entries(attributes)) node.setAttribute(name, String(value));
        if (text != null) node.textContent = text;
        return node;
      }
      function activate(node, callback) { node.addEventListener('click', callback); node.addEventListener('keydown', event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); callback(); } }); }
      const canvas = svg('svg', { viewBox: `0 0 ${graph.width} ${graph.height}`, width: graph.width, height: graph.height, role: 'group', 'aria-label': 'Entity relationship graph. Tab to nodes and edges; press Enter for details.' });
      const defs = svg('defs');
      for (const [id, color] of [['explicit-arrow', '#126550'], ['candidate-arrow', '#a06a17']]) { const marker = svg('marker', { id, viewBox: '0 0 10 10', refX: 9, refY: 5, markerWidth: 7, markerHeight: 7, orient: 'auto-start-reverse' }); marker.append(svg('path', { d: 'M 0 0 L 10 5 L 0 10 z', fill: color })); defs.append(marker); }
      canvas.append(defs);
      for (const edge of graph.edges) {
        const r = edge.relationship, candidate = r.evidenceKind !== 'ExplicitSchemaRef';
        const group = svg('g', { class: 'graph-edge' + (candidate ? ' candidate' : ''), role: 'button', tabindex: 0,
          'aria-label': `${model.entities.get(r.sourceEntityId).name} to ${model.entities.get(r.targetEntityId).name}: ${r.name}, ${r.cardinality}, ${candidate ? 'unverified candidate' : r.evidenceKind}` });
        group.dataset.relationshipId = r.id;
        group.append(svg('title', {}, `${r.name} · ${r.cardinality} · ${r.evidenceKind}`), svg('path', { d: edge.path, class: 'edge-hit' }), svg('path', { d: edge.path, class: 'edge-line', 'marker-end': `url(#${candidate ? 'candidate-arrow' : 'explicit-arrow'})` }));
        const label = `${r.name.length > 25 ? r.name.slice(0, 22) + '…' : r.name} · ${r.cardinality}${candidate ? ' ?' : ''}`;
        const width = Math.min(255, label.length * 6.5 + 14);
        group.append(svg('rect', { x: edge.x - width / 2, y: edge.y - 11, width, height: 22, rx: 3, class: 'edge-label-bg' }), svg('text', { x: edge.x, y: edge.y + 4, 'text-anchor': 'middle', class: 'edge-label' }, label));
        activate(group, () => showRelationship(r)); canvas.append(group);
      }
      for (const node of graph.nodes) {
        const entity = node.entity;
        const group = svg('g', { class: 'graph-node' + (selected === entity.id ? ' selected' : ''), role: 'button', tabindex: 0, 'aria-label': `Entity ${entity.name}`, 'aria-pressed': selected === entity.id });
        group.dataset.entityId = entity.id;
        const name = entity.name.length > 27 ? entity.name.slice(0, 24) + '…' : entity.name;
        group.append(svg('title', {}, entity.name + '\n' + entity.id), svg('rect', { x: node.x - 110, y: node.y - 30, width: 220, height: 60, rx: 7 }),
          svg('text', { x: node.x, y: node.y - 2, 'text-anchor': 'middle', class: 'node-label' }, name), svg('text', { x: node.x, y: node.y + 18, 'text-anchor': 'middle', class: 'node-meta' }, `${entity.properties.length} properties · ${entity.actionIds.length} actions`));
        activate(group, () => showEntity(entity)); canvas.append(group);
      }
      container.append(canvas);
    }
    function property(p, depth = 0) {
      const box = element('div', null, 'property');
      const title = element('div', null, 'property-title');
      title.append(element('strong', p.name), element('code', p.dataType + (p.format ? ` · ${p.format}` : '')), badge(p.required ? 'Required' : 'Optional'));
      if (p.nullable) title.append(badge('Nullable'));
      box.append(title);
      if (p.description) box.append(element('p', p.description));
      if (p.referencedSchemaId) box.append(element('div', `Schema: ${p.referencedSchemaId}`, 'identifier'));
      if (p.enumValues?.length) box.append(element('code', 'Enum: ' + JSON.stringify(p.enumValues)));
      if (p.provenance) box.append(element('div', `${p.provenance.document ?? ''} ${p.provenance.sourcePointer ?? ''}`, 'identifier'));
      if (depth < 32) { if (p.items) box.append(property(p.items, depth + 1)); (p.properties ?? []).forEach(child => box.append(property(child, depth + 1))); }
      return box;
    }
    function action(a) {
      const box = element('details', null, 'action'); box.append(element('summary', `${a.httpMethod} ${a.path} · ${a.name}`));
      if (a.summary) box.append(element('p', a.summary)); if (a.description) box.append(element('p', a.description));
      const fields = element('dl');
      const add = (label, value) => fields.append(element('dt', label), element('dd', value));
      add('Operation ID', a.operationId ?? 'Not supplied'); add('Action ID', a.id); add('Source', a.sourcePointer);
      add('Tags', (a.tags ?? []).join(', ') || 'None'); add('Request required', a.requestBodyRequired ? 'Yes' : 'No');
      add('Request schemas', (a.requestSchemas ?? []).map(s => `${s.mediaType ?? 'unspecified'} → ${s.schemaId}`).join('\n') || a.requestSchemaId || 'None');
      add('Entities', a.entityIds.map(id => model.entities.get(id).name).join(', ') || 'Unbound'); box.append(fields);
      box.append(element('h3', 'Parameters'));
      if (!a.parameters?.length) box.append(element('p', 'No parameters.', 'muted'));
      (a.parameters ?? []).forEach(p => box.append(element('p', `${p.location} · ${p.name} · ${p.dataType}${p.format ? ' (' + p.format + ')' : ''} · ${p.required ? 'required' : 'optional'}${p.description ? '\n' + p.description : ''}`)));
      box.append(element('h3', 'Responses'));
      if (!a.responses?.length) box.append(element('p', a.responseSchemaIds.join(', ') || 'No response metadata.', 'muted'));
      (a.responses ?? []).forEach(r => box.append(element('p', `${r.statusCode} · ${r.description ?? ''}\n${(r.schemas ?? []).map(s => `${s.mediaType ?? 'unspecified'} → ${s.schemaId}`).join('\n') || 'No schema'}`)));
      box.append(element('h3', `Security${a.securityInherited ? ' · inherited' : ''}`), element('pre', JSON.stringify(a.security ?? [], null, 2)));
      if (a.associations?.length) { box.append(element('h3', 'Association evidence')); a.associations.forEach(v => { const row = element('p', `${model.entities.get(v.entityId).name} · ${v.evidenceKind}\n${v.sourcePointer ?? ''}`); if (v.evidenceKind.includes('Heuristic')) row.append(badge('Unverified', true)); box.append(row); }); }
      return box;
    }
    function showEntity(entity) {
      selected = entity.id;
      const panel = get('details'); panel.replaceChildren(element('p', 'ENTITY TYPE', 'eyebrow'), element('h2', entity.name), element('div', entity.id, 'identifier'));
      if (entity.description) panel.append(element('p', entity.description));
      if (entity.provenance) panel.append(element('div', `${entity.provenance.document ?? ''} ${entity.provenance.sourcePointer ?? ''}`, 'identifier'));
      panel.append(element('h3', `Properties (${entity.properties.length})`));
      if (!entity.properties.length) panel.append(element('p', 'No properties defined.', 'muted'));
      entity.properties.forEach(p => panel.append(property(p)));
      const associated = [...model.actions.values()].filter(a => entity.actionIds.includes(a.id) || a.entityIds.includes(entity.id));
      panel.append(element('h3', `Associated actions (${associated.length})`));
      if (!associated.length) panel.append(element('p', 'No associated actions.', 'muted'));
      associated.forEach(a => panel.append(action(a)));
      const connected = model.ontology.relationships.filter(r => r.sourceEntityId === entity.id || r.targetEntityId === entity.id);
      panel.append(element('h3', `Relationships (${connected.length})`));
      if (!connected.length) panel.append(element('p', 'No relationships.', 'muted'));
      connected.forEach(r => panel.append(button(`${model.entities.get(r.sourceEntityId).name} → ${model.entities.get(r.targetEntityId).name} · ${r.name} · ${r.cardinality}${r.evidenceKind !== 'ExplicitSchemaRef' ? ' · Unverified candidate' : ''}`, () => showRelationship(r))));
      for (const button of get('entity-list').querySelectorAll('button')) button.setAttribute('aria-current', String(button.dataset.entityId === selected));
      renderGraph();
    }
    function renderList() {
      const list = get('entity-list'); list.replaceChildren();
      const term = get('entity-search').value.trim().toLocaleLowerCase();
      const entities = [...model.entities.values()].filter(e => (e.name + ' ' + e.id).toLocaleLowerCase().includes(term)).sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
      get('filter-count').textContent = `${entities.length} shown`;
      if (!entities.length) list.append(element('p', model.entities.size ? 'No matching entities.' : 'This ontology has no entities.', 'empty'));
      for (const entity of entities) { const button = element('button', null, 'entity-button'); button.type = 'button'; button.dataset.entityId = entity.id; button.setAttribute('aria-current', String(selected === entity.id)); button.append(element('strong', entity.name), element('small', `${entity.properties.length} properties · ${entity.actionIds.length} actions`)); button.addEventListener('click', () => showEntity(entity)); list.append(button); }
      renderGraph();
    }
    function loadText(text, filename) {
      const next = parseExport(text); // Preserve the current view if validation fails.
      model = next; selected = null; focusId = null;
      get('ontology-name').textContent = model.ontology.name;
      get('ontology-description').textContent = `${filename} · Schema version 1.0 · ${model.ontology.id}`;
      get('entity-count').textContent = String(model.entities.size); get('relationship-count').textContent = String(model.ontology.relationships.length); get('action-count').textContent = String(model.actions.size);
      get('entity-search').disabled = false; get('entity-search').value = ''; renderList();
      get('message').className = ''; get('message').textContent = `Loaded ${filename}. Select an entity to explore its properties and actions.`;
      const first = [...model.entities.values()].sort((a, b) => a.name.localeCompare(b.name))[0];
      if (first) showEntity(first); else get('details').replaceChildren(element('h2', 'No entities defined.'), element('p', `${model.actions.size} actions are retained in this ontology. Entity details will appear here when an entity is available.`, 'muted'));
      const unbound = [...model.actions.values()].filter(a => a.entityIds.length === 0).sort((a, b) => a.id.localeCompare(b.id));
      get('unbound-heading').textContent = `Unbound actions (${unbound.length})`;
      get('unbound-actions').replaceChildren();
      if (!unbound.length) get('unbound-actions').append(element('p', 'No unbound actions.', 'muted'));
      unbound.forEach(a => get('unbound-actions').append(action(a)));
    }
    get('focus-entity').addEventListener('click', () => { if (model && selected) { focusId = selected; renderGraph(); } });
    get('reset-graph').addEventListener('click', () => { if (model) { focusId = null; get('entity-search').value = ''; renderList(); } });
    get('entity-search').addEventListener('input', () => { if (model) renderList(); });
    get('ontology-file').addEventListener('change', async event => {
      const file = event.target.files?.[0]; if (!file) return;
      const current = ++generation;
      try {
        if (file.size > MAX_BYTES) throw new Error('File exceeds the 16 MiB viewer limit.');
        const text = await file.text(); if (current !== generation) return; loadText(text, file.name);
      } catch (error) { if (current === generation) { get('message').className = 'error'; get('message').textContent = `Could not load file: ${error.message}${model ? ' The previous ontology is still displayed.' : ''}`; } }
      finally { if (current === generation) event.target.value = ''; }
    });
    if (typeof initialJson === 'string') {
      try { loadText(initialJson, 'ontology.json (bundled export)'); }
      catch (error) { get('message').className = 'error'; get('message').textContent = `Could not load bundled ontology: ${error.message} Choose Load ontology.json to select a file.`; }
    }
    return { loadText };
  }
  const api = { validateExport, parseExport, buildGraph, mount, MAX_BYTES };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else if (root.document) mount(root.document);
})(typeof globalThis !== 'undefined' ? globalThis : this);
