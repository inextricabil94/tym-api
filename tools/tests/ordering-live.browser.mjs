import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const O = require('../../ui/Tym.Ui/wwwroot/ordering-core.js');
const site = process.env.TYM_UI_URL || 'http://127.0.0.1:8793';
const api = process.env.TYM_API_URL || 'http://127.0.0.1:8798';
if (!['127.0.0.1', 'localhost'].includes(new URL(api).hostname)) throw Error('Live regression requires a loopback API.');
const fixture = JSON.parse(await readFile(new URL('../../build_verify/segmentation-fixtures/en-article_rules.json', import.meta.url), 'utf8'));
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, serviceWorkers: 'block', locale: 'en-GB' });
const errors = []; page.on('pageerror', error => errors.push(error.message));
await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG={apiBaseUrl:${JSON.stringify(api)},examples:[]};` }));
try {
  await page.goto(site); await page.locator('#interface-language [data-locale="en"]').click();
  await page.locator('#text').fill(fixture.text);
  const responsePromise = page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === '/v1/diagrams');
  await page.locator('.actions .primary').click(); const response = await responsePromise;
  assert.equal(response.status(), 200); const result = await response.json();
  await page.locator('.segment-overview svg').waitFor();
  await page.getByRole('button', { name: 'Ordering', exact: true }).click(); await page.locator('.ordering-block').first().waitFor();
  const model = O.createModel(result.diagram), state = O.createState(model);
  let move;
  for (const id of state.user) for (let position = 1; position <= state.user.length; position++) {
    const preview = O.previewMove(model, state, id, position);
    if (preview.valid && preview.order.join() !== state.user.join()) move ||= { id, position };
  }
  assert.ok(move); await page.locator(`[data-ts-id="${move.id}"]`).click();
  await page.getByLabel('Preferred position', { exact: true }).fill(String(move.position));
  await page.getByRole('button', { name: 'Move to position', exact: true }).click();
  assert.equal(await page.locator(`[data-ts-id="${move.id}"]`).getAttribute('data-track-id'), model.byId[move.id].track_id);
  await page.getByRole('button', { name: 'Replay', exact: true }).click();
  await page.getByLabel('Ordering keys', { exact: true }).selectOption('story_order');
  await page.getByRole('button', { name: 'Build trace', exact: true }).click();
  await page.locator('[data-suggestion-result]').waitFor(); assert.match(await page.locator('[data-suggestion-result]').textContent(), /Sorting correct: Pass.*Temporal compatibility: Compatible/);
  await page.getByRole('button', { name: 'Apply Suggested Order', exact: true }).click();
  await page.getByRole('button', { name: 'Compare', exact: true }).click(); assert.equal(await page.locator('.ordering-comparison-column').count(), 3);
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ liveApi: api, segments: model.original.length, manualMove: true, workerReplay: true, validatedApply: true, compare: true, pageErrors: errors.length }));
} finally { await browser.close(); }
