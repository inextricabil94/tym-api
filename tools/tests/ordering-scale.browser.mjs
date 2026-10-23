import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const O = require('../../ui/Tym.Ui/wwwroot/ordering-core.js');
const site = process.env.TYM_UI_URL || 'http://127.0.0.1:8793';
const seed = JSON.parse(await readFile(new URL('../../build_verify/segmentation-fixtures/en-article_rules.json', import.meta.url), 'utf8'));

// Authored scaling fixture based on the real response shape. This is an
// engineering pagination/replay check, not an extraction or corpus benchmark.
const count = 1003, response = structuredClone(seed.response);
const segmentTemplate = seed.response.diagram.segments[0], eventTemplate = seed.response.diagram.events[0];
let text = '';
const segments = [], events = [];
for (let i = 1; i <= count; i++) {
  const excerpt = `Authored segment ${String(i).padStart(4, '0')} waits.`, start = text.length;
  text += `${excerpt}\n`;
  segments.push({ ...segmentTemplate, id: `TS${i}`, text: excerpt, text_order: i, story_order: i,
    track_id: `TT${(i - 1) % 7 + 1}`, span_start: start, span_end: start + excerpt.length, event_ids: [`EV${i}`] });
  events.push({ ...eventTemplate, id: `EV${i}`, text: excerpt, order: i, span_start: start, span_end: start + excerpt.length,
    relation_to_previous: 'UNKNOWN', relation_cue: '', relation_evidence: 'Authored scaling fixture; ordering is unresolved.' });
}
Object.assign(response.diagram, {
  segments, events, entity_mentions: [],
  tracks: Array.from({ length: 7 }, (_, i) => ({ id: `TT${i + 1}`, name: `Authored track ${i + 1}`, left_endpoint: 'START', right_endpoint: 'STOP' })),
  relations: [{ from_id: 'TS1', rel: 'BEFORE', to_id: 'TS2', evidence: 'Authored scale constraint.', source: 'authored_scale_fixture' }],
  time_ml: { events: [], timex3: [], make_instances: [], tlinks: [] },
  temporal_ordering: { method: 'evidence_partial_order_v1', layout_basis: 'partial_order_with_text_order_tiebreak',
    constraints: [{ from_id: 'EV1', rel: 'BEFORE', to_id: 'EV2', evidence: 'Authored scale constraint.', source: 'authored_scale_fixture' }],
    unresolved: [], conflicts: [], placements: segments.map(segment => ({ segment_id: segment.id, layout_order: segment.story_order, has_unresolved_order: segment.text_order > 2 })) }
});
response.id = 'authored-ordering-scale-1003';
response.segmentation = { ...response.segmentation, decisions: [] };
const model = O.createModel(response.diagram);
let expected = O.createState(model);
assert.equal(expected.validation.valid, true);
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, serviceWorkers: 'block', locale: 'en-GB' });
const errors = []; page.on('pageerror', error => errors.push(error.message));
page.setDefaultTimeout(30000);
await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG={apiBaseUrl:${JSON.stringify(site)},examples:[]};` }));
await page.route('**/v1/**', route => {
  const url = new URL(route.request().url());
  const data = url.pathname.endsWith('/books') ? { books: [] } : url.pathname.endsWith('/config') ? { enabled: false }
    : url.pathname.endsWith('/entities') ? { entities: [] } : response;
  return route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) });
});
const arrange = () => page.locator('.ordering-main').filter({ has: page.getByRole('heading', { name: 'User Adjusted', exact: true }) });
const visibleOrder = () => arrange().locator('.ordering-block').evaluateAll(elements => elements
  .map(element => ({ id: element.dataset.tsId, track: element.dataset.trackId, position: Number(element.parentElement.dataset.position) }))
  .sort((a, b) => a.position - b.position));
const property = label => page.locator('.ordering-inspector .segment-properties > div').filter({ has: page.locator('dt', { hasText: new RegExp(`^${label}$`) }) }).locator('dd').textContent();
const savedRecord = () => page.evaluate(() => {
  const key = Object.keys(localStorage).find(value => value.startsWith('tym-ordering:'));
  return key ? JSON.parse(localStorage.getItem(key)) : null;
});
async function moveSelected(position) {
  await page.getByLabel('Assign to display group', { exact: true }).selectOption(O.groupAt(expected, position).id);
  await page.getByLabel('Preferred position', { exact: true }).fill(String(position));
  await page.getByRole('button', { name: 'Move to position', exact: true }).click();
  expected = O.transition(model, expected, { type: 'move', id: 'TS24', position });
  assert.equal(expected.validation.valid, true);
  assert.equal(await property('Current position'), String(position));
}
let releaseWorker;
try {
  await page.goto(site);
  await page.locator('#interface-language [data-locale="en"]').click();
  await page.locator('#text').fill(text);
  await page.locator('.actions .primary').click();
  await page.locator('.segment-overview svg').waitFor();
  await page.getByRole('button', { name: 'Ordering', exact: true }).click();
  await arrange().locator('.ordering-block').first().waitFor();
  assert.deepEqual((await visibleOrder()).map(item => item.id), expected.user.slice(0, 24));
  assert.equal(await arrange().locator('.ordering-block').count(), 24, 'Only the current position page is rendered');

  // Keep a drag open so both the preview and the rejected drop are observable.
  const transfer = await page.evaluateHandle(() => new DataTransfer());
  const invalidSource = arrange().locator('[data-ts-id="TS2"]');
  const invalidDestination = arrange().locator('.ordering-drop[data-position="1"]').first();
  await invalidSource.dispatchEvent('dragstart', { dataTransfer: transfer });
  await invalidDestination.dispatchEvent('dragover', { dataTransfer: transfer });
  await arrange().locator('.ordering-drop.invalid').first().waitFor();
  assert.equal(await arrange().locator('.ordering-block.conflict').count(), 2);
  assert.match(await arrange().locator('.ordering-diagnostics').textContent(), /TS1 must precede TS2/);
  await invalidDestination.dispatchEvent('drop', { dataTransfer: transfer });
  await invalidSource.dispatchEvent('dragend', { dataTransfer: transfer });
  await transfer.dispose();
  assert.deepEqual((await visibleOrder()).map(item => item.id), expected.user.slice(0, 24), 'Invalid drag changes no committed positions');

  const boundary = arrange().locator('[data-ts-id="TS24"]');
  await boundary.click();
  await boundary.focus();
  await boundary.press('Alt+ArrowRight');
  expected = O.transition(model, expected, { type: 'move', id: 'TS24', position: 25 });
  await arrange().locator('[data-ts-id="TS24"]').waitFor();
  assert.equal(await arrange().locator('[data-ts-id="TS24"]').evaluate(element => element === document.activeElement), true,
    'Keyboard movement from position 24 to 25 follows the TS and restores focus on its new page');
  assert.equal((await visibleOrder())[0].position, 25);
  assert.equal(await property('TS ID'), 'TS24');
  assert.equal(await property('TT ID'), model.byId.TS24.track_id);
  assert.equal(await property('Original position'), '24');
  assert.equal(await property('Current position'), '25');

  await moveSelected(count);
  assert.equal((await visibleOrder()).length, 19, 'Final page renders the remainder of the 1,003-position layout');
  assert.equal((await visibleOrder()).at(-1).id, 'TS24');
  await moveSelected(1);

  // Traverse every page and verify the complete logical arrangement, identity,
  // position numbers and immutable TT membership despite the bounded DOM.
  const rendered = [];
  for (let start = 0; start < count; start += 24) {
    const current = await visibleOrder();
    assert.equal(current.length, Math.min(24, count - start));
    for (let i = 0; i < current.length; i++) {
      assert.equal(current[i].position, start + i + 1);
      assert.equal(current[i].track, model.byId[current[i].id].track_id);
    }
    rendered.push(...current.map(item => item.id));
    if (start + 24 < count) await arrange().getByRole('button', { name: 'Next positions', exact: true }).click();
  }
  assert.deepEqual(rendered, expected.user);
  assert.equal(new Set(rendered).size, count);
  assert.equal(await property('TS ID'), 'TS24', 'Paging does not change the selected TS');
  await page.getByRole('button', { name: 'Save Arrangement', exact: true }).click();
  assert.deepEqual((await savedRecord()).order, expected.user, 'Save contains every logical TS, including unrendered positions');

  // Leave an unsaved change, then switch top-level tabs without remounting state.
  await moveSelected(2);
  const unsavedPage = await visibleOrder();
  await page.getByRole('tablist', { name: 'Result views' }).getByRole('button', { name: 'SVG', exact: true }).click();
  await page.getByRole('button', { name: 'Ordering', exact: true }).click();
  assert.deepEqual(await visibleOrder(), unsavedPage);
  assert.equal(await property('TS ID'), 'TS24');
  assert.equal(await property('Current position'), '2');
  await page.getByRole('button', { name: 'Save Arrangement', exact: true }).click();
  assert.deepEqual((await savedRecord()).order, expected.user);
  assert.equal(O.validate(model, (await savedRecord()).order).valid, true);

  await page.getByRole('button', { name: 'Replay', exact: true }).click();
  await page.getByRole('button', { name: 'Build trace', exact: true }).click();
  await page.locator('[data-suggestion-result]').filter({ hasText: 'merge' }).waitFor();
  assert.match(await page.locator('[data-suggestion-result]').textContent(), /Sorting correct: Pass.*Temporal compatibility: Compatible/);
  await page.getByLabel('Operations per second', { exact: true }).fill('20');
  await page.getByRole('button', { name: 'Play', exact: true }).click();
  await page.waitForFunction(() => Number(document.querySelector('[data-replay-step]').dataset.replayStep) > 0);
  await page.getByRole('tablist', { name: 'Result views' }).getByRole('button', { name: 'SVG', exact: true }).click();
  // Allow any in-flight render to settle before checking the hidden host.
  await page.waitForTimeout(100);
  const pausedStep = await page.locator('[data-replay-step]').getAttribute('data-replay-step');
  await page.waitForTimeout(250);
  assert.equal(await page.locator('[data-replay-step]').getAttribute('data-replay-step'), pausedStep, 'Replay pauses while Ordering is hidden');
  await page.getByRole('button', { name: 'Ordering', exact: true }).click();
  assert.equal(await page.getByRole('button', { name: 'Play', exact: true }).isEnabled(), true);
  assert.equal(await page.locator('[data-replay-step]').getAttribute('data-replay-step'), pausedStep);
  assert.equal(await property('TS ID'), 'TS24');

  // Hold a real worker script request so cancellation is tested while a job is
  // pending rather than racing a fast completed sort.
  let pendingRequests = 0;
  const gate = new Promise(resolve => { releaseWorker = resolve; });
  await page.route('**/ordering-worker.js*', async route => {
    pendingRequests++;
    await gate;
    await route.continue().catch(() => {}); // A terminated worker can close its request.
  });
  await page.getByLabel('Algorithm', { exact: true }).selectOption('quick');
  await page.getByRole('button', { name: 'Build trace', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.ordering-sort-controls').textContent.includes('Building trace'));
  for (let attempt = 0; !pendingRequests && attempt < 50; attempt++) await page.waitForTimeout(20);
  assert.ok(pendingRequests, 'The pending worker request was held');
  await page.getByRole('button', { name: 'Cancel computation', exact: true }).click();
  assert.equal(await page.getByRole('button', { name: 'Build trace', exact: true }).isEnabled(), true);
  assert.equal(await page.getByRole('button', { name: 'Next operation', exact: true }).isDisabled(), true);
  releaseWorker();
  await page.waitForTimeout(300);
  assert.match(await page.locator('[data-suggestion-result]').textContent(), /merge/);
  assert.doesNotMatch(await page.locator('[data-suggestion-result]').textContent(), /quick/);
  assert.equal(await page.getByRole('button', { name: 'Next operation', exact: true }).isDisabled(), true,
    'Canceled worker cannot publish a late replay result');
  await page.getByRole('button', { name: 'Arrange', exact: true }).click();
  await page.getByRole('button', { name: 'Save Arrangement', exact: true }).click();
  const record = await savedRecord();
  assert.deepEqual(record.order, expected.user, 'Replay and cancellation never replace User Adjusted');
  assert.equal(record.order.length, count);
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, fixture: 'authored scaling fixture based on real response shape', segments: count,
    pages: Math.ceil(count / 24), maximumRenderedBlocks: 24, invalidDragPreview: true, boundaryKeyboardFocus: true,
    completeLogicalPersistence: true, hiddenReplayPaused: true, unsavedTabPreservation: true, pendingWorkerCancellation: true }));
} finally {
  releaseWorker?.();
  await browser.close();
}
