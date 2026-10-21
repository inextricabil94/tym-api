import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const site = process.env.TYM_UI_URL || 'http://127.0.0.1:8791';
const fixture = JSON.parse(await readFile(path.resolve(import.meta.dirname, '../../build_verify/segmentation-fixtures/ro-mlnet_perceptron.json'), 'utf8'));
const output = path.resolve(import.meta.dirname, '../../build_verify/segmentation-browser');
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'en-GB' });
const errors = [], posts = [];
page.on('pageerror', error => errors.push(error.message));
page.on('request', request => { if (request.method() === 'POST' && new URL(request.url()).pathname.endsWith('/diagrams')) posts.push(request.postDataJSON()); });
try {
  await page.goto(site);
  await page.locator('#interface-language [data-locale="en"]').click();
  await page.locator('#language').selectOption('ro');
  await page.locator('#text').fill(fixture.text);
  await page.locator('#segmentation-algorithm').selectOption('mlnet_perceptron');
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled, null, { timeout: 90000 });
  await page.locator('#highlight').selectOption('');
  const resultRequest = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === '/v1/diagrams');
  await page.locator('.actions .primary').click();
  const response = await resultRequest;
  assert.equal(response.status(), 200);
  const result = await response.json();
  assert.equal(result.segmentation.algorithm, 'mlnet_perceptron');
  assert.equal(result.segmentation.score_kind, 'margin');
  await page.locator('.segment-overview svg').waitFor();
  await page.waitForFunction(() => document.querySelectorAll('#cached-density option:not(:disabled)').length === 10, null, { timeout: 90000 });
  await page.getByRole('button', { name: 'Segmentation', exact: true }).click();
  await page.locator('.source-segment').first().waitFor();
  const marked = () => page.locator('.segmented-source').evaluate(element => [...element.querySelectorAll('.source-gap,.source-segment')].map(part => part.textContent).join(''));
  assert.equal(await marked(), fixture.text);
  assert.equal(await page.locator('.source-segment').count(), result.diagram.segments.length);
  await page.locator('.segment-marker').nth(1).click();
  assert.ok(!(await page.getByTestId('boundary-score').textContent()).includes('%'));
  await page.locator('#cached-density').selectOption('100');
  assert.equal(await marked(), fixture.text);
  assert.equal(posts.length, 1, 'Cached density caused a second generation');
  await page.locator('.segmentation-view').scrollIntoViewIfNeeded();
  await page.screenshot({ path: path.join(output, 'live-local.png') });
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ actualApi: true, algorithm: result.segmentation.algorithm, language: 'ro',
    events: result.diagram.events.length, segments: result.diagram.segments.length,
    cachedDensities: 10, generationRequests: posts.length, exactMarkedSource: true, browserErrors: errors.length }));
} finally { await browser.close(); }
