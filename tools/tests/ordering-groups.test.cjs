const test = require('node:test'), assert = require('node:assert/strict');
const O = require('../../ui/Tym.Ui/wwwroot/ordering-core.js');
const fixture = () => ({ segments: Array.from({ length: 10 }, (_, i) => ({ id: `TS${i + 1}`, track_id: `TT${i % 3}`, text_order: i + 1 })),
  relations: [{ from_id: 'TS4', rel: 'BEFORE', to_id: 'TS7', evidence: 'accepted source' }] });
test('display ranges cover every position, including small/empty/large documents', () => {
  for (const count of [0, 1, 3, 4, 10, 1003]) {
    const groups = O.createGroups(count); assert.equal(groups.reduce((sum, g) => sum + g.end - g.start + 1, 0), count);
    assert.deepEqual(groups.flatMap(g => Array.from({ length: g.end - g.start + 1 }, (_, i) => g.start + i)), Array.from({ length: count }, (_, i) => i + 1));
  }
  assert.deepEqual(O.createGroups(10).map(g => [g.id, g.start, g.end]), [['A', 1, 3], ['B', 4, 5], ['C', 6, 8], ['D', 9, 10]]);
});
test('group movement validates range and entire temporal permutation without mutating annotations', () => {
  const diagram = fixture(), before = JSON.stringify(diagram), model = O.createModel(diagram), initial = O.createState(model, 'doc');
  assert.equal(O.previewGroupMove(model, initial, 'TS7', 5, 'C').diagnostics[0].code, 'display-group-violation');
  assert.equal(O.previewGroupMove(model, initial, 'TS7', 3, 'A').valid, false);
  const moved = O.transition(model, initial, { type: 'move-in-group', id: 'TS7', position: 5, groupId: 'B' });
  assert.equal(moved.user[4], 'TS7'); assert.equal(O.groupAt(moved, 5).id, 'B');
  assert.equal(model.byId.TS7.track_id, 'TT0'); assert.equal(JSON.stringify(diagram), before); assert.deepEqual(moved.original, initial.original);
  assert.deepEqual(O.transition(model, moved, { type: 'undo' }).user, initial.user);
  const redo = O.transition(model, O.transition(model, moved, { type: 'undo' }), { type: 'redo' }); assert.deepEqual(redo.user, moved.user);
  const rejected = O.transition(model, initial, { type: 'move-in-group', id: 'TS7', position: 3, groupId: 'A' });
  assert.deepEqual(rejected.user, initial.user); assert.equal(rejected.past.length, 0);
});
test('v2 groups persist, corrupt ranges are rejected, and v1 layouts migrate without annotation changes', () => {
  const model = O.createModel(fixture()), state = O.createState(model, 'doc'), values = new Map();
  const storage = { getItem: k => values.get(k), setItem: (k, v) => values.set(k, v) };
  assert.equal(O.save(model, state, storage).ok, true); const key = O.storageKey(model, 'doc'), record = JSON.parse(values.get(key));
  assert.deepEqual(O.restore(model, state, storage).state.groups, state.groups);
  storage.setItem(key, JSON.stringify({ ...record, groups: [{ id: 'A', start: 2, end: 10 }] }));
  assert.equal(O.restore(model, state, storage).code, 'invalid-saved-layout');
  values.clear(); storage.setItem(`tym-ordering:v1:${O.signature('doc')}:document`, JSON.stringify({ ...record, schemaVersion: 1, groups: undefined }));
  assert.deepEqual(O.restore(model, state, storage).state.groups, state.groups);
});
