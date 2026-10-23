import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const site = process.env.TYM_UI_URL || 'http://127.0.0.1:8791';
const fixture = JSON.parse(await readFile(path.resolve(import.meta.dirname, '../../build_verify/segmentation-fixtures/en-article_rules.json'), 'utf8'));
const book = { id: 'offline-fixture', label: 'Authored offline book', language: 'en', source: 'Authored test',
  character_count: fixture.text.length, sha256_utf8: 'authored-source-hash',
  sections: [{ id: 's1', label: 'ONE', kind: 'chapter', start: 0, end: fixture.text.length, character_count: fixture.text.length }] };
const output = path.resolve(import.meta.dirname, '../../build_verify/offline-browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, locale: 'en-GB' });
let offline = false, restricted = false, catalogRequests = 0, generations = 0;
const errors = [];
async function routes(target) {
  await target.route('**/v1/**', async route => {
    if (offline) return route.abort('internetdisconnected');
    const url = new URL(route.request().url()); let data;
    if (url.pathname.endsWith('/config')) data = { enabled: false, text_requires_login: restricted };
    else if (url.pathname === '/v1/books') { catalogRequests++; data = { books: [{ ...book, text: 'RAW CATALOG SOURCE MUST NOT BE CACHED' }] }; }
    else if (url.pathname.endsWith('/entities')) data = { entities: [], pos_filter: { provider: 'spaCy' } };
    else if (url.pathname.endsWith('/source')) data = { text: fixture.text };
    else if (url.pathname.endsWith('/diagrams')) { generations++; data = { ...fixture.response, xml: '' }; }
    else throw new Error('Unexpected endpoint: ' + url.pathname);
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) });
  });
}
const page = await context.newPage(); page.on('pageerror', error => errors.push(error.message)); await routes(page);
try {
  await page.goto(site);
  await page.locator('#hosted-book option[value="offline-fixture"]').waitFor({ state: 'attached' });
  await page.waitForFunction(() => Boolean(navigator.serviceWorker.controller), null, { timeout: 60000 });
  const catalogCount = catalogRequests;
  await page.reload();
  await page.locator('#hosted-book option[value="offline-fixture"]').waitFor({ state: 'attached' });
  assert.equal(catalogRequests, catalogCount, 'Fresh catalog should load from IndexedDB');
  await page.locator('#hosted-book').selectOption(book.id);
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
  await page.locator('.actions .primary').click();
  await page.locator('.diagram-area svg').waitFor();
  await page.waitForFunction(() => document.querySelectorAll('#saved-diagram option').length === 2);
  assert.equal(generations, 1);
  await page.evaluate(async () => {
    const db = await new Promise((resolve, reject) => { const r = indexedDB.open('tym-offline-v1'); r.onsuccess = () => resolve(r.result); r.onerror = reject; });
    await new Promise((resolve, reject) => {
      const tx = db.transaction('records', 'readwrite'), store = tx.objectStore('records');
      const request = store.getAll();
      request.onsuccess = () => {
        const record = request.result.find(item => item.kind === 'diagram');
        record.response.density_cache.views = {};
        record.response.density_cache.options = record.response.density_cache.options.filter(option => option.view_id === 'base');
        store.put(record);
      };
      tx.oncomplete = resolve; tx.onabort = reject;
    });
    db.close();
  });
  await page.locator('.actions .primary').click();
  await page.locator('.diagram-area svg').waitFor();
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
  assert.equal(generations, 1, 'Matching diagram should not consume another generation');
  assert.equal(await page.locator('#cached-density option:not(:disabled)').count(), 10, 'Older disk snapshot replaced completed live densities');
  const records = await page.evaluate(async () => {
    const db = await new Promise((resolve, reject) => { const r = indexedDB.open('tym-offline-v1'); r.onsuccess = () => resolve(r.result); r.onerror = reject; });
    return new Promise(resolve => { const r = db.transaction('records').objectStore('records').getAll(); r.onsuccess = () => { db.close(); resolve(r.result); }; });
  });
  assert.equal(records.find(item => item.kind === 'catalog').books[0].text, undefined);
  assert.equal(records.find(item => item.kind === 'diagram').response.text, undefined);
  assert.equal(records.find(item => item.kind === 'diagram').response.density_cache.session_id, undefined);
  assert.equal(records.find(item => item.kind === 'diagram').book, true);
  assert.equal(records.find(item => item.kind === 'diagram').response.density_cache.options.length, 10, 'Live bundle was not flushed before reuse');
  const concurrent = await page.evaluate(async response => {
    const a = TymOffline.create({ database: 'tym-offline-concurrency-test' });
    const b = TymOffline.create({ database: 'tym-offline-concurrency-test' });
    await Promise.all([a.putDiagram('a', 'https://test', '1440x1050', response, {}),
      b.putDiagram('b', 'https://test', '1440x1050', response, {})]);
    const count = (await a.diagrams('https://test', '1440x1050')).length;
    const oldRevision = b.revision(); await a.clear();
    const late = await b.putDiagram('late', 'https://test', '1440x1050', response, {}, oldRevision);
    return { count, late: late.saved };
  }, fixture.response);
  assert.deepEqual(concurrent, { count: 2, late: false }, 'Concurrent stores lost data or restored pre-clear work');
  const shellPaths = await page.evaluate(async () => (await Promise.all((await caches.keys()).filter(key => key.startsWith('tym-shell-')).map(async key =>
    (await (await caches.open(key)).keys()).map(request => new URL(request.url).pathname)))).flat());
  assert.ok(shellPaths.length > 20);
  assert.ok(!shellPaths.some(value => value.includes('/v1/') || value.includes('/local-examples/')));
  offline = true; await context.setOffline(true); await page.reload();
  await page.locator('#saved-diagram option').nth(1).waitFor({ state: 'attached' });
  await page.locator('#saved-diagram').selectOption({ index: 1 });
  await page.locator('.diagram-area svg').waitFor();
  assert.equal(await page.locator('.download-actions').getByRole('button', { name: 'JSON', exact: true }).count(), 0, 'Saved books must retain export restrictions');
  assert.equal(await page.locator('.result-toolbar').getByRole('button', { name: 'JSON', exact: true }).count(), 0);
  await page.locator('#cached-density').selectOption('100');
  assert.equal(generations, 1);
  await page.screenshot({ path: path.join(output, 'offline-desktop.png') });
  await page.setViewportSize({ width: 390, height: 1050 });
  await page.waitForFunction(() => document.querySelectorAll('#saved-diagram option').length === 1 && !document.querySelector('.diagram-area svg'));
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1));
  await page.screenshot({ path: path.join(output, 'offline-mobile.png') });
  await page.setViewportSize({ width: 1440, height: 1050 });
  await page.locator('#saved-diagram option').nth(1).waitFor({ state: 'attached' });
  await page.getByRole('button', { name: 'Clear offline cache', exact: true }).click();
  await page.waitForFunction(() => document.querySelectorAll('#saved-diagram option').length === 1);
  await page.reload();
  await page.waitForFunction(() => document.querySelectorAll('#hosted-book option').length === 1
    && !document.querySelector('#hosted-book').textContent.includes('Loading'));
  assert.equal(await page.locator('#saved-diagram option').count(), 1);
  offline = false; restricted = true; await context.setOffline(false); await page.reload();
  await page.locator('#hosted-book option[value="offline-fixture"]').waitFor({ state: 'attached' });
  await page.locator('#hosted-book').selectOption(book.id);
  await page.getByRole('button', { name: 'Sign in to read text', exact: true }).waitFor();
  await page.locator('.actions .primary').click();
  await page.locator('.diagram-area svg').waitFor();
  await page.waitForTimeout(300);
  assert.equal(await page.locator('#saved-diagram option').count(), 1, 'Restricted book result persisted offline');
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, indexedDbSurvivesReload: true, offlineShell: true, catalogMetadataOnly: true,
    repeatedGenerationRequests: 0, savedBookExportPolicy: true, sourceRequestsNeverCached: true,
    exactResolution: true, clearCache: true, concurrentStorage: true, liveBundleBeatsOlderSnapshot: true,
    restrictedBooksNotPersisted: true, browserErrors: errors.length }));
} finally { await context.close(); await browser.close(); }
