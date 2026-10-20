import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const site = (process.env.TYM_UI_URL || 'http://127.0.0.1:8780').replace(/\/$/, '');
const output = path.resolve(import.meta.dirname, '../../build_verify/i18n');
await mkdir(output, { recursive: true });
const text = 'CAPITOLUL UNU\nAna a spus: Legend.\n\nCAPITOLUL DOI\nȘtefan a plecat la Iași.\n';
const boundary = text.indexOf('CAPITOLUL DOI');
const book = { id: 'i18n-fixture', label: 'Carte de test / Authored book', source: 'Authored fixture', language: 'ro', character_count: text.length,
  sections: [{ id: 's1', kind: 'chapter', label: 'CAPITOLUL UNU', start: 0, end: boundary, character_count: boundary },
    { id: 's2', kind: 'chapter', label: 'CAPITOLUL DOI', start: boundary, end: text.length, character_count: text.length - boundary }] };
// Authored presentation fixture; NLP correctness is covered by the real API regression suites.
const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="960" height="490" viewBox="0 0 960 490" role="img" aria-label="TYM time-yard diagram">
<rect width="960" height="490" fill="white"/>
<g data-section="text-order" data-height="100"><title>Text order overview; large diagrams show evenly spaced segment IDs.</title><path d="M50 55H900" stroke="black"/><text x="50" y="40">TS1</text></g>
<g class="time-track" data-track-id="TT1" data-height="120"><title>TT1</title><text x="24" y="24">TT1</text><path d="M40 50H900" stroke="black"/><g class="time-segment" data-segment-id="TS1"><title>TS1: Narration</title><text x="50" y="85">Narration</text></g><g data-tag-icon="REM"><title>Memory / flashback</title><circle cx="34" cy="80" r="5" fill="purple"/></g></g>
<g data-section="legend" data-height="180"><rect x="18" width="924" height="180" fill="#f9fafb"/><text x="36" y="25">Legend</text><text x="36" y="55">Solid line: narrative continuity</text><text x="490" y="55">Triangle: track start/stop</text><text x="36" y="85">REM: Memory / flashback</text></g></svg>`;
const result = { model_version: 'authored-i18n-fixture', track_generation: { density: 50 },
  diagram: { actors: [{ id: 'A1', name: 'Adam' }], events: [{ id: 'EV1', text: 'Narration', temporal_category: 'Past', relation_to_previous: 'Before', actors: ['Adam'], confidence: 0.9 }],
    segments: [{ id: 'TS1', text: 'Narration', track_id: 'TT1', actors: ['Adam'], temporal_anchor: '', event_ids: ['EV1'], type: 'NAR', confidence: 0.9 }],
    tracks: [{ id: 'TT1', name: 'Adam' }], relations: [], time_ml: { events: [{ id: 'e1', text: 'Narration', class: 'OCCURRENCE' }], timex3: [], tlinks: [] } },
  analysis: { character_count: 9, average_event_confidence: 0.9, average_segment_confidence: 0.9, segment_types: { NAR: 1 }, temporal_categories: { Past: 1 },
    issues: [{ code: 'SEGMENTS_WITHOUT_TIMEX', severity: 'info', message: '1 segment(s) have no explicit temporal anchor.', evidence: 'Temporal anchors are optional in TYM; use TIMEX3 annotations when exact time evidence is needed.' }] },
  render: { svg }, xml: '<TimeML><TEXT>Narration</TEXT></TimeML>', warnings: [],
  density_cache: { session_id: 'authored', pending_densities: [], views: {}, options: Array.from({ length: 10 }, (_, i) => ({ density: (i + 1) * 10, view_id: 'base', track_count: 1 })) } };
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const context = await browser.newContext({ locale: 'ro-RO', viewport: { width: 1440, height: 1050 } });
const requests = [], errors = [];
let catalogUnavailable = false, signedIn = false;
async function routes(target) {
  await target.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG={apiBaseUrl:${JSON.stringify(site)},examples:[]};` }));
  await target.route('**/v1/**', async route => {
    const request = route.request(), url = new URL(request.url());
    requests.push({ path: url.pathname, method: request.method(), body: request.postDataJSON() });
    let status = 200, data = {};
    if (url.pathname === '/v1/books') { status = catalogUnavailable ? 503 : 200; data = catalogUnavailable ? {} : { books: [book] }; }
    else if (url.pathname.endsWith('/config')) data = { enabled: true, email_enabled: true, email_delivery: 'local_test', text_requires_login: false };
    else if (url.pathname.endsWith('/entities')) data = { entities: [
      { name: 'Adam', label: 'PERSON', mention_count: 2, parts_of_speech: ['PROPN'] },
      { name: 'Bank of England', label: 'ORGANIZATION', mention_count: 1, parts_of_speech: ['NOUN', 'ADP', 'PROPN'], recognition_source: 'trained_ner' },
      { name: 'Quickly', label: 'PERSON', mention_count: 1, parts_of_speech: ['ADV'], recognition_source: 'trained_ner' }
    ], pos_filter: { provider: 'spaCy' } };
    else if (url.pathname.endsWith('/source')) {
      const section = book.sections.find(item => item.id === url.searchParams.get('sectionId'));
      data = { text: section ? text.slice(section.start, section.end) : text };
    } else if (url.pathname.endsWith('/diagrams')) data = result;
    else if (url.pathname.endsWith('/login')) {
      signedIn = request.postDataJSON().password === 'Authored-Password-2026!';
      status = signedIn ? 200 : 401;
      data = signedIn ? { access_token: 'authored-access', refresh_token: 'authored-refresh', expires_in: 900 } : {};
    } else if (url.pathname.endsWith('/me')) data = { email: 'reader@example.test', email_confirmed: true };
    else if (url.pathname.endsWith('/logout')) { status = 204; signedIn = false; }
    return route.fulfill({ status, contentType: 'application/json', ...(status === 204 ? {} : { body: JSON.stringify(data) }) });
  });
}
const page = await context.newPage();
page.on('pageerror', error => errors.push(error.message));
await routes(page);
const selectLocale = async value => {
  await page.locator('#interface-language').selectOption(value);
  await page.waitForFunction(locale => document.documentElement.lang === locale, value);
};
try {
  await page.goto(site);
  await page.getByRole('heading', { name: 'Date de intrare', exact: true }).waitFor();
  assert.equal(await page.locator('#interface-language').inputValue(), 'ro', 'Browser locale should initialize the UI');
  assert.equal(await page.locator('#language').inputValue(), 'en', 'Interface locale must not change the analysis language');
  const initialText = await page.locator('#text').inputValue();
  await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
  assert.equal(await page.locator('#highlight optgroup[label="Organizații"] option').textContent(), 'Bank of England (1)');
  assert.ok(!(await page.locator('#highlight').textContent()).includes('Quickly'));
  await selectLocale('en');
  assert.equal(await page.locator('#highlight optgroup[label="Organizations"] option').textContent(), 'Bank of England (1)');
  assert.equal(await page.title(), 'TYM Diagram UI');
  assert.equal(await page.locator('#text').inputValue(), initialText);
  await page.getByRole('button', { name: 'Generate diagram', exact: true }).click();
  await page.waitForFunction(() => document.querySelectorAll('#cached-density option:not([disabled])').length === 10);
  const beforeSwitch = requests.filter(request => request.method === 'POST').length;
  await page.locator('#cached-density').selectOption('100');
  await selectLocale('ro');
  await page.getByRole('heading', { name: 'Diagramă', exact: true }).waitFor();
  assert.equal(await page.title(), 'TYM - Diagrame temporale');
  assert.equal(await page.locator('#cached-density').inputValue(), '100');
  assert.equal(await page.locator('#language').inputValue(), 'en');
  assert.equal(await page.locator('#text').inputValue(), initialText);
  assert.equal(await page.locator('.diagram-area [data-section="legend"] text').first().textContent(), 'Legendă');
  assert.equal(await page.locator('.diagram-area .time-segment text').textContent(), 'Narration', 'Narrative text must not be translated');
  assert.equal(await page.locator('.diagram-area [data-tag-icon] title').textContent(), 'Amintire / retrospectivă');
  await page.getByRole('button', { name: 'Analiză', exact: true }).click();
  await page.getByRole('heading', { name: 'Categorii temporale', exact: true }).waitFor();
  assert.ok((await page.locator('.analysis-view').innerText()).includes('Segmente fără ancoră temporală explicită: 1.'));
  await page.locator('.result-toolbar').getByRole('button', { name: 'JSON', exact: true }).click();
  const data = JSON.parse(await page.locator('.code-view').textContent());
  assert.equal(data.diagram.events[0].text, 'Narration');
  assert.equal(data.diagram.events[0].temporal_category, 'Past');
  await page.getByRole('button', { name: 'SVG', exact: true }).last().click();
  assert.equal(requests.filter(request => request.method === 'POST').length, beforeSwitch, 'Switching locales or cached views must not rerun NLP');
  // Translation and export are pure presentation transformations of the cached response.
  assert.equal(result.render.svg, svg);
  await page.locator('#hosted-book').selectOption(book.id);
  await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('CAPITOLUL UNU'));
  assert.deepEqual(await page.locator('#chapter option').allTextContents(), ['Capitolul 1', 'Capitolul 2']);
  await page.locator('#chapter').selectOption('1');
  await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('Ștefan'));
  const sourceReads = requests.filter(request => request.path.endsWith('/source')).length;
  await selectLocale('en');
  assert.deepEqual(await page.locator('#chapter option').allTextContents(), ['Chapter 1', 'Chapter 2']);
  assert.equal(await page.locator('.source-prose').textContent(), text.slice(boundary));
  assert.equal(await page.locator('.source-prose').getAttribute('lang'), 'ro');
  assert.equal(await page.locator('#chapter').inputValue(), '1');
  assert.equal(requests.filter(request => request.path.endsWith('/source')).length, sourceReads, 'Locale switching must not refetch source text');
  await page.reload();
  await page.getByRole('heading', { name: 'Input', exact: true }).waitFor();
  assert.equal(await page.locator('#interface-language').inputValue(), 'en', 'Saved preference overrides the Romanian browser locale');
  await selectLocale('ro');
  await page.getByRole('button', { name: 'Autentificare', exact: true }).click();
  await page.getByRole('button', { name: 'Creați un cont', exact: true }).click();
  await page.locator('#account-email').fill('reader@example.test');
  await page.locator('#account-password').fill('Authored-Password-2026!');
  await page.locator('#account-confirm').fill('Authored-Password-2026!');
  await page.getByRole('button', { name: 'Creare cont', exact: true }).click();
  await page.getByText('Confirmarea a fost salvată local pentru testare. Nu a fost trimis niciun e-mail.', { exact: true }).waitFor();
  await page.locator('#account-password').fill('Wrong-Password-2026!');
  await page.getByRole('button', { name: 'Autentificare prin e-mail', exact: true }).click();
  await page.getByText('Autentificarea a eșuat. Verificați datele de acces și confirmarea e-mailului.', { exact: true }).waitFor();
  await page.locator('#account-password').fill('Authored-Password-2026!');
  await page.getByRole('button', { name: 'Autentificare prin e-mail', exact: true }).click();
  await page.getByRole('button', { name: 'Deconectare', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Deconectare', exact: true }).click();
  await page.getByRole('button', { name: 'Autentificare', exact: true }).waitFor();
  // A storage event updates another open tab without losing the current work.
  const other = await context.newPage(); await routes(other); await other.goto(site);
  await other.getByRole('heading', { name: 'Date de intrare', exact: true }).waitFor();
  await selectLocale('en');
  await other.getByRole('heading', { name: 'Input', exact: true }).waitFor();
  await other.close();
  for (const locale of ['en', 'ro']) {
    await selectLocale(locale);
    for (const width of [320, 390, 768, 1440, 1920]) {
      await page.setViewportSize({ width, height: 1050 });
      await page.waitForFunction(width => document.querySelector('[data-testid="window-resolution"]')?.textContent
        === window.TymI18n.t('Window {width} x {height}', { width, height: 1050 }), width);
      await page.waitForFunction(() => !document.querySelector('.actions .primary').disabled);
      await page.locator('.actions .primary').click();
      await page.waitForFunction(() => document.querySelector('.diagram-area svg') && document.querySelector('.status-dot.ready'));
      await page.evaluate(() => document.fonts.ready);
      assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${locale}: page overflow at ${width}`);
      assert.ok(await page.locator('.zoom-tools .tool').evaluateAll(buttons => buttons.every(button => button.scrollWidth <= button.clientWidth + 1)), `${locale}: clipped zoom label at ${width}`);
      // Full-page capture can temporarily resize the window and invalidate resolution-keyed diagrams.
      await page.evaluate(() => scrollTo(0, 0));
      await page.screenshot({ path: path.join(output, `${locale}-${width}.png`) });
      assert.equal(await page.locator('.diagram-area svg').count(), 1, 'Screenshot must preserve the current window resolution');
      if (width < 980) {
        await page.locator('.result-panel').evaluate(element => element.scrollIntoView({ block: 'start' }));
        await page.screenshot({ path: path.join(output, `${locale}-${width}-diagram.png`) });
      }
    }
  }
  await page.setViewportSize({ width: 390, height: 1050 });
  await page.getByRole('button', { name: 'Autentificare', exact: true }).click();
  await page.getByRole('button', { name: 'Ați uitat parola?', exact: true }).click();
  await page.getByRole('heading', { name: 'Resetare parolă', exact: true }).waitFor();
  await page.screenshot({ path: path.join(output, 'ro-reset-390.png') });
  await page.getByRole('button', { name: 'Închideți autentificarea' }).click();
  catalogUnavailable = true;
  await page.getByRole('button', { name: 'Reîncărcați biblioteca', exact: true }).click();
  await page.getByText('Catalogul de cărți nu este disponibil (HTTP 503).', { exact: true }).waitFor();
  await selectLocale('en');
  await page.getByText('Book catalog unavailable (HTTP 503).', { exact: true }).waitFor();
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, locales: 2, responsive_cases: 10, browser_locale_detected: true,
    saved_locale_and_cross_tab_sync: true, source_and_analysis_language_preserved: true, cached_diagram_preserved: true,
    no_nlp_on_locale_change: true, localized_legend_without_source_changes: true, account_messages_and_errors: true,
    real_mail_sent: false, screenshots: output }, null, 2));
} catch (failure) {
  console.error(JSON.stringify({ browser_errors: errors, url: page.url() }));
  await page.screenshot({ path: path.join(output, 'failure.png'), fullPage: true });
  throw failure;
} finally { await context.close(); await browser.close(); }
