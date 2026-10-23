const test = require('node:test');
const assert = require('node:assert/strict');
const S = require('../../ui/Tym.Ui/wwwroot/ordering-sort.js');
const O = require('../../ui/Tym.Ui/wwwroot/ordering-core.js');
const sort = (id, values, options) => { const order = values.map((_, i) => `S${i}`), keys = Object.fromEntries(order.map((id, i) => [id, values[i]])); return S.run(id, order, keys, options); };
test('fallback catalog is exactly the requested ten algorithms', () => assert.deepEqual(S.catalog.map(item => item.id), ['bubble', 'selection', 'insertion', 'merge', 'quick', 'heap', 'shell', 'counting', 'radix', 'bucket']));
for (const { id } of S.catalog) {
  test(`${id}: empty, singleton, negative, duplicate, sorted, reverse and seeded random keys`, () => {
    let seed = 73; const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed % 41 - 20; };
    const cases = [[], [1], [0, -2, 5, -2, 1, 0], [1, 2, 3, 4], [5, 4, 3, 2, 1], Array.from({ length: 80 }, random)];
    if (!['counting', 'radix'].includes(id)) cases.push([1.5, -2.1, 1.5, 0.01], [-Number.MAX_VALUE, 0, Number.MAX_VALUE]);
    if (id === 'radix') cases.push([-Number.MAX_SAFE_INTEGER, Number.MAX_SAFE_INTEGER, 0]);
    for (const values of cases) {
      const run = sort(id, values), expected = values.map((key, rank) => ({ key, rank })).sort((a, b) => a.key < b.key ? -1 : a.key > b.key ? 1 : a.rank - b.rank).map(item => `S${item.rank}`);
      assert.deepEqual(run.order, expected); assert.equal(run.correctness, true);
      assert.deepEqual(S.reconstruct(run, run.operations.length).order, run.order);
      assert.equal(run.metrics.operationCount, run.operations.length); assert.equal(run.operations.at(-1).type, 'complete');
      for (const metric of ['comparisons', 'swaps', 'writes', 'movements'])
        assert.equal(run.metrics[metric], run.operations.reduce((total, op) => total + (op.cost[metric] || 0), 0), `${metric} is accounted for by trace costs`);
      assert.ok(run.metrics.computationTime >= 0); assert.ok(run.metrics.traceGenerationOverhead >= 0);
      assert.deepEqual(sort(id, values).operations, run.operations, 'traces are deterministic');
      const mutations = run.operations.filter(op => op.type === 'swap' || op.type === 'write');
      assert.equal(run.metrics.swaps, mutations.filter(op => op.type === 'swap').length);
      assert.ok(run.metrics.writes >= mutations.reduce((total, op) => total + op.changes.length, 0));
    }
  });
}
test('rejects unsupported, sparse and corrupt keys without mutating prepared input', () => {
  for (const value of [NaN, Infinity, '1', null]) assert.throws(() => S.run('quick', ['A'], { A: value }), /finite/);
  for (const id of ['counting', 'radix']) assert.throws(() => sort(id, [1.1, 2]), /integer/);
  assert.throws(() => sort('counting', [-1000000, 1000000]), /sparse/);
  assert.throws(() => S.run('missing', [], {}), /Unknown/);
  assert.throws(() => S.run('bubble', ['A', 'A'], { A: 1 }), /unique/);
  assert.throws(() => S.run('quick', Array(1), {}), error => error.code === 'identity');
  assert.throws(() => S.run('quick', Array(20001), {}), error => error.code === 'capacity');
  assert.equal(S.verify(Array(1), Array(1)), false);
  assert.equal(S.verify([{ id: 'A', key: 1, rank: 0 }], Array(1)), false);
  assert.throws(() => sort('bubble', [3, 2, 1], { maxOperations: 1 }), /operation limit/);
  assert.throws(() => sort('quick', [3, 2, 1], { signal: { aborted: true } }), /cancelled/);
});
test('checkpoint reconstruction matches sequential replay at every step and backwards', () => {
  const run = sort('merge', Array.from({ length: 70 }, (_, i) => 70 - i)), frame = { order: [...run.initial], visual: {}, operation: null };
  assert.ok(run.checkpoints.length > 1);
  for (let i = 0; i < run.operations.length; i++) { S.applyOperation(frame, run.operations[i]); assert.deepEqual(S.reconstruct(run, i + 1).order, frame.order); }
  assert.deepEqual(S.reconstruct(run, 0).order, run.initial);
  for (let step = run.operations.length; step >= 0; step -= 73) assert.equal(S.reconstruct(run, step).step, step);
});
test('numeric correctness and temporal compatibility are independent', () => {
  const run = sort('quick', [3, 1, 2]); assert.equal(run.correctness, true);
  const model = O.createModel({ segments: run.initial.map(id => ({ id })), relations: [{ from_id: 'S0', rel: 'BEFORE', to_id: 'S1' }] });
  assert.equal(O.validate(model, run.order).valid, false); assert.equal(O.validate(model, run.initial).valid, true);
});
test('instrumentation reports hand-counted bubble swaps/writes/movements', () => {
  const run = sort('bubble', [3, 2, 1]);
  assert.equal(run.metrics.comparisons, 3); assert.equal(run.metrics.swaps, 3); assert.equal(run.metrics.writes, 6); assert.equal(run.metrics.movements, 6);
});
test('distribution algorithms report each range comparison and count-table write', () => {
  const counting = sort('counting', [2, 1, 2]);
  assert.equal(counting.metrics.comparisons, 4);
  // Two initialized counts, three frequency writes, one prefix write, three decrements,
  // three output-buffer writes and three main-array writes.
  assert.equal(counting.metrics.writes, 15); assert.equal(counting.metrics.movements, 6);
  const radix = sort('radix', [-1, 0, 1]);
  assert.equal(radix.metrics.comparisons, 4); assert.equal(radix.metrics.writes, 6); assert.equal(radix.metrics.movements, 6);
  assert.equal(counting.operations.filter(op => op.type === 'compare').length, counting.metrics.comparisons);
  assert.equal(radix.operations.filter(op => op.type === 'compare').length, radix.metrics.comparisons);
});
test('bucket normalization handles subnormal numbers and both finite extremes', () => {
  for (const values of [[0, -Number.MIN_VALUE], [Number.MIN_VALUE * 2, 0, Number.MIN_VALUE], [-Number.MIN_VALUE, Number.MIN_VALUE, 0], [Number.MAX_VALUE, -Number.MAX_VALUE, 0]]) {
    const run = sort('bucket', values);
    assert.equal(run.correctness, true); assert.deepEqual(S.reconstruct(run, run.operations.length).order, run.order);
  }
});
test('prepared input and captured run keys remain independent from caller-owned data', () => {
  const order = Object.freeze(['A', 'B']), keys = { A: 2, B: 1 }, run = S.run('merge', order, keys);
  keys.A = -100;
  assert.deepEqual(order, ['A', 'B']); assert.deepEqual(run.keys, { A: 2, B: 1 });
  for (const value of [null, undefined, 'keys']) assert.throws(() => S.run('quick', ['A'], value), error => error.code === 'key');
});
test('large counting traces bound auxiliary previews and checkpoint storage', () => {
  const run = sort('counting', Array.from({ length: 800 }, (_, i) => Math.floor((799 - i) * 99999 / 799)));
  assert.equal(run.correctness, true); assert.ok(run.checkpointSlotBound <= 250000);
  for (const { frame } of run.checkpoints) {
    assert.ok(Object.keys(frame.visual.counts || {}).length <= run.previewLimit);
    assert.ok(Object.keys(frame.visual.buckets || {}).length <= run.previewLimit);
    assert.equal(frame.order.length, run.initial.length);
  }
  const frame = S.reconstruct(run, run.operations.length);
  assert.ok(Object.keys(frame.visual.counts).length <= run.previewLimit);
  assert.ok(Object.keys(frame.visual.buckets).length <= run.previewLimit);
  assert.deepEqual(frame.order, run.order);
  const lastCheckpoint = run.checkpoints.at(-1), sequential = JSON.parse(JSON.stringify(lastCheckpoint.frame));
  for (let i = lastCheckpoint.step; i < run.operations.length; i++) S.applyOperation(sequential, run.operations[i]);
  assert.deepEqual(frame.visual, sequential.visual);
});
