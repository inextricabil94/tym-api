import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const cache = require('../../ui/Tym.Ui/wwwroot/density-cache.js');
const site = (process.env.TYM_UI_URL || 'http://127.0.0.1:8791').replace(/\/$/, '');
const output = path.resolve(import.meta.dirname, '../../build_verify/segmentation-browser');
await mkdir(output, { recursive: true });
const fixtures = {};
for (const language of ['en', 'ro']) for (const algorithm of ['article_rules', 'mlnet_logistic', 'mlnet_perceptron'])
  fixtures[`${language}/${algorithm}`] = JSON.parse(await readFile(path.resolve(import.meta.dirname,
    `../../build_verify/segmentation-fixtures/${language}-${algorithm}.json`), 'utf8'));
const sourceText = fixtures['en/mlnet_logistic'].text;
const prefix = 'Front matter\n';
const book = { id: 'authored-book', label: 'Authored chapter fixture', language: 'en', character_count: prefix.length + sourceText.length,
  source: 'Authored fixture', sections: [
    { id: 'front', label: 'Preface', kind: 'front_matter', start: 0, end: prefix.length, character_count: prefix.length },
    { id: 'chapter', label: 'TWO', kind: 'chapter', start: prefix.length, end: prefix.length + sourceText.length, character_count: sourceText.length }
  ] };
let requests = [], sourceReads = 0, restricted = false, legacy = false, longFixture = null;
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ serviceWorkers: 'block', viewport: { width: 1440, height: 1050 }, locale: 'en-GB' });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG={apiBaseUrl:${JSON.stringify(site)},examples:[]};` }));
await page.route('**/v1/**', async route => {
  const url = new URL(route.request().url());
  let data;
  if (url.pathname === '/v1/books') data = { books: [book] };
  else if (url.pathname.endsWith('/config')) data = { enabled: false, text_requires_login: restricted };
  else if (url.pathname.endsWith('/source')) {
    sourceReads++;
    data = { text: sourceText, start: prefix.length, end: prefix.length + sourceText.length };
  } else if (url.pathname.endsWith('/entities')) data = { entities: [], pos_filter: { provider: 'spaCy' } };
  else if (url.pathname.endsWith('/diagrams')) {
    const payload = route.request().postDataJSON();
    requests.push(payload);
    data = longFixture || fixtures[`${payload.options.language}/${payload.options.segmentation_algorithm}`].response;
    if (legacy) data = { ...data, segmentation: undefined };
  } else throw new Error(`Unexpected request: ${url.pathname}`);
  await route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) });
});
const locale = value => page.locator(`#interface-language [data-locale="${value}"]`).click();
async function generate() {
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
  await page.locator('.actions .primary').click();
  await page.locator('.segment-overview svg').waitFor();
}
async function inspect(language) {
  await page.getByRole('button', { name: language === 'ro' ? 'Segmentare' : 'Segmentation', exact: true }).click();
  await page.locator('.source-segment').first().waitFor();
}
const markedText = () => page.locator('.segmented-source').evaluate(element => [...element.querySelectorAll('.source-gap,.source-segment')].map(part => part.textContent).join(''));
try {
  await page.goto(site);
  assert.equal(await page.locator('#segmentation-algorithm option').count(), 3);
  assert.equal(await page.locator('#segmentation-algorithm').inputValue(), 'article_rules');
  let cases = 0;
  for (const width of [320, 390, 1440]) {
    await page.setViewportSize({ width, height: 1050 });
    for (const language of ['en', 'ro']) for (const algorithm of ['article_rules', 'mlnet_logistic', 'mlnet_perceptron']) {
      const fixture = fixtures[`${language}/${algorithm}`];
      await locale(language);
      await page.locator('#language').selectOption(language);
      await page.locator('#text').fill(fixture.text);
      await page.locator('#segmentation-algorithm').selectOption(algorithm);
      await generate();
      assert.equal(requests.at(-1).options.segmentation_algorithm, algorithm);
      assert.equal(requests.at(-1).text, fixture.text);
      await inspect(language);
      assert.equal(await markedText(), fixture.text);
      assert.equal(await page.locator('.source-segment').count(), fixture.response.diagram.segments.length);
      await page.locator('.segment-marker').nth(1).click();
      assert.equal(await page.locator('.segment-marker[aria-pressed="true"]').textContent(), fixture.response.diagram.segments[1].id);
      if (algorithm === 'mlnet_perceptron') assert.ok(!(await page.getByTestId('boundary-score').textContent()).includes('%'));
      if (algorithm === 'mlnet_logistic') assert.ok((await page.getByTestId('boundary-score').textContent()).includes('%'));
      const before = requests.length;
      await page.locator('#cached-density').selectOption('100');
      assert.equal(await markedText(), fixture.text);
      assert.equal(requests.length, before, 'Density selection repeated NLP');
      const selected = cache.select(fixture.response, 100).diagram.segments[1];
      await page.locator('.segment-track-link').click();
      await page.locator(`.diagram-area g.time-track[data-track-id="${selected.track_id}"]`).waitFor();
      await inspect(language);
      for (const theme of ['dark', 'light']) {
        if (await page.locator('html').getAttribute('data-theme') !== theme) await page.locator('.theme-toggle').click();
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Page overflow: ${width}/${language}/${algorithm}/${theme}`);
        assert.ok(await page.locator('.segment-properties dd').evaluateAll(elements => elements.every(element => element.scrollWidth <= element.clientWidth + 1)));
        cases++;
      }
      if (language === 'ro' && algorithm === 'mlnet_logistic' && width !== 320) {
        await page.locator('.segmentation-view').scrollIntoViewIfNeeded();
        await page.screenshot({ path: path.join(output, `marked-${width}.png`) });
      }
    }
  }
  await locale('en');
  await page.locator('#segmentation-algorithm').selectOption('article_rules');
  await page.waitForFunction(() => !document.querySelector('.source-segment'));
  assert.equal(await page.locator('#cached-density').isDisabled(), true, 'Algorithm change retained old cached diagrams');

  await page.locator('#segmentation-algorithm').selectOption('mlnet_logistic');
  await page.locator('#hosted-book').selectOption(book.id);
  await page.locator('.source-prose').waitFor();
  await generate();
  assert.equal(requests.at(-1).section_id, 'chapter');
  await inspect('en');
  assert.equal(await markedText(), sourceText, 'Chapter markers used whole-book offsets');

  // Large UI fixture exercises paging only; it is not a model-quality sample.
  const text = Array.from({ length: 45 }, (_, i) => `Event ${i}.\n`).join('');
  let cursor = 0;
  const segments = Array.from({ length: 45 }, (_, i) => {
    const start = cursor, end = text.indexOf('\n', cursor); cursor = end + 1;
    return { id: `TS${i + 1}`, track_id: 'TT1', type: 'NAR', span_start: start, span_end: end, event_ids: [] };
  });
  longFixture = { ...fixtures['en/mlnet_logistic'].response, diagram: { ...fixtures['en/mlnet_logistic'].response.diagram, segments } };
  await page.locator('.input-panel .tabs').getByRole('button', { name: 'English', exact: true }).click();
  await page.locator('#text').fill(text);
  await generate(); await inspect('en');
  const contents = [await markedText()];
  assert.equal(await page.locator('.source-segment').count(), 20);
  await page.getByRole('button', { name: 'Next segments', exact: true }).click();
  contents.push(await markedText());
  await page.getByRole('button', { name: 'Next segments', exact: true }).click();
  contents.push(await markedText());
  assert.equal(await page.locator('.source-segment').count(), 5);
  assert.equal(contents.join(''), text);
  assert.equal(await page.getByRole('button', { name: 'Next segments', exact: true }).isDisabled(), true);
  longFixture = null;

  legacy = true;
  await page.locator('#text').fill(fixtures['en/mlnet_logistic'].text);
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
  await page.getByRole('button', { name: 'Regenerate diagram', exact: true }).click();
  await page.locator('.message').filter({ hasText: 'The API did not apply the selected segmentation algorithm.' }).waitFor();
  assert.equal(await page.locator('.segment-overview svg').count(), 0);
  legacy = false;

  restricted = true;
  await page.reload();
  await page.locator('#hosted-book').selectOption(book.id);
  await page.getByRole('button', { name: 'Sign in to read text', exact: true }).waitFor();
  const reads = sourceReads;
  await page.locator('#segmentation-algorithm').selectOption('mlnet_logistic');
  await generate();
  await page.getByRole('button', { name: 'Segmentation', exact: true }).click();
  await page.getByText('Source text is unavailable or its offsets do not match.', { exact: true }).waitFor();
  assert.equal(await page.locator('.segmented-source').count(), 0);
  assert.equal(sourceReads, reads, 'Segmentation bypassed source access restrictions');
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ responsiveCases: cases, algorithmCount: 3, languages: ['en', 'ro'], sourcePaging: true,
    chapterOffsets: true, cacheInvalidation: true, legacyApiRejected: true, sourceAccessRespected: true, browserErrors: errors.length }));
} finally { await browser.close(); }
