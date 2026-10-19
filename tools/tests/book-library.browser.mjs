import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const { cleanBookText, bookSections } = require('../book-text.cjs');
const root = path.resolve(import.meta.dirname, '../..');
const folder = path.join(root, 'build_verify/library-browser-fixture');
await mkdir(folder, { recursive: true });
const text = cleanBookText('Chapter ONE\nAdam searched for Margaret in London. Karl traveled alone. Adam waited by the house.\n\nChapter TWO\nMargaret found Adam at dawn. Years earlier, Karl had carried Adam through the rain. Adam remembered Johan.');
const books = [];
for (const id of ['tash-aw-en', 'leghe', 'tinutul-1', 'colind', 'oversized']) {
  const content = id === 'oversized' ? cleanBookText('Adam waited.\n\n'.repeat(9000)) : text;
  books.push({ id, label: `Authored test fixture ${id}`, language: 'en', source: 'Authored test; not a published book',
    character_count: content.length, original_character_count: content.length,
    sha256_utf8: createHash('sha256').update(content).digest('hex'), sections: bookSections(content) });
  await writeFile(path.join(folder, `${id}.txt`), content);
}
await writeFile(path.join(folder, 'catalog.json'), JSON.stringify(books));
const socket = createServer();
await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve));
const port = socket.address().port;
await new Promise(resolve => socket.close(resolve));
const api = `http://127.0.0.1:${port}`;
const server = spawn('dotnet', [path.join(root, 'bin/Release/net9.0/Tym.Api.dll'), '--urls', api],
  { cwd: root, windowsHide: true, stdio: 'ignore', env: { ...process.env, TYM_LIBRARY_PATH: folder } });
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
page.setDefaultTimeout(90000);
const errors = [], posts = [];
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => { if (request.method() === 'POST' && request.url().startsWith(api)) posts.push({ url: request.url(), body: request.postDataJSON() }); });
const post = (url, body) => fetch(`${api}${url}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
try {
  for (let tries = 0; tries < 100; tries++) {
    try { if ((await fetch(`${api}/health`)).ok) break; } catch {}
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  const catalog = await (await fetch(`${api}/v1/books`)).json();
  assert.equal(catalog.books.length, 5);
  assert.ok(catalog.books.every(book => !('text' in book) && !('full_text_url' in book)));
  const publicSource = await fetch(`${api}/v1/books/leghe/source`);
  assert.equal(publicSource.status, 200, 'Default reading must work with accounts disabled');
  assert.equal((await publicSource.json()).text, text);
  for (const url of ['/v1/books/leghe/text', '/library/leghe.txt', '/library/catalog.json', '/leghe.txt'])
    assert.equal((await fetch(`${api}${url}`)).status, 404);
  assert.equal((await post('/v1/books/missing/entities', {})).status, 404);
  assert.equal((await post('/v1/books/leghe/entities', { section_id: '../leghe' })).status, 404);
  const complete = await post('/v1/books/oversized/diagrams', {});
  assert.equal(complete.status, 200);
  const completeResult = await complete.json();
  assert.equal(completeResult.analysis.character_count, books.find(book => book.id === 'oversized').character_count);
  assert.equal(completeResult.xml, '');

  await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG = ${JSON.stringify({ apiBaseUrl: api, examples: [] })};` }));
  await page.goto(process.env.TYM_UI_URL || 'http://127.0.0.1:8780/');
  await page.waitForFunction(() => document.querySelectorAll('#hosted-book option').length === 6 && !document.getElementById('highlight').disabled);
  for (const id of ['tash-aw-en', 'leghe', 'tinutul-1', 'colind']) {
    await page.locator('#hosted-book').selectOption(id);
    await page.waitForFunction(() => { const image = document.querySelector('.example-visual img'); return image?.complete && image.naturalWidth > 0; });
  }
  const before = posts.length;
  await page.locator('#chapter').selectOption('1');
  await page.waitForTimeout(800);
  assert.equal(posts.length, before, 'Selecting a book/chapter must not trigger NLP');
  assert.equal(await page.locator('#text').count(), 0, 'Book source must not be exposed in the editor');
  await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('Chapter TWO'));
  assert.equal(await page.locator('.source-prose').textContent(), text.slice(books[0].sections[1].start));
  assert.equal(await page.locator('.download-actions').getByRole('button', { name: 'JSON', exact: true }).count(), 0);
  assert.equal(await page.locator('.download-actions').getByRole('button', { name: 'XML', exact: true }).count(), 0);
  assert.equal(await page.getByRole('button', { name: 'JSON', exact: true }).count(), 0);
  const entityResponse = page.waitForResponse(response => response.url().endsWith('/v1/books/colind/entities'));
  await page.getByRole('button', { name: 'Refresh entities', exact: true }).click();
  assert.equal((await entityResponse).status(), 200);
  await page.waitForFunction(() => !document.getElementById('highlight').disabled);
  await page.locator('#highlight').selectOption('Adam');
  let allowBackground;
  const backgroundGate = new Promise(resolve => { allowBackground = resolve; });
  await page.route('**/v1/density-sessions/**', async route => { await backgroundGate; await route.continue(); });
  const responsePromise = page.waitForResponse(response => response.url().endsWith('/v1/books/colind/diagrams'));
  await page.getByRole('button', { name: 'Generate diagram', exact: true }).click();
  const response = await responsePromise;
  assert.equal(response.status(), 200);
  const result = await response.json();
  assert.equal(result.density_cache.options.length, 1, 'Initial response must contain only the selected density');
  assert.equal(result.density_cache.pending_densities.length, 9);
  await page.waitForFunction(() => document.querySelector('.diagram-area svg'));
  assert.equal(await page.locator('#cached-density').inputValue(), '50');
  assert.doesNotMatch(await page.locator('body').innerText(), /Complete source retained|Analysis limit:|unlabeled local input/);
  assert.equal(await page.getByRole('radio', { name: 'Full text', exact: true }).isEnabled(), true);
  allowBackground();
  assert.equal(result.analysis.character_count, text.length - books[0].sections[1].start);
  assert.equal(posts.find(post => post.url.endsWith('/diagrams')).body.options.width, 1440);
  await page.waitForFunction(() => document.querySelectorAll('#cached-density option:not([disabled])').length === 10);
  const generated = posts.filter(post => post.url.endsWith('/diagrams')).length;
  await page.locator('#cached-density').selectOption('100');
  await page.locator('#cached-density').selectOption('10');
  assert.equal(posts.filter(post => post.url.endsWith('/diagrams')).length, generated);
  for (const request of posts.filter(post => post.url.includes('/v1/books/'))) {
    assert.ok(!('text' in request.body));
    assert.equal(request.body.section_id, 's2');
  }
  const screenshots = [];
  for (const width of [320, 390, 768, 1440, 1920]) {
    await page.setViewportSize({ width, height: 1050 });
    await page.evaluate(() => document.fonts.ready);
    const layout = await page.evaluate(() => ({ width: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth,
      densityHeight: document.querySelector('.density-cache-control').getBoundingClientRect().height }));
    assert.ok(layout.scroll <= layout.width + 1, `Page overflow at ${width}`);
    assert.ok(layout.densityHeight < 100, `Oversized density control at ${width}`);
    const screenshot = path.join(root, `build_verify/library-${width}.png`);
    await page.screenshot({ path: screenshot, fullPage: true });
    screenshots.push(screenshot);
  }
  assert.equal(posts.filter(post => post.url.endsWith('/diagrams')).length, generated, 'Resize must not consume diagram usage');
  assert.equal(await page.locator('#cached-density').isDisabled(), true);
  assert.equal(await page.locator('.diagram-area svg').count(), 0, 'Old-resolution diagram must be discarded');
  assert.equal(await page.locator('#width').textContent(), '1920px');
  const resizedResponse = page.waitForResponse(response => response.url().endsWith('/v1/books/colind/diagrams'));
  await page.getByRole('button', { name: 'Generate diagram', exact: true }).click();
  assert.equal((await resizedResponse).status(), 200);
  await page.waitForFunction(() => document.querySelector('.diagram-area svg')?.getAttribute('width') === '1920');
  assert.equal(posts.filter(post => post.url.endsWith('/diagrams')).at(-1).body.options.width, 1920);
  await page.setViewportSize({ width: 1920, height: 1051 });
  await page.waitForFunction(() => document.querySelector('[data-testid="window-resolution"]')?.textContent === 'Window 1920 x 1051'
    && !document.querySelector('.diagram-area svg'));
  assert.equal(await page.locator('.diagram-area svg').count(), 0, 'Height-only resize must invalidate resolution cache');

  let release, requested;
  const hold = new Promise(resolve => { release = resolve; });
  const started = new Promise(resolve => { requested = resolve; });
  await page.route('**/v1/books/colind/diagrams', async route => {
    requested(); await hold;
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(result) });
  });
  await page.getByRole('button', { name: 'Generate diagram', exact: true }).click();
  await started;
  await page.setViewportSize({ width: 1280, height: 900 });
  release();
  await page.waitForFunction(() => { const button = document.querySelector('.actions .primary'); return button && !button.disabled; });
  assert.equal(await page.locator('.diagram-area svg').count(), 0, 'In-flight old-resolution response must be ignored');
  assert.equal(await page.locator('#cached-density').isDisabled(), true);
  await page.unroute('**/v1/books/colind/diagrams');
  for (let count = 0; count < 2; count++) assert.equal((await post('/v1/diagrams', { text: 'Adam waited.', options: { language: 'en' } })).status, 200);
  const limited = await post('/v1/books/colind/diagrams', { section_id: 's2' });
  assert.equal(limited.status, 429, 'Book and normal diagrams must share five daily permits');
  assert.equal((await limited.json()).limit, 5);
  let entityLimit;
  for (let count = 0; count < 31; count++) {
    const response = await post('/v1/books/colind/entities', { section_id: 's2' });
    if (response.status === 429) { entityLimit = await response.json(); break; }
    assert.equal(response.status, 200);
  }
  assert.equal(entityLimit.limit, 30);
  assert.equal(entityLimit.window, '1 minute');
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, books: books.length, tested_viewports: screenshots.length,
    legacy_source_paths_blocked: true, public_reader_without_accounts: true, no_source_editor_or_text_exports: true, shared_daily_quota: true,
    separate_entity_quota: true, cached_presets: 10, selected_density_first: true, full_book_over_100000: true, window_width_applied: true,
    resize_invalidates_cache: true, resize_requests: 0, stale_resolution_response_ignored: true }, null, 2));
} finally {
  await browser.close(); server.kill();
  await new Promise(resolve => server.once('exit', resolve));
}
