const test = require('node:test');
const assert = require('node:assert/strict');
const O = require('../../ui/Tym.Ui/wwwroot/ordering-core.js');
const fixture = () => ({ segments: ['A', 'B', 'C'].map((id, i) => ({ id, text: id, track_id: `TT${i % 2}`, text_order: i + 1, story_order: i + 1, event_ids: [`E${id}`] })),
  relations: [{ from_id: 'A', rel: 'BEFORE', to_id: 'C', evidence: 'then' }], time_ml: { events: [{ eid: 'e1' }] } });
const memory = () => { const data = new Map(); return { getItem: key => data.get(key), setItem: (key, value) => data.set(key, value) }; };
test('preview, movement, independent selection, identity and annotations are immutable', () => {
  const diagram = fixture(), before = JSON.stringify(diagram), model = O.createModel(diagram), state = O.createState(model, 'doc');
  const selected = O.transition(model, state, { type: 'select', id: 'B' });
  assert.deepEqual(O.previewMove(model, selected, 'B', 1).order, ['B', 'A', 'C']); assert.deepEqual(state.user, ['A', 'B', 'C']);
  const moved = O.transition(model, selected, { type: 'move', id: 'B', position: 1 });
  assert.equal(moved.selected, 'B'); assert.equal(model.byId.B.track_id, 'TT1'); assert.deepEqual(moved.original, ['A', 'B', 'C']);
  assert.equal(JSON.stringify(diagram), before); assert.ok(Object.isFrozen(moved.user));
  const invalid = O.transition(model, moved, { type: 'move', id: 'C', position: 1 });
  assert.deepEqual(invalid.user, moved.user); assert.equal(invalid.validation.valid, false); assert.equal(invalid.past.length, moved.past.length);
  for (const position of [0, 4, 1.5, NaN]) assert.equal(O.previewMove(model, moved, 'A', position).valid, false);
});
test('undo redo reset and branch changes preserve history semantics', () => {
  const model = O.createModel(fixture()); let state = O.createState(model);
  state = O.transition(model, state, { type: 'move', id: 'B', position: 1 });
  state = O.transition(model, state, { type: 'reset' }); assert.deepEqual(state.user, state.original);
  state = O.transition(model, state, { type: 'undo' }); assert.deepEqual(state.user, ['B', 'A', 'C']);
  state = O.transition(model, state, { type: 'redo' }); assert.deepEqual(state.user, state.original);
  state = O.transition(model, state, { type: 'undo' });
  state = O.transition(model, state, { type: 'move', id: 'B', position: 3 }); assert.equal(state.future.length, 0);
  assert.deepEqual(O.transition(model, state, { type: 'redo' }).user, state.user);
});
test('algorithm suggestion never changes user order; explicit application is validated', () => {
  const model = O.createModel(fixture()), state = O.createState(model);
  const suggested = O.transition(model, state, { type: 'suggest', order: ['C', 'B', 'A'], algorithm: 'quick', correctness: true });
  assert.deepEqual(suggested.user, state.user); assert.equal(suggested.suggested.validation.valid, false);
  assert.deepEqual(O.transition(model, suggested, { type: 'apply' }).user, state.user);
});

test('malformed algorithm outputs are rejected without replacing a previous suggestion or history', () => {
  const model = O.createModel(fixture()), initial = O.createState(model);
  const state = O.transition(model, initial, { type: 'suggest', order: ['B', 'A', 'C'], algorithm: 'merge', correctness: true });
  const sparse = ['A', 'B', 'C']; delete sparse[1];
  for (const order of [undefined, null, sparse, ['A', 'A', 'C']]) {
    const rejected = O.transition(model, state, { type: 'suggest', order, algorithm: 'bad' });
    assert.equal(rejected.validation.valid, false);
    assert.equal(rejected.suggested, state.suggested);
    assert.deepEqual(rejected.user, state.user);
    assert.deepEqual(rejected.past, state.past);
  }
});

test('selection clears rejected-proposal feedback and reports the committed arrangement validity', () => {
  const model = O.createModel(fixture()), initial = O.createState(model);
  const rejected = O.transition(model, initial, { type: 'move', id: 'C', position: 1 });
  assert.equal(rejected.validation.valid, false);
  const selected = O.transition(model, rejected, { type: 'select', id: 'B' });
  assert.equal(selected.validation.valid, true);
  assert.equal(selected.notice, null);
  assert.deepEqual(selected.user, initial.user);
  assert.deepEqual(selected.past, initial.past);
});
test('per-document save/restore is versioned, undoable and rejects tampering', () => {
  const model = O.createModel(fixture()), storage = memory(), initial = O.createState(model, 'doc');
  const moved = O.transition(model, initial, { type: 'move', id: 'B', position: 1 });
  assert.equal(O.save(model, moved, storage).ok, true);
  const restored = O.restore(model, initial, storage); assert.equal(restored.ok, true); assert.deepEqual(restored.state.user, moved.user);
  assert.deepEqual(O.transition(model, restored.state, { type: 'undo' }).user, initial.user);
  assert.equal(O.restore(model, O.createState(model, 'other'), storage).code, 'no-saved-layout');
  const key = O.storageKey(model, 'doc'), record = JSON.parse(storage.getItem(key));
  for (const [patch, code] of [[{ schemaVersion: 99 }, 'unsupported-schema'], [{ membershipFingerprint: 'old' }, 'stale-layout'],
    [{ constraintFingerprint: 'old' }, 'changed-constraints'], [{ order: ['C', 'B', 'A'] }, 'invalid-saved-layout']]) {
    storage.setItem(key, JSON.stringify({ ...record, ...patch })); assert.equal(O.restore(model, initial, storage).code, code);
  }
});
test('detects changed constraints and TT membership; catches denied browser storage', () => {
  const diagram = fixture(), model = O.createModel(diagram), storage = memory(), state = O.createState(model, 'doc'); O.save(model, state, storage);
  diagram.relations.push({ from_id: 'B', rel: 'BEFORE', to_id: 'C' });
  const changed = O.createModel(diagram); assert.equal(O.restore(changed, O.createState(changed, 'doc'), storage).code, 'changed-constraints');
  diagram.relations.pop(); diagram.segments[0].track_id = 'TT99'; const membership = O.createModel(diagram);
  assert.equal(O.restore(membership, O.createState(membership, 'doc'), storage).code, 'stale-layout');
  assert.equal(O.save(model, state, { setItem() { throw Error(); } }).code, 'storage-unavailable');
});

test('stable document keys detect changed source layouts and retain legacy saved arrangements', () => {
  const diagram = fixture(), model = O.createModel(diagram), storage = memory(), initial = O.createState(model, 'source-document-1');
  O.save(model, initial, storage);
  diagram.segments[0].text = 'Updated source';
  const changed = O.createModel(diagram);
  assert.equal(O.restore(changed, O.createState(changed, initial.documentKey), storage).code, 'stale-layout');
  // Earlier workbench saves included the full fingerprint in the key.
  const record = storage.getItem(O.storageKey(model, initial.documentKey)), legacy = memory();
  legacy.setItem(`tym-ordering:v${O.SCHEMA_VERSION}:${O.signature(initial.documentKey)}:${model.documentFingerprint}`, record);
  assert.equal(O.restore(model, initial, legacy).ok, true);
  assert.notEqual(O.storageKey(model, ''), O.storageKey(changed, ''));
  assert.notEqual(O.storageKey(model, 'source-document-1'), O.storageKey(model, 'source-document-2'));
});

test('corrupt saved JSON is distinguished from unavailable browser storage', () => {
  const model = O.createModel(fixture()), state = O.createState(model, 'doc'), storage = memory();
  storage.setItem(O.storageKey(model, state.documentKey), '{not valid json');
  assert.equal(O.restore(model, state, storage).code, 'invalid-saved-layout');
  assert.equal(O.restore(model, state, { getItem() { throw Error('denied'); } }).code, 'storage-unavailable');
});

test('history is bounded and revalidated against changed accepted evidence', () => {
  const diagram = fixture(), model = O.createModel(diagram); let state = O.createState(model);
  for (let i = 0; i < 125; i++) state = O.transition(model, state, { type: 'move', id: 'B', position: i % 2 ? 2 : 1 });
  assert.equal(state.past.length, 100);
  diagram.relations.push({ from_id: 'B', rel: 'BEFORE', to_id: 'A' });
  const changed = O.createModel(diagram), undo = O.transition(changed, state, { type: 'undo' });
  assert.deepEqual(undo.user, state.user);
  assert.equal(undo.validation.valid, false);
  assert.equal(undo.past.length, state.past.length);
});
test('original may contradict accepted flashbacks; initial feasible layout and guarded reset report this clearly', () => {
  const diagram = fixture(); diagram.relations = [{ from_id: 'C', rel: 'BEFORE', to_id: 'A' }];
  const model = O.createModel(diagram), state = O.createState(model);
  assert.deepEqual(state.original, ['A', 'B', 'C']); assert.equal(state.validation.valid, true);
  const reset = O.transition(model, state, { type: 'reset' }); assert.equal(reset.validation.valid, false); assert.deepEqual(reset.user, state.user);
  diagram.relations.push({ from_id: 'A', rel: 'BEFORE', to_id: 'C' });
  const cyclic = O.createModel(diagram), blocked = O.createState(cyclic); assert.equal(blocked.validation.valid, false);
  assert.deepEqual(O.transition(cyclic, blocked, { type: 'move', id: 'B', position: 1 }).user, blocked.user);
});
