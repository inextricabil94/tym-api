(function (root, factory) {
  'use strict';
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TymOrdering = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const strict = new Set(['BEFORE', 'IBEFORE', 'IMMEDIATELY_BEFORE', 'AFTER', 'IAFTER', 'IMMEDIATELY_AFTER']);
  const equal = new Set(['SIMULTANEOUS', 'IDENTITY']);
  const overlap = new Set(['OVERLAP', 'OVERLAPS']);
  const passive = new Set(['UNKNOWN', 'VAGUE', 'NONE', 'INCLUDES', 'IS_INCLUDED', 'DURING', 'DURING_INV',
    'BEGINS', 'BEGUN_BY', 'ENDS', 'ENDED_BY']);
  const clone = value => JSON.parse(JSON.stringify(value));
  function freeze(value) {
    if (value && typeof value === 'object' && !Object.isFrozen(value)) {
      Object.freeze(value); Object.values(value).forEach(freeze);
    }
    return value;
  }
  function signature(value) {
    const text = JSON.stringify(value); let a = 2166136261, b = 5381;
    for (let i = 0; i < text.length; i++) { a = Math.imul(a ^ text.charCodeAt(i), 16777619); b = Math.imul(b, 33) ^ text.charCodeAt(i); }
    return `${(a >>> 0).toString(16)}-${(b >>> 0).toString(16)}-${text.length}`;
  }
  function relation(item, provenance) {
    return { from: item.from_id, to: item.to_id, type: String(item.rel || item.rel_type || 'UNKNOWN').trim().toUpperCase(),
      provenance: { source: item.source || item.origin || provenance, cue: item.cue || null,
        evidence: item.evidence || null, id: item.lid || item.id || null }, projection: Boolean(item.projection) };
  }
  const isAccepted = item => Boolean(item) && item.accepted !== false
    && !['rejected', 'candidate', 'unresolved', 'pending', 'proposed', 'unknown'].includes(String(item.status || '').trim().toLowerCase());
  function normalized(item) {
    if (item.directionNormalized) return item;
    return ['AFTER', 'IAFTER', 'IMMEDIATELY_AFTER'].includes(item.type)
      ? { ...item, directionNormalized: true, originalFrom: item.from, originalTo: item.to, from: item.to, to: item.from } : item;
  }
  const diagnostic = (code, message, ids, relations = []) => ({ code, message, segmentIds: [...new Set(ids)], relations });
  // Compile once. Validation of any complete permutation is O(TS + accepted edges).
  function graph(ids, relations, mapId = id => [id]) {
    const parent = new Map(ids.map(id => [id, id]));
    function find(id) { let current = id; while (parent.get(current) !== current) current = parent.get(current);
      while (parent.get(id) !== id) { const next = parent.get(id); parent.set(id, current); id = next; } return current; }
    const errors = [], warnings = [], usable = [];
    for (const item of relations) {
      if (!parent.has(item.from) || !parent.has(item.to)) {
        errors.push(diagnostic('missing-endpoint', 'Accepted relationship references an unavailable endpoint.',
          [...mapId(item.from), ...mapId(item.to)], [item])); continue;
      }
      usable.push(item);
      if (equal.has(item.type)) parent.set(find(item.to), find(item.from));
      if (!strict.has(item.type) && !equal.has(item.type) && !overlap.has(item.type) && !passive.has(item.type))
        warnings.push(diagnostic('unsupported-relation', 'Relationship retained without inventing strict precedence.', [...mapId(item.from), ...mapId(item.to)], [item]));
    }
    const groups = [...new Set(ids.map(find))], edges = usable.filter(item => strict.has(item.type)).map(normalized);
    const adjacency = new Map(groups.map(id => [id, []])), reverse = new Map(groups.map(id => [id, []]));
    for (const edge of edges) { adjacency.get(find(edge.from)).push(edge); reverse.get(find(edge.to)).push(edge); }
    // Iterative SCC traversal: long documents cannot overflow the JavaScript call stack.
    const seen = new Set(), finished = [];
    for (const id of groups) {
      const stack = [[id, false]];
      while (stack.length) {
        const [node, done] = stack.pop();
        if (done) { finished.push(node); continue; }
        if (seen.has(node)) continue; seen.add(node); stack.push([node, true]);
        for (const edge of adjacency.get(node)) if (!seen.has(find(edge.to))) stack.push([find(edge.to), false]);
      }
    }
    const components = new Map(); let number = 0;
    for (const id of finished.reverse()) {
      if (components.has(id)) continue;
      const stack = [id];
      while (stack.length) { const node = stack.pop(); if (components.has(node)) continue; components.set(node, number);
        for (const edge of reverse.get(node)) stack.push(find(edge.from)); }
      number++;
    }
    const cyclic = edges.filter(edge => components.get(find(edge.from)) === components.get(find(edge.to)));
    if (cyclic.length) errors.push(diagnostic('cycle', 'Accepted precedence is contradictory; no arrangement can satisfy it.',
      cyclic.flatMap(edge => [...mapId(edge.from), ...mapId(edge.to)]), [...cyclic, ...usable.filter(item => equal.has(item.type))]));
    function path(from, to) {
      if (!parent.has(from) || !parent.has(to)) return null;
      const start = find(from), end = find(to), previous = new Map([[start, null]]), queue = [start];
      for (let i = 0; i < queue.length; i++) {
        const node = queue[i]; if (node === end) break;
        for (const edge of adjacency.get(node)) { const next = find(edge.to); if (!previous.has(next)) { previous.set(next, edge); queue.push(next); } }
      }
      if (!previous.has(end)) return null;
      const result = []; let current = end;
      while (current !== start) { const edge = previous.get(current); result.push(edge); current = find(edge.from); }
      return result.reverse();
    }
    for (const item of usable.filter(item => overlap.has(item.type) && !item.projection)) {
      const chain = path(item.from, item.to) || path(item.to, item.from);
      if (chain?.length) errors.push(diagnostic('conflicting-relations', 'Accepted overlap conflicts with strict precedence.',
        [...mapId(item.from), ...mapId(item.to), ...chain.flatMap(edge => [...mapId(edge.from), ...mapId(edge.to)])], [item, ...chain]));
    }
    // Equality represents tied evidence, not two blocks occupying one layout slot.
    const members = new Map(groups.map(id => [id, []])); ids.forEach(id => members.get(find(id)).push(id));
    const expanded = edges.flatMap(edge => members.get(find(edge.from)).flatMap(from => members.get(find(edge.to)).map(to => ({ ...edge, from, to }))));
    return { edges: expanded, errors, warnings, path };
  }
  function createModel(diagram) {
    const data = freeze(clone(diagram || {})), segments = data.segments || [];
    const original = segments.map((segment, index) => ({ segment, index })).sort((a, b) =>
      (a.segment.text_order ?? a.index + 1) - (b.segment.text_order ?? b.index + 1) || a.index - b.index).map(item => item.segment.id);
    const byId = Object.fromEntries(segments.map(item => [item.id, item]));
    const eventSegment = new Map(), identityErrors = [];
    segments.forEach(segment => (segment.event_ids || []).forEach(id => {
      if (eventSegment.has(id) && eventSegment.get(id) !== segment.id)
        identityErrors.push(diagnostic('ambiguous-event-membership', `Event ${id} belongs to more than one TS; its temporal evidence cannot be projected safely.`, [eventSegment.get(id), segment.id]));
      else eventSegment.set(id, segment.id);
    }));
    const eventIds = [...new Set([...(data.events || []).map(item => item.id), ...eventSegment.keys()])];
    const eventRelations = (data.temporal_ordering?.constraints || []).filter(isAccepted).map(item => relation(item, 'temporal_ordering.constraints'));
    const instances = new Map((data.time_ml?.make_instances || []).map(item => [item.eiid, item.tym_event_id]));
    for (const item of data.time_ml?.tlinks || []) {
      if (!item.event_instance_id || !item.related_to_event_instance || !isAccepted(item)) continue; // Anchors are not precedence.
      eventRelations.push(relation({ ...item, from_id: instances.get(item.event_instance_id) || item.event_instance_id,
        to_id: instances.get(item.related_to_event_instance) || item.related_to_event_instance }, 'time_ml.tlinks'));
    }
    for (const item of eventRelations) if (strict.has(item.type) && (!eventSegment.has(item.from) || !eventSegment.has(item.to)))
      identityErrors.push(diagnostic('missing-event-membership', 'Accepted event precedence cannot be projected because its TS membership is unavailable.',
        [eventSegment.get(item.from) || item.from, eventSegment.get(item.to) || item.to], [item]));
    const eventGraph = graph(eventIds, eventRelations, id => eventSegment.has(id) ? [eventSegment.get(id)] : [id]);
    const project = item => ({ ...item, eventIds: [item.from, item.to],
      from: eventSegment.get(item.from) || item.from, to: eventSegment.get(item.to) || item.to, projection: true });
    const crossesSegments = item => eventSegment.get(item.from) !== eventSegment.get(item.to);
    const projected = eventGraph.edges.filter(crossesSegments).map(project);
    const segmentRelations = (data.relations || []).filter(isAccepted).map(item => ({ ...relation(item, 'diagram.relations'),
      projection: Boolean(data.temporal_ordering && /projected onto TS spans/.test(item.evidence || '')) }));
    const layoutRelations = [...projected, ...segmentRelations];
    // Nonstrict event evidence remains inspectable. It is not a whole-TS interval
    // assertion: two simultaneous events must not merge their entire TS blocks.
    const relations = [...layoutRelations, ...eventRelations.filter(item => !strict.has(item.type) && crossesSegments(item)).map(project)];
    const compiled = graph(original, layoutRelations);
    const errors = [...identityErrors, ...eventGraph.errors, ...compiled.errors];
    if (new Set(original).size !== original.length || original.some(id => typeof id !== 'string' || !id))
      errors.push(diagnostic('invalid-identity', 'Every TS must have a unique stable ID.', original));
    const model = { data, original: freeze(original), byId: freeze(byId), relations: freeze(relations), eventRelations: freeze(eventRelations), edges: freeze(compiled.edges),
      errors: freeze(errors), warnings: freeze([...eventGraph.warnings, ...compiled.warnings]), path: compiled.path,
      documentFingerprint: signature(segments.map(item => [item.id, item.text_order, item.span_start, item.span_end, item.text, item.event_ids])),
      membershipFingerprint: signature(segments.map(item => [item.id, item.track_id])),
      constraintFingerprint: signature([eventRelations, segmentRelations]),
      withheld: data.temporal_ordering?.conflicts || [] };
    return Object.freeze(model);
  }
  function validate(model, order) {
    const diagnostics = [...model.errors];
    if (!Array.isArray(order) || order.length !== model.original.length || new Set(order).size !== order.length
      || Array.from(order).some(id => typeof id !== 'string' || !Object.hasOwn(model.byId, id)))
      diagnostics.push(diagnostic('invalid-permutation', 'Arrangement must contain every TS exactly once.', Array.isArray(order) ? order : []));
    if (diagnostics.length) return { valid: false, diagnostics, warnings: model.warnings };
    const positions = new Map(order.map((id, index) => [id, index]));
    for (const edge of model.edges) if (positions.get(edge.from) >= positions.get(edge.to))
      diagnostics.push(diagnostic('precedence-violation', `${edge.from} must precede ${edge.to} (${edge.type}).`, [edge.from, edge.to], [edge]));
    return { valid: diagnostics.length === 0, diagnostics, warnings: model.warnings };
  }
  function initialOrder(model) {
    const suggested = [...model.original].sort((a, b) => (model.byId[a].story_order ?? Infinity) - (model.byId[b].story_order ?? Infinity));
    if (validate(model, suggested).valid) return suggested;
    // Stable Kahn layout is only a feasible display preference, never new evidence.
    const adjacency = new Map(model.original.map(id => [id, new Set()])), degree = new Map(model.original.map(id => [id, 0]));
    for (const edge of model.edges) if (adjacency.has(edge.from) && degree.has(edge.to) && !adjacency.get(edge.from).has(edge.to)) {
      adjacency.get(edge.from).add(edge.to); degree.set(edge.to, degree.get(edge.to) + 1);
    }
    const queue = model.original.filter(id => degree.get(id) === 0), result = [];
    for (let i = 0; i < queue.length; i++) { const id = queue[i]; result.push(id);
      for (const next of adjacency.get(id)) { degree.set(next, degree.get(next) - 1); if (!degree.get(next)) queue.push(next); } }
    return result.length === model.original.length ? result : [...model.original];
  }
  function createState(model, documentKey = '') {
    const user = initialOrder(model);
    return freeze({ documentKey, original: [...model.original], user, suggested: null, selected: user[0] || null,
      groups: createGroups(user.length), past: [], future: [], validation: validate(model, user), notice: null });
  }
  // Fixed, contiguous display ranges. Membership is derived from layout position,
  // never stored on a TimeSegment or confused with its temporal/TT annotation.
  function createGroups(size) {
    const count = size >= 4 ? 4 : size ? 1 : 0;
    return freeze(Array.from({ length: count }, (_, i) => ({ id: 'ABCD'[i], start: Math.round(i * size / count) + 1, end: Math.round((i + 1) * size / count) })));
  }
  function validGroups(groups, size) {
    if (!Array.isArray(groups) || groups.length > 4 || (size > 0 && !groups.length)) return false;
    let end = 0; const seen = new Set();
    for (const group of groups) {
      if (!group || !'ABCD'.includes(group.id) || group.id.length !== 1 || seen.has(group.id) || !Number.isInteger(group.start)
        || !Number.isInteger(group.end) || group.start !== end + 1 || group.end < group.start || group.end > size) return false;
      seen.add(group.id); end = group.end;
    }
    return end === size;
  }
  const groupAt = (state, position) => state.groups.find(group => position >= group.start && position <= group.end);
  function previewGroupMove(model, state, id, position, groupId) {
    const group = state.groups.find(item => item.id === groupId);
    if (!group || !Number.isInteger(position) || position < group.start || position > group.end)
      return { order: state.user, valid: false, diagnostics: [diagnostic('display-group-violation',
        group ? `Choose a position in display group ${group.id} (${group.start}–${group.end}).` : 'Choose a display group.', [id])], warnings: [] };
    return previewMove(model, state, id, position);
  }
  function previewMove(model, state, id, position) {
    if (!Object.hasOwn(model.byId, id) || !Number.isInteger(position) || position < 1 || position > state.user.length)
      return { order: state.user, valid: false, diagnostics: [diagnostic('invalid-position', 'Choose a global layout position within the document.', [id])], warnings: [] };
    const order = state.user.filter(value => value !== id); order.splice(position - 1, 0, id);
    return { order, ...validate(model, order) };
  }
  function commit(model, state, order) {
    const validation = validate(model, order);
    if (!validation.valid) return freeze({ ...state, validation, notice: 'Arrangement rejected by temporal validation.' });
    if (order.every((id, i) => id === state.user[i])) return freeze({ ...state, validation, notice: null });
    return freeze({ ...state, user: [...order], past: [...state.past, state.user].slice(-100), future: [], validation, notice: null });
  }
  function transition(model, state, action) {
    switch (action.type) {
      case 'select': return Object.hasOwn(model.byId, action.id)
        ? freeze({ ...state, selected: action.id, validation: validate(model, state.user), notice: null }) : state;
      case 'move': { const preview = previewMove(model, state, action.id, action.position);
        return preview.valid ? commit(model, state, preview.order) : freeze({ ...state, validation: preview, notice: 'Movement rejected by temporal validation.' }); }
      case 'move-in-group': {
        const preview = previewGroupMove(model, state, action.id, action.position, action.groupId);
        return preview.valid ? commit(model, state, preview.order) : freeze({ ...state, validation: preview, notice: 'Movement rejected. Review the selected display group and temporal evidence.' });
      }
      case 'reset': return commit(model, state, state.original);
      case 'suggest': {
        const validation = validate(model, action.order);
        if (validation.diagnostics.some(item => item.code === 'invalid-permutation'))
          return freeze({ ...state, validation, notice: 'Algorithm suggestion must contain every TS exactly once.' });
        return freeze({ ...state, suggested: { order: [...action.order], algorithm: action.algorithm,
          validation, correctness: action.correctness } });
      }
      case 'apply': return state.suggested ? commit(model, state, state.suggested.order) : state;
      case 'undo': case 'redo': {
        const undo = action.type === 'undo', source = undo ? state.past : state.future;
        if (!source.length) return state;
        const order = source[source.length - 1], validation = validate(model, order);
        if (!validation.valid) return freeze({ ...state, validation, notice: 'History arrangement is no longer valid.' });
        return freeze({ ...state, user: order, past: undo ? state.past.slice(0, -1) : [...state.past, state.user].slice(-100),
          future: undo ? [...state.future, state.user].slice(-100) : state.future.slice(0, -1), validation, notice: null });
      }
      default: return state;
    }
  }
  const SCHEMA_VERSION = 2;
  // Callers supply a stable, unique source-document key. Anonymous callers stay
  // fingerprint-scoped so two unrelated documents cannot overwrite each other.
  const storageKey = (model, key) => `tym-ordering:v${SCHEMA_VERSION}:${signature(key)}:${key ? 'document' : model.documentFingerprint}`;
  const legacyStorageKey = (model, key) => `tym-ordering:v${SCHEMA_VERSION}:${signature(key)}:${model.documentFingerprint}`;
  function save(model, state, storage) {
    const validation = validate(model, state.user);
    if (!validation.valid) return { ok: false, code: 'invalid-arrangement', validation };
    const record = { schemaVersion: SCHEMA_VERSION, documentKey: state.documentKey, documentFingerprint: model.documentFingerprint,
      membershipFingerprint: model.membershipFingerprint, constraintFingerprint: model.constraintFingerprint,
      order: state.user, selected: state.selected, groups: state.groups };
    if (!validGroups(state.groups, state.user.length)) return { ok: false, code: 'invalid-saved-layout' };
    try { storage.setItem(storageKey(model, state.documentKey), JSON.stringify(record)); return { ok: true }; }
    catch { return { ok: false, code: 'storage-unavailable' }; }
  }
  function restore(model, state, storage) {
    let record, raw;
    try { raw = storage.getItem(storageKey(model, state.documentKey)) || (state.documentKey && storage.getItem(legacyStorageKey(model, state.documentKey)))
      || storage.getItem(`tym-ordering:v1:${signature(state.documentKey)}:${state.documentKey ? 'document' : model.documentFingerprint}`)
      || storage.getItem(`tym-ordering:v1:${signature(state.documentKey)}:${model.documentFingerprint}`); }
    catch { return { ok: false, code: 'storage-unavailable', state }; }
    if (!raw) return { ok: false, code: 'no-saved-layout', state };
    try { record = JSON.parse(raw); } catch { return { ok: false, code: 'invalid-saved-layout', state }; }
    if (![1, SCHEMA_VERSION].includes(record?.schemaVersion)) return { ok: false, code: 'unsupported-schema', state };
    if (record.documentKey !== state.documentKey || record.documentFingerprint !== model.documentFingerprint || record.membershipFingerprint !== model.membershipFingerprint)
      return { ok: false, code: 'stale-layout', state };
    if (record.constraintFingerprint !== model.constraintFingerprint) return { ok: false, code: 'changed-constraints', state };
    const validation = validate(model, record.order);
    if (!validation.valid) return { ok: false, code: 'invalid-saved-layout', validation, state };
    const groups = record.schemaVersion === 1 ? createGroups(model.original.length) : record.groups;
    if (!validGroups(groups, model.original.length)) return { ok: false, code: 'invalid-saved-layout', state };
    const restored = commit(model, state, record.order);
    return { ok: true, state: freeze({ ...restored, groups: clone(groups), selected: Object.hasOwn(model.byId, record.selected) ? record.selected : restored.selected }) };
  }
  return { createModel, validate, signature, freeze, isAccepted, createState, previewMove, createGroups, groupAt, previewGroupMove, transition, save, restore, storageKey, SCHEMA_VERSION };
});
