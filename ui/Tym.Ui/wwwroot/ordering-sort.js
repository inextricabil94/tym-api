(function (root, factory) {
  'use strict'; const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api; else root.TymSorting = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
  'use strict';
  const catalog = Object.freeze([
    ['bubble', 'Bubble Sort'], ['selection', 'Selection Sort'], ['insertion', 'Insertion Sort'], ['merge', 'Merge Sort'],
    ['quick', 'Quick Sort'], ['heap', 'Heap Sort'], ['shell', 'Shell Sort'], ['counting', 'Counting Sort'], ['radix', 'Radix Sort'], ['bucket', 'Bucket Sort']
  ].map(([id, label]) => Object.freeze({ id, label, keys: ['counting', 'radix'].includes(id) ? 'safe integers' : 'finite numbers' })));
  class SortError extends Error { constructor(code, message) { super(message); this.name = 'SortError'; this.code = code; } }
  const PREVIEW_LIMIT = 40, CHECKPOINT_SLOT_BUDGET = 250000;
  function prepare(order, keys) {
    if (!Array.isArray(order) || new Set(order).size !== order.length || Array.from(order).some(id => typeof id !== 'string')) throw new SortError('identity', 'Expected unique stable TS IDs.');
    if (!keys || typeof keys !== 'object') throw new SortError('key', 'Expected a numeric key map for the TS IDs.');
    return order.map((id, rank) => {
      const key = keys[id]; if (typeof key !== 'number' || !Number.isFinite(key)) throw new SortError('key', `A finite numeric key is required for ${id}.`);
      return { id, key, rank };
    });
  }
  function compatibility(algorithm, entries) {
    if (!catalog.some(item => item.id === algorithm)) return { ok: false, code: 'algorithm', message: 'Unknown sorting algorithm.' };
    if (entries.some(item => typeof item.key !== 'number' || !Number.isFinite(item.key))) return { ok: false, code: 'key', message: 'All keys must be finite numbers.' };
    if (['counting', 'radix'].includes(algorithm) && entries.some(item => !Number.isSafeInteger(item.key)))
      return { ok: false, code: 'integer-keys', message: 'Counting and Radix Sort require safe integer keys.' };
    if (algorithm === 'counting' && entries.length) {
      let min = Infinity, max = -Infinity; for (const item of entries) { min = Math.min(min, item.key); max = Math.max(max, item.key); }
      if (!Number.isSafeInteger(max - min) || max - min > 100000 || max - min > Math.max(1024, entries.length * 128))
        return { ok: false, code: 'sparse-range', message: 'Counting Sort key range is too sparse (maximum 100,001 counts). Choose another algorithm.' };
    }
    return { ok: true };
  }
  const now = () => typeof performance !== 'undefined' ? performance.now() : Date.now();
  function engine(algorithm, input, traced, maxOperations, signal) {
    const a = input.slice(), operations = [], metrics = { comparisons: 0, swaps: 0, writes: 0, movements: 0, auxiliaryMemory: 0, operationCount: 0 };
    const emit = (type, detail = {}, changes = [], cost = {}) => {
      if (signal?.aborted) throw new SortError('cancelled', 'Sorting cancelled.');
      if (++metrics.operationCount > maxOperations) throw new SortError('capacity', `Sorting exceeds the ${maxOperations.toLocaleString()} operation limit. Use a smaller input or a more efficient algorithm.`);
      for (const key of Object.keys(cost)) metrics[key] += cost[key];
      if (traced) operations.push({ type, ...detail, changes, cost });
    };
    const memory = slots => { metrics.auxiliaryMemory = Math.max(metrics.auxiliaryMemory, slots); };
    const cmp = (x, y) => { emit('compare', { ids: [x.id, y.id] }, [], { comparisons: 1 }); return x.key < y.key ? -1 : x.key > y.key ? 1 : x.rank - y.rank; };
    const rangeCompare = (item, value) => { emit('compare', { ids: [item.id], keys: [item.key, value], phase: 'key-range' }, [], { comparisons: 1 }); return item.key < value ? -1 : item.key > value ? 1 : 0; };
    const swap = (i, j) => { if (i === j) return; const ids = [a[i].id, a[j].id]; [a[i], a[j]] = [a[j], a[i]];
      emit('swap', { indices: [i, j], ids }, [[i, a[i].id], [j, a[j].id]], { swaps: 1, writes: 2, movements: 2 }); };
    const write = (i, item, detail = {}) => { a[i] = item; emit('write', { indices: [i], ids: [item.id], ...detail }, [[i, item.id]], { writes: 1, movements: 1 }); };
    const auxWrite = (type, detail) => emit(type, detail, [], { writes: 1, movements: 1 });
    function insertion(items, gap = 1, main = true) {
      memory(1);
      for (let i = gap; i < items.length; i++) {
        const value = items[i]; let j = i;
        while (j >= gap && cmp(items[j - gap], value) > 0) {
          if (main) write(j, items[j - gap]); else { items[j] = items[j - gap]; auxWrite('move', { ids: [items[j].id], aux: true }); } j -= gap;
        }
        if (j !== i) { if (main) write(j, value); else { items[j] = value; auxWrite('move', { ids: [value.id], aux: true }); } }
      }
    }
    if (algorithm === 'bubble') {
      memory(1);
      for (let end = a.length - 1; end > 0; end--) { let changed = false;
        for (let i = 0; i < end; i++) if (cmp(a[i], a[i + 1]) > 0) { swap(i, i + 1); changed = true; }
        emit('partition', { sortedFrom: end }); if (!changed) break;
      }
    } else if (algorithm === 'selection') {
      memory(1);
      for (let i = 0; i < a.length - 1; i++) { let min = i;
        for (let j = i + 1; j < a.length; j++) if (cmp(a[j], a[min]) < 0) min = j;
        swap(i, min); emit('partition', { sortedUntil: i + 1 });
      }
    } else if (algorithm === 'insertion') insertion(a);
    else if (algorithm === 'shell') {
      for (let gap = Math.floor(a.length / 2); gap > 0; gap = Math.floor(gap / 2)) { emit('partition', { gap }); insertion(a, gap); }
    } else if (algorithm === 'merge') {
      memory(a.length);
      for (let width = 1; width < a.length; width *= 2) for (let lo = 0; lo < a.length; lo += 2 * width) {
        const mid = Math.min(lo + width, a.length), hi = Math.min(lo + 2 * width, a.length), buffer = []; let l = lo, r = mid;
        while (l < mid || r < hi) { const item = r >= hi || (l < mid && cmp(a[l], a[r]) <= 0) ? a[l++] : a[r++];
          buffer.push(item); auxWrite('move', { ids: [item.id], aux: true, bufferSize: buffer.length }); }
        emit('merge', { range: [lo, hi], buffer: buffer.slice(0, 40).map(item => item.id), bufferSize: buffer.length });
        buffer.forEach((item, index) => write(lo + index, item));
      }
    } else if (algorithm === 'quick') {
      const stack = [[0, a.length - 1]];
      while (stack.length) { memory(stack.length * 2 + 1); const [lo, hi] = stack.pop(); if (lo >= hi) continue;
        const pivotIndex = Math.floor((lo + hi) / 2); swap(pivotIndex, hi); const pivot = a[hi];
        emit('pivot', { pivot: pivot.id, range: [lo, hi + 1] }); let p = lo;
        for (let j = lo; j < hi; j++) if (cmp(a[j], pivot) < 0) swap(p++, j);
        swap(p, hi); emit('partition', { pivot: pivot.id, pivotPosition: p, range: [lo, hi + 1] });
        stack.push([p + 1, hi], [lo, p - 1]);
      }
    } else if (algorithm === 'heap') {
      memory(1);
      function sift(start, end) { let parent = start;
        while (2 * parent + 1 < end) { let child = 2 * parent + 1;
          if (child + 1 < end && cmp(a[child], a[child + 1]) < 0) child++;
          if (cmp(a[parent], a[child]) >= 0) break; swap(parent, child); parent = child; }
      }
      emit('partition', { heapEnd: a.length });
      for (let i = Math.floor(a.length / 2) - 1; i >= 0; i--) sift(i, a.length);
      for (let end = a.length - 1; end > 0; end--) { swap(0, end); emit('partition', { heapEnd: end, sortedFrom: end }); sift(0, end); }
    } else if (algorithm === 'counting') {
      if (a.length) {
        let min = a[0].key, max = min; for (let i = 1; i < a.length; i++) { if (rangeCompare(a[i], min) < 0) min = a[i].key; if (rangeCompare(a[i], max) > 0) max = a[i].key; }
        const counts = Array(max - min + 1).fill(0), output = Array(a.length); memory(counts.length + output.length);
        emit('count', { phase: 'initialize', countRange: [min, max] }, [], { writes: counts.length });
        for (const item of a) { const index = item.key - min; counts[index]++; emit('count', { key: item.key, count: counts[index] }, [], { writes: 1 }); }
        for (let i = 1; i < counts.length; i++) { counts[i] += counts[i - 1]; emit('count', { key: i + min, count: counts[i], cumulative: true }, [], { writes: 1 }); }
        for (let i = a.length - 1; i >= 0; i--) { const item = a[i]; output[--counts[item.key - min]] = item;
          emit('bucket', { bucket: item.key, ids: [item.id], outputPosition: counts[item.key - min], countKey: item.key, remainingCount: counts[item.key - min] }, [], { writes: 2, movements: 1 }); }
        output.forEach((item, i) => write(i, item));
      }
    } else if (algorithm === 'radix') {
      // BigInt offset avoids unsafe max-min arithmetic even at both safe-integer extremes.
      if (a.length) {
        let minimum = a[0].key, maximum = minimum;
        for (let i = 1; i < a.length; i++) { if (rangeCompare(a[i], minimum) < 0) minimum = a[i].key; if (rangeCompare(a[i], maximum) > 0) maximum = a[i].key; }
        const min = BigInt(minimum), max = BigInt(maximum);
        memory(a.length + 10);
        for (let divisor = 1n; (max - min) / divisor > 0n; divisor *= 10n) {
          const buckets = Array.from({ length: 10 }, () => []); emit('partition', { digit: divisor.toString(), clearBuckets: true });
          for (const item of a) { const digit = Number(((BigInt(item.key) - min) / divisor) % 10n); buckets[digit].push(item); auxWrite('bucket', { bucket: digit, ids: [item.id], bucketSize: buckets[digit].length }); }
          let index = 0; for (const bucket of buckets) for (const item of bucket) write(index++, item);
        }
      }
    } else if (algorithm === 'bucket') {
      if (a.length) {
        let min = a[0].key, max = min;
        for (let i = 1; i < a.length; i++) { if (rangeCompare(a[i], min) < 0) min = a[i].key; if (rangeCompare(a[i], max) > 0) max = a[i].key; }
        const size = Math.min(256, Math.max(1, Math.ceil(Math.sqrt(a.length)))), buckets = Array.from({ length: size }, () => []); memory(a.length + size + 1);
        emit('partition', { clearBuckets: true });
        for (const item of a) {
          const span = max - min;
          const ratio = min === max ? 0 : Number.isFinite(span) ? (item.key - min) / span : (item.key / 2 - min / 2) / (max / 2 - min / 2);
          const index = Math.max(0, Math.min(size - 1, Math.floor(ratio * size))); buckets[index].push(item); auxWrite('bucket', { bucket: index, ids: [item.id], bucketSize: buckets[index].length });
        }
        let index = 0; for (let i = 0; i < size; i++) { insertion(buckets[i], 1, false); emit('bucket', { bucket: i, bucketOrder: buckets[i].slice(0, 40).map(item => item.id), bucketSize: buckets[i].length });
          for (const item of buckets[i]) write(index++, item); }
      }
    }
    emit('complete', { sortedUntil: a.length });
    return { output: a, operations, metrics };
  }
  // Auxiliary structures are bounded previews. The complete logical TS slot array is always retained.
  function previewSlot(visual, name, key, initial) {
    const values = visual[name] ||= {}, keys = visual[`${name}Keys`] ||= [];
    if (!Object.hasOwn(values, key)) {
      if (keys.length === PREVIEW_LIMIT) delete values[keys.shift()];
      keys.push(key); values[key] = initial;
    }
    return values;
  }
  function applyOperation(frame, operation) {
    for (const [index, id] of operation.changes) frame.order[index] = id;
    for (const key of ['pivot', 'pivotPosition', 'range', 'sortedFrom', 'sortedUntil', 'heapEnd', 'gap', 'buffer', 'bufferSize', 'digit', 'countRange'])
      if (Object.hasOwn(operation, key)) frame.visual[key] = operation[key];
    if (operation.clearBuckets) { frame.visual.buckets = {}; frame.visual.bucketsKeys = []; }
    if (operation.type === 'count' && operation.key != null) previewSlot(frame.visual, 'counts', operation.key, 0)[operation.key] = operation.count;
    if (operation.countKey != null) previewSlot(frame.visual, 'counts', operation.countKey, 0)[operation.countKey] = operation.remainingCount;
    if (operation.type === 'bucket' && operation.bucket != null) {
      const current = previewSlot(frame.visual, 'buckets', operation.bucket, { ids: [], size: 0 })[operation.bucket];
      if (operation.bucketOrder) { current.ids = [...operation.bucketOrder]; current.size = operation.bucketSize; }
      else { current.size = operation.bucketSize ?? current.size + 1; if (current.ids.length < PREVIEW_LIMIT) current.ids.push(...(operation.ids || []).slice(0, PREVIEW_LIMIT - current.ids.length)); }
    }
    frame.operation = operation;
    return frame;
  }
  const copyFrame = frame => JSON.parse(JSON.stringify(frame));
  function checkpoints(initial, operations) {
    // Account for retained auxiliary previews as well as main-array IDs. Reserve the initial checkpoint.
    const frameSlotBound = Math.max(1, initial.length + PREVIEW_LIMIT * (PREVIEW_LIMIT + 4) + 128);
    const count = Math.max(1, Math.floor(CHECKPOINT_SLOT_BUDGET / frameSlotBound) - 1);
    const stride = Math.max(256, Math.ceil(operations.length / count));
    const frame = { order: [...initial], visual: {}, operation: null }, result = [{ step: 0, frame: copyFrame(frame) }];
    for (let i = 0; i < operations.length; i++) { applyOperation(frame, operations[i]); if ((i + 1) % stride === 0) result.push({ step: i + 1, frame: copyFrame(frame) }); }
    return { stride, checkpoints: result, checkpointSlotBound: result.length * frameSlotBound, previewLimit: PREVIEW_LIMIT };
  }
  function reconstruct(run, step) {
    step = Math.max(0, Math.min(run.operations.length, Math.trunc(step) || 0));
    const checkpoint = run.checkpoints[Math.min(run.checkpoints.length - 1, Math.floor(step / run.stride))];
    const frame = copyFrame(checkpoint.frame);
    for (let i = checkpoint.step; i < step; i++) applyOperation(frame, run.operations[i]);
    return { ...frame, step };
  }
  function verify(input, output) {
    if (!Array.isArray(input) || !Array.isArray(output) || input.length !== output.length ||
      Array.from(input).some(item => !item || typeof item.id !== 'string') || Array.from(output).some(item => !item || typeof item.id !== 'string') ||
      new Set(input.map(item => item.id)).size !== input.length || new Set(output.map(item => item.id)).size !== input.length) return false;
    const ids = new Map(input.map(item => [item.id, item]));
    return output.every((item, i) => ids.get(item.id)?.key === item.key && ids.get(item.id)?.rank === item.rank &&
      (!i || output[i - 1].key < item.key || (output[i - 1].key === item.key && output[i - 1].rank < item.rank)));
  }
  function run(algorithm, order, keys, options = {}) {
    if (Array.isArray(order) && order.length > 20000) throw new SortError('capacity', 'Replay supports at most 20,000 TS; Arrange retains the complete document.');
    const input = prepare(order, keys), compatible = compatibility(algorithm, input);
    if (!compatible.ok) throw new SortError(compatible.code, compatible.message);
    const maxOperations = Math.max(1, Math.min(Math.trunc(Number(options.maxOperations) || 200000), 1000000));
    const start = now(), computation = engine(algorithm, input, false, maxOperations, options.signal), computationTime = now() - start;
    const tracedStart = now(), traced = engine(algorithm, input, true, maxOperations, options.signal), traceTime = now() - tracedStart;
    const initial = input.map(item => item.id), reconstructionStart = now(), indexed = checkpoints(initial, traced.operations), checkpointTime = now() - reconstructionStart;
    return { algorithm, initial, order: traced.output.map(item => item.id), keys: Object.fromEntries(input.map(item => [item.id, item.key])), correctness: verify(input, traced.output), operations: traced.operations, ...indexed,
      metrics: { ...computation.metrics, computationTime, traceGenerationOverhead: Math.max(0, traceTime - computationTime), checkpointTime,
        timingMethod: 'untraced instrumented engine; overhead is nonnegative traced-minus-untraced estimate', auxiliaryMemoryUnit: 'peak logical slots, excluding input, main array and trace' } };
  }
  return { catalog, prepare, compatibility, run, verify, reconstruct, applyOperation, SortError };
});
