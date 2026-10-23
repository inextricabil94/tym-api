const test = require('node:test');
const assert = require('node:assert/strict');
const O = require('../../ui/Tym.Ui/wwwroot/ordering-core.js');
const fixture = relations => ({ segments: ['A', 'B', 'C', 'D'].map((id, i) => ({ id, track_id: `TT${i % 2}`, text_order: i + 1, event_ids: [`E${id}`] })), relations });
const rel = (from_id, rel, to_id) => ({ from_id, rel, to_id, source: 'authored', evidence: 'Source evidence', cue: 'then' });
test('valid moves and unknown/overlap/containment never invent strict precedence', () => {
  for (const type of ['UNKNOWN', 'VAGUE', 'OVERLAPS', 'SIMULTANEOUS', 'IS_INCLUDED', 'INCLUDES', 'BEGINS', 'ENDS']) {
    const model = O.createModel(fixture([rel('A', type, 'B')]));
    assert.equal(O.validate(model, ['D', 'C', 'B', 'A']).valid, true, type);
  }
});
test('normalizes AFTER and immediate directions; validates the entire permutation', () => {
  for (const [from, type, to] of [['A', 'BEFORE', 'B'], ['B', 'AFTER', 'A'], ['A', 'IBEFORE', 'B'], ['B', 'IAFTER', 'A']]) {
    const model = O.createModel(fixture([rel(from, type, to)]));
    assert.equal(O.validate(model, ['C', 'A', 'D', 'B']).valid, true);
    const result = O.validate(model, ['B', 'A', 'C', 'D']);
    assert.equal(result.valid, false); assert.equal(result.diagnostics[0].relations[0].provenance.evidence, 'Source evidence');
  }
});
test('transitive paths retain complete provenance and prevent transitive violations', () => {
  const model = O.createModel(fixture([rel('A', 'BEFORE', 'B'), rel('B', 'BEFORE', 'C')]));
  assert.equal(model.path('A', 'C').length, 2);
  assert.equal(O.validate(model, ['C', 'A', 'B', 'D']).valid, false);
});
test('AFTER direction is normalized exactly once across event-to-TS and TimeML projections', () => {
  for (const type of ['AFTER', 'IAFTER', 'IMMEDIATELY_AFTER']) {
    for (const timeml of [false, true]) {
      const diagram = fixture([]);
      if (timeml) diagram.time_ml = { make_instances: [{ eiid: 'iA', tym_event_id: 'EA' }, { eiid: 'iB', tym_event_id: 'EB' }],
        tlinks: [{ event_instance_id: 'iB', related_to_event_instance: 'iA', rel_type: type }] };
      else diagram.temporal_ordering = { constraints: [rel('EB', type, 'EA')] };
      const model = O.createModel(diagram);
      assert.equal(O.validate(model, ['A', 'B', 'C', 'D']).valid, true, `${type} ${timeml}`);
      assert.equal(O.validate(model, ['B', 'A', 'C', 'D']).valid, false, `${type} ${timeml}`);
    }
  }
});
test('accepted precedence with no TS ownership is diagnosed rather than silently dropped', () => {
  const diagram = fixture([]); diagram.events = [{ id: 'missing' }];
  diagram.temporal_ordering = { constraints: [rel('EA', 'BEFORE', 'missing')] };
  assert.ok(O.createModel(diagram).errors.some(item => item.code === 'missing-event-membership'));
});
test('detects cycles, simultaneous/strict conflicts and transitive overlap conflicts', () => {
  for (const relations of [ [rel('A', 'BEFORE', 'B'), rel('B', 'BEFORE', 'A')],
    [rel('A', 'SIMULTANEOUS', 'B'), rel('A', 'BEFORE', 'B')],
    [rel('A', 'BEFORE', 'B'), rel('B', 'BEFORE', 'C'), rel('A', 'OVERLAPS', 'C')] ]) {
    const model = O.createModel(fixture(relations)); assert.ok(model.errors.length); assert.equal(O.validate(model, model.original).valid, false);
  }
});
test('projects accepted event evidence and TimeML event instances without ordering time anchors or internal TS events', () => {
  const diagram = fixture([]);
  diagram.temporal_ordering = { constraints: [rel('EA', 'BEFORE', 'EB')], conflicts: [{ constraint: rel('EC', 'BEFORE', 'EA') }] };
  diagram.time_ml = { make_instances: [{ eiid: 'iB', tym_event_id: 'EB' }, { eiid: 'iC', tym_event_id: 'EC' }],
    tlinks: [{ event_instance_id: 'iB', related_to_event_instance: 'iC', rel_type: 'BEFORE', lid: 'l1', origin: 'accepted' },
      { event_instance_id: 'iC', related_to_time: 't1', rel_type: 'IS_INCLUDED' }] };
  const before = JSON.stringify(diagram), model = O.createModel(diagram);
  assert.equal(model.path('A', 'C').length, 2); assert.equal(model.withheld.length, 1);
  assert.equal(O.validate(model, ['A', 'B', 'C', 'D']).valid, true);
  assert.equal(O.validate(model, ['C', 'A', 'B', 'D']).valid, false);
  assert.equal(JSON.stringify(diagram), before);
});
test('rejects incomplete permutations, missing endpoints and contradictory accepted projections', () => {
  const model = O.createModel(fixture([]));
  for (const order of [[], ['A', 'A', 'C', 'D'], ['A', 'B', 'C', 'Z']]) assert.equal(O.validate(model, order).valid, false);
  assert.equal(O.validate(O.createModel(fixture([rel('A', 'BEFORE', 'Z')])), model.original).valid, false);
  const diagram = fixture([]); diagram.temporal_ordering = { constraints: [rel('EA', 'BEFORE', 'EB'), rel('EB', 'BEFORE', 'EA')] };
  assert.ok(O.createModel(diagram).errors.some(item => item.code === 'cycle'));
});

test('malformed permutations, including sparse arrays, return diagnostics without throwing', () => {
  const model = O.createModel(fixture([]));
  const sparse = ['A', 'B', 'C', 'D']; delete sparse[1];
  for (const order of [null, {}, 'ABCD', sparse, ['A', new String('B'), 'C', 'D']]) {
    const result = O.validate(model, order);
    assert.equal(result.valid, false);
    assert.ok(result.diagnostics.some(item => item.code === 'invalid-permutation'));
  }
});

test('explicit unaccepted statuses never become strict evidence, with canonical relation types', () => {
  for (const patch of [{ accepted: false }, { status: 'REJECTED' }, { status: ' Candidate ' }, { status: 'Unresolved' }, { status: 'pending' }]) {
    const model = O.createModel(fixture([{ ...rel('A', 'BEFORE', 'B'), ...patch }]));
    assert.equal(O.validate(model, ['D', 'C', 'B', 'A']).valid, true);
    assert.equal(model.relations.length, 0);
  }
  const model = O.createModel(fixture([rel('B', ' after ', 'A')]));
  const result = O.validate(model, ['D', 'C', 'B', 'A']);
  assert.equal(result.valid, false);
  assert.equal(result.diagnostics[0].relations[0].originalFrom, 'B');
  assert.equal(result.diagnostics[0].relations[0].originalTo, 'A');
});

test('event evidence stays inspectable without promoting simultaneous events to whole-segment equality', () => {
  const diagram = fixture([]); diagram.segments[0].event_ids.push('EA2');
  diagram.temporal_ordering = { constraints: [rel('EA', 'SIMULTANEOUS', 'EB'), rel('EA2', 'BEFORE', 'EC'), rel('EA', 'UNKNOWN', 'ED')] };
  const model = O.createModel(diagram);
  assert.ok(model.relations.some(item => item.from === 'A' && item.to === 'B' && item.type === 'SIMULTANEOUS' && item.projection));
  assert.ok(model.relations.some(item => item.type === 'UNKNOWN'));
  assert.equal(model.eventRelations.length, 3);
  assert.equal(O.validate(model, ['A', 'C', 'B', 'D']).valid, true);
  assert.equal(O.validate(model, ['C', 'A', 'B', 'D']).valid, false);
});

test('internal segment precedence is retained as evidence and ambiguous event membership blocks projection', () => {
  const diagram = fixture([]); diagram.segments[0].event_ids.push('EA2');
  diagram.temporal_ordering = { constraints: [rel('EA', 'BEFORE', 'EA2')] };
  const model = O.createModel(diagram);
  assert.equal(model.eventRelations.length, 1);
  assert.equal(model.edges.length, 0);
  assert.equal(O.validate(model, ['D', 'C', 'B', 'A']).valid, true);
  diagram.segments[1].event_ids.push('EA');
  const ambiguous = O.createModel(diagram);
  assert.ok(ambiguous.errors.some(item => item.code === 'ambiguous-event-membership'));
  assert.equal(O.validate(ambiguous, ambiguous.original).valid, false);
});
test('simultaneous evidence propagates precedence without imposing tied layout slots', () => {
  const model = O.createModel(fixture([rel('A', 'SIMULTANEOUS', 'B'), rel('A', 'BEFORE', 'C')]));
  assert.equal(O.validate(model, ['B', 'A', 'C', 'D']).valid, true);
  assert.equal(O.validate(model, ['A', 'C', 'B', 'D']).valid, false);
});
test('20,000 TS chain compiles iteratively and validates linearly', () => {
  const segments = Array.from({ length: 20000 }, (_, i) => ({ id: `S${i}` }));
  const model = O.createModel({ segments, relations: segments.slice(1).map((s, i) => rel(`S${i}`, 'BEFORE', s.id)) });
  assert.equal(O.validate(model, model.original).valid, true);
  assert.equal(O.validate(model, model.original.toReversed()).valid, false);
});
