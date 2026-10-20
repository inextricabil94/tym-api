import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const site = (process.env.TYM_UI_URL || 'http://127.0.0.1:8780').replace(/\/$/, '');
const output = path.resolve(import.meta.dirname, '../../build_verify/diagram-panels');
await mkdir(output, { recursive: true });
const tracks = Array.from({ length: 13 }, (_, index) => ({ id: `TT${index + 1}`, name: `Track ${index + 1}` }));
const segments = tracks.map((track, index) => ({ id: `TS${index + 1}`, track_id: track.id, actors: ['Adam'], text: 'Adam waited.', type: 'NAR' }));
const strip = `<g data-section="text-order" data-height="158"><title>Text order overview; large diagrams show evenly spaced segment IDs.</title><path d="M112 76H1720" stroke="#111827"/>${segments.map((segment, index) => `<text x="${112 + index * 120}" y="54">${segment.id}</text>`).join('')}</g>`;
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1800" height="2700" viewBox="0 0 1800 2700" role="img"><rect width="1800" height="2700" fill="white"/><defs><symbol id="fixture-icon" viewBox="0 0 10 10"><path d="M0 0L10 5L0 10Z"/></symbol></defs>${strip}${tracks.map((track, index) => `<g class="time-track" data-track-id="${track.id}" data-height="160" transform="translate(0 ${180 + index * 180})"><title>${track.id}</title><text x="24" y="24">${track.id}</text><g class="time-segment" data-segment-id="TS${index + 1}"><line x1="112" y1="60" x2="1720" y2="60" stroke="#111827"/><text x="112" y="105">Adam waited.</text><use href="#fixture-icon" x="85" y="45" width="16" height="16"/></g></g>`).join('')}<g data-section="legend" data-height="100"><text x="36" y="32">Legend</text><text x="36" y="65">Solid line: narrative continuity</text></g></svg>`;
const result = { model_version: 'authored-panel-fixture', track_generation: { density: 50 },
  diagram: { actors: [{ id: 'A1', name: 'Adam' }], tracks, segments, events: [], relations: [], time_ml: { tlinks: [] } },
  render: { svg }, xml: '', density_cache: { views: {}, pending_densities: [],
    options: Array.from({ length: 10 }, (_, i) => ({ density: (i + 1) * 10, view_id: 'base', track_count: tracks.length })) } };
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'ro-RO' });
const posts = [], errors = [];
page.on('pageerror', error => errors.push(error.message));
await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG={apiBaseUrl:${JSON.stringify(site)},examples:[]};` }));
await page.route('**/v1/**', route => {
  const url = new URL(route.request().url());
  if (route.request().method() === 'POST') posts.push(url.pathname);
  const data = url.pathname === '/v1/books' ? { books: [] }
    : url.pathname.endsWith('/config') ? { enabled: false, text_requires_login: false }
      : url.pathname.endsWith('/entities') ? { entities: [{ name: 'Adam', label: 'PERSON', parts_of_speech: ['PROPN'], mention_count: 1 }], pos_filter: { provider: 'spaCy' } }
        : result;
  return route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) });
});
const locale = value => page.locator(`#interface-language [data-locale="${value}"]`).click();
const generate = async () => {
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
  await page.locator('.actions .primary').click();
  await page.locator('.segment-overview svg').waitFor();
};
try {
  await page.goto(site);
  assert.equal(await page.locator('select#interface-language, #interface-language select').count(), 0);
  await page.locator('#interface-language [data-locale="ro"][aria-pressed="true"]').waitFor();
  await page.waitForFunction(() => [...document.querySelectorAll('.locale-button img')].every(image => image.complete && image.naturalWidth > 0));
  assert.equal(await page.locator('.locale-button img').count(), 2);
  await page.locator('#interface-language [data-locale="en"]').focus();
  await page.locator('#interface-language [data-locale="en"]').press('Enter');
  assert.equal(await page.locator('#interface-language [data-locale="en"]').getAttribute('aria-pressed'), 'true');
  const text = await page.locator('#text').inputValue();
  await generate();
  const before = posts.length;
  const overview = await page.locator('.segment-overview svg').getAttribute('viewBox');
  assert.equal(await page.locator('.segment-overview g.time-track, .segment-overview [data-section="legend"]').count(), 0);
  assert.equal(await page.locator('.time-tracks-view [data-section="text-order"]').count(), 0);
  assert.equal(await page.locator('.segment-overview text').count(), 13);
  assert.equal(await page.locator('.diagram-area g.time-track').count(), 12);
  await page.getByRole('button', { name: 'Next tracks', exact: true }).click();
  assert.equal(await page.locator('.diagram-area g.time-track').getAttribute('data-track-id'), 'TT13');
  assert.equal(await page.locator('.segment-overview svg').getAttribute('viewBox'), overview);
  await page.getByRole('searchbox', { name: 'Filter time tracks', exact: true }).fill('no-such-track');
  assert.equal(await page.locator('.diagram-area svg').count(), 0);
  assert.equal(await page.locator('.segment-overview text').count(), 13);
  await page.getByRole('searchbox', { name: 'Filter time tracks', exact: true }).fill('');
  await page.locator('#track-groups').selectOption('10');
  await page.locator('#track-group').selectOption('9');
  assert.equal(await page.locator('.segment-overview text').count(), 13);
  await page.locator('#track-groups').selectOption('0');
  await page.locator('#cached-density').selectOption('10');
  const originalWidth = (await page.locator('.segment-overview svg').boundingBox()).width;
  await page.getByRole('button', { name: 'Zoom in', exact: true }).click();
  assert.equal((await page.locator('.segment-overview svg').boundingBox()).width, originalWidth);
  await page.getByRole('button', { name: 'Actual size (100%)', exact: true }).click();
  await page.locator('.segment-overview-area').evaluate(element => { element.scrollLeft = 250; });
  const stripPosition = await page.locator('.segment-overview-area').evaluate(element => ({ left: element.scrollLeft, top: element.getBoundingClientRect().top }));
  await page.locator('.diagram-area').evaluate(element => { element.scrollTop = 400; element.scrollLeft = 50; });
  assert.ok(await page.locator('.diagram-area').evaluate(element => element.scrollTop > 0));
  assert.deepEqual(await page.locator('.segment-overview-area').evaluate(element => ({ left: element.scrollLeft, top: element.getBoundingClientRect().top })), stripPosition);
  await page.getByRole('button', { name: 'Fit', exact: true }).click();
  assert.equal((await page.locator('.segment-overview svg').boundingBox()).width, originalWidth);
  await locale('ro');
  assert.equal(await page.locator('#segment-overview-title').textContent(), 'Segmente temporale (TS)');
  assert.equal(await page.locator('#time-tracks-title').textContent(), 'Piste temporale (TT)');
  assert.equal(await page.locator('#language').inputValue(), 'en');
  assert.equal(await page.locator('#text').inputValue(), text);
  assert.equal(await page.locator('#cached-density').inputValue(), '10');
  assert.equal(posts.length, before, 'Panel controls and language buttons must not repeat NLP');
  const downloadEvent = page.waitForEvent('download');
  await page.locator('.download-actions').getByRole('button', { name: 'SVG', exact: true }).click();
  const download = await downloadEvent;
  const exported = await readFile(await download.path(), 'utf8');
  assert.ok(exported.includes('data-section="text-order"'));
  assert.equal((exported.match(/class="time-track"/g) || []).length, 13);
  assert.ok(exported.includes('Legendă'));
  let cases = 0;
  for (const width of [320, 390, 768, 1440, 1920]) {
    await page.setViewportSize({ width, height: 1050 });
    await page.waitForFunction(() => !document.querySelector('.segment-overview svg'));
    await generate();
    for (const language of ['en', 'ro']) {
      await locale(language);
      for (const theme of ['dark', 'light']) {
        if (await page.locator('html').getAttribute('data-theme') !== theme) await page.locator('.theme-toggle').click();
        await page.evaluate(() => document.fonts.ready);
        assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Page overflow: ${width}/${language}/${theme}`);
        assert.ok(await page.locator('.locale-button').evaluateAll(buttons => buttons.every(button => button.scrollWidth <= button.clientWidth + 1)));
        const panels = await page.evaluate(() => ({ a: document.querySelector('.segment-overview').getBoundingClientRect().bottom,
          b: document.querySelector('.time-tracks-view').getBoundingClientRect().top }));
        assert.ok(panels.b >= panels.a - 1, 'TS and TT sections overlap');
        assert.equal(await page.locator('.time-tracks-view [data-section="text-order"]').count(), 0);
        assert.equal(await page.locator('.segment-overview text').count(), 13);
        cases++;
        if ([390, 1440].includes(width) && theme === 'dark') {
          await page.evaluate(() => scrollTo(0, 0));
          await page.screenshot({ path: path.join(output, `${language}-${width}-header.png`) });
          await page.locator('.segment-overview').evaluate(element => element.scrollIntoView({ block: 'start' }));
          await page.screenshot({ path: path.join(output, `${language}-${width}-panels.png`) });
        }
      }
    }
  }
  await locale('ro'); await page.reload();
  await page.locator('#interface-language [data-locale="ro"][aria-pressed="true"]').waitFor();
  assert.equal(await page.locator('#language').inputValue(), 'en');
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, responsive_locale_theme_cases: cases, flag_buttons: true, keyboard_and_persistence: true,
    separate_ts_tt_containers: true, independent_scrolling: true, overview_survives_filters_paging_zoom: true,
    no_extra_nlp: true, complete_svg_export: true, browser_errors: 0, screenshots: output }, null, 2));
} finally { await browser.close(); }
