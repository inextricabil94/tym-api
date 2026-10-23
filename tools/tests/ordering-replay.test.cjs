const test = require('node:test');
const assert = require('node:assert/strict');
const S = require('../../ui/Tym.Ui/wwwroot/ordering-sort.js');
const P = require('../../ui/Tym.Ui/wwwroot/ordering-replay.js');
test('play pause previous next restart scrubbing and boundary behavior are deterministic', () => {
  const run = S.run('heap', ['A', 'B', 'C'], { A: 3, B: 1, C: 2 });
  let state = P.transition(P.create(), { type: 'load', run });
  state = P.transition(state, { type: 'play' }); const epoch = state.epoch;
  state = P.transition(state, { type: 'tick', epoch }); assert.equal(state.step, 1);
  state = P.transition(state, { type: 'pause' }); assert.equal(P.transition(state, { type: 'tick', epoch }).step, 1);
  state = P.transition(state, { type: 'next' }); assert.equal(state.step, 2);
  state = P.transition(state, { type: 'previous' }); assert.equal(state.step, 1);
  state = P.transition(state, { type: 'seek', step: 999 }); assert.equal(state.step, run.operations.length);
  state = P.transition(state, { type: 'play' }); assert.equal(state.step, 0);
  for (let i = 0; i < run.operations.length; i++) state = P.transition(state, { type: 'tick', epoch: state.epoch });
  assert.equal(state.playing, false); assert.deepEqual(S.reconstruct(run, state.step).order, run.order);
  state = P.transition(state, { type: 'restart' }); assert.equal(state.step, 0);
});
test('cancellation invalidates pending ticks and replacement runs start from their own initial state', () => {
  const run = S.run('bubble', ['A', 'B'], { A: 2, B: 1 });
  let state = P.transition(P.create(), { type: 'load', run }); state = P.transition(state, { type: 'play' }); const epoch = state.epoch;
  state = P.transition(state, { type: 'cancel' }); assert.equal(state.run, null);
  assert.equal(P.transition(state, { type: 'tick', epoch }), state);
  state = P.transition(state, { type: 'load', run }); assert.equal(state.step, 0); assert.equal(state.playing, false);
  state = P.transition(state, { type: 'speed', value: 100 }); assert.equal(state.speed, 60);
});
test('all ten trace families reconstruct metadata and changes consistently across checkpoints', () => {
  for (const { id } of S.catalog) {
    const order = Array.from({ length: 32 }, (_, i) => `S${i}`), keys = Object.fromEntries(order.map((id, i) => [id, 31 - i]));
    const run = S.run(id, order, keys), frame = { order: [...order], visual: {}, operation: null };
    for (let i = 0; i < run.operations.length; i++) {
      S.applyOperation(frame, run.operations[i]);
      if (i % 71 === 0 || i === run.operations.length - 1) {
        const reconstructed = S.reconstruct(run, i + 1); assert.deepEqual(reconstructed.order, frame.order); assert.deepEqual(reconstructed.visual, frame.visual);
      }
    }
  }
});
