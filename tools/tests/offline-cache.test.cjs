const { test } = require('node:test');
const assert = require('node:assert/strict');
const cache = require('../../ui/Tym.Ui/wwwroot/offline-cache.js');
const response = { model_version: 'test', render: { svg: '<svg/>' }, diagram: { segments: [], tracks: [] },
  density_cache: { session_id: 'secret-session', pending_densities: [10], options: [], views: {} } };
const book = { id: 'book', label: 'Book', language: 'ro', character_count: 10, sha256_utf8: 'abc', text: 'NOT CATALOG DATA',
  sections: [{ id: 's1', label: 'ONE', kind: 'chapter', start: 0, end: 10, text: 'NOT SECTION DATA' }] };
test('catalog persists only public metadata, never supplied source fields', () => {
  const clean = cache.catalog([book]);
  assert.equal(clean[0].text, undefined); assert.equal(clean[0].sections[0].text, undefined);
  assert.throws(() => cache.catalog([{ ...book, sections: [{ start: -1 }] }]));
});
test('diagram snapshots remove IP-bound sessions but retain generated views', () => {
  const saved = cache.savedResponse(response);
  assert.equal(saved.density_cache.session_id, undefined);
  assert.deepEqual(saved.density_cache.pending_densities, []);
  assert.equal(response.density_cache.session_id, 'secret-session');
  assert.equal(cache.savedResponse({}), null);
});
test('digest isolates text, API, chapter/hash, segmentation, highlight and full resolution', async () => {
  const payload = { text: '🙂 Ana a venit.', options: { width: 400, language: 'ro', segmentation_algorithm: 'article_rules', track_density: 50 } };
  const key = await cache.identity('https://api.test/', payload, '320x800');
  assert.equal(key, await cache.identity('https://api.test', { ...payload, options: { ...payload.options, track_density: 100 } }, '320x800'));
  for (const [api, data, size, input] of [
    ['https://other.test', payload, '320x800'], ['https://api.test', { ...payload, text: 'Ana a venit.' }, '320x800'],
    ['https://api.test', payload, '390x800'], ['https://api.test', payload, '320x801'],
    ['https://api.test', { ...payload, section_id: 's2' }, '320x800', book],
    ['https://api.test', payload, '320x800', { ...book, sha256_utf8: 'different' }],
    ['https://api.test', { ...payload, options: { ...payload.options, segmentation_algorithm: 'mlnet_logistic' } }, '320x800'],
    ['https://api.test', { ...payload, options: { ...payload.options, highlight_entities: ['Ana'] } }, '320x800']
  ]) assert.notEqual(key, await cache.identity(api, data, size, input));
  assert.ok(!key.includes(payload.text));
});
test('blocked storage degrades to a bounded expiring session cache', async () => {
  let now = 1000;
  const store = cache.create({ indexedDB: null, now: () => now, ttl: 100, maxEntries: 2 });
  assert.deepEqual(await store.putDiagram('one', 'https://api', '320x800', response, { label: 'one' }), { saved: true, persistent: false });
  now++; await store.putDiagram('two', 'https://api', '320x800', response, { label: 'two' });
  now++; await store.putDiagram('three', 'https://api', '320x800', response, { label: 'three' });
  assert.equal(await store.getDiagram('one'), null);
  assert.equal((await store.diagrams('https://api', '320x800')).length, 2);
  assert.equal((await store.diagrams('https://api', '320x801')).length, 0);
  now += 101; assert.equal(await store.getDiagram('three'), null);
});
test('clearing prevents earlier in-flight work from repopulating storage', async () => {
  const store = cache.create({ indexedDB: null });
  const revision = store.revision(); await store.clear();
  assert.equal((await store.putDiagram('late', 'https://api', '320x800', response, {}, revision)).saved, false);
  await store.putCatalog('https://api/', [book]);
  assert.equal((await store.getCatalog('https://api')).books.length, 1);
  await store.putDiagram('book', 'https://api', '320x800', response, { book: true });
  await store.removeBooks('https://api'); assert.equal(await store.getDiagram('book'), null);
  assert.ok(await store.getCatalog('https://api'));
});
test('oversized diagrams are skipped without breaking generation', async () => {
  const store = cache.create({ indexedDB: null, maxBytes: 10 });
  assert.equal((await store.putDiagram('large', 'https://api', '320x800', response, {})).tooLarge, true);
});
test('saved diagrams retain opaque ordering document identity for later reopening', async () => {
  const store = cache.create({ indexedDB: null });
  await store.putDiagram('one', 'https://api', '320x800', response, { orderingKey: 'https://api:text:digest' });
  assert.equal((await store.getDiagram('one')).orderingKey, 'https://api:text:digest');
});
