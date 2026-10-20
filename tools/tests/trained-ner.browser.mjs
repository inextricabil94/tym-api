import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(import.meta.dirname, '../..');
const socket = createServer();
await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve));
const port = socket.address().port;
await new Promise(resolve => socket.close(resolve));
const api = `http://127.0.0.1:${port}`;
const server = spawn('dotnet', [path.join(root, 'bin/Release/net9.0/Tym.Api.dll'), '--urls', api], {
  cwd: root, windowsHide: true, stdio: 'ignore', env: { ...process.env, TYM_AUTH_ENABLED: 'false', TYM_LIBRARY_PATH: '' }
});
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 }, locale: 'en-GB' });
page.setDefaultTimeout(60000);
const errors = [], payloads = [];
page.on('pageerror', error => errors.push(error.message));
page.on('response', async response => {
  if (response.url() === `${api}/v1/entities` && response.ok()) payloads.push(await response.json());
});
await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript',
  body: `window.TYM_CONFIG={apiBaseUrl:${JSON.stringify(api)},examples:[]};` }));
try {
  for (let attempt = 0; attempt < 100; attempt++) {
    try { if ((await fetch(`${api}/health`)).ok) break; } catch {}
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  await page.goto(process.env.TYM_UI_URL || 'http://127.0.0.1:8780/');
  for (const fixture of [
    { language: 'en', locale: 'en', text: 'John Smith arrived at the Bank of England in London. He waited there.', org: 'Bank of England', group: 'Organizations' },
    { language: 'ro', locale: 'ro', text: 'Ion Popescu a mers la Banca Națională a României din București. El a plecat.', org: 'Banca Națională a României', group: 'Organizații' }
  ]) {
    await page.locator('#interface-language').selectOption(fixture.locale);
    await page.locator('#language').selectOption(fixture.language);
    await page.locator('#text').fill(fixture.text);
    await page.waitForFunction(name => [...document.querySelectorAll('#highlight option')].some(option => option.value.includes(name)), fixture.org);
    const option = page.locator(`#highlight optgroup[label="${fixture.group}"] option`).filter({ hasText: fixture.org });
    const value = await option.getAttribute('value');
    await page.locator('#highlight').selectOption(value);
    const response = payloads.findLast(data => data.language === fixture.language && data.entities.some(entity => entity.name === value));
    assert.ok(response?.ner.pipeline_components.includes('ner'));
    assert.equal(response.method, 'spacy-trained-ner+pos+rule-anaphora-en-ro-v4');
    const entity = response.entities.find(entity => entity.name === value);
    assert.equal(entity.recognition_source, 'trained_ner');
    assert.equal(fixture.text.slice(entity.span_start, entity.span_end), value);
    assert.ok(entity.parts_of_speech.some(pos => !['PROPN', 'PRON'].includes(pos)), 'Complete organization must preserve internal POS');
    assert.equal(await page.locator('#text').inputValue(), fixture.text);
    const generation = page.waitForResponse(response => response.url() === `${api}/v1/diagrams` && response.request().method() === 'POST');
    await page.locator('.actions .primary').click();
    const generated = await generation;
    assert.equal(generated.status(), 200);
    const diagram = await generated.json();
    assert.ok(diagram.render.svg.includes('stroke-width="5"'), `Missing organization highlight in ${fixture.language} API SVG: ${value}`);
    await page.locator('.diagram-area svg').waitFor();
    await page.waitForFunction(() => document.querySelector('.diagram-area svg line[stroke-width="5"]'));
  }
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, languages: 2, actual_trained_ner: true, whole_organization_spans: true,
    organization_highlighting: true, original_text_preserved: true, browser_errors: errors.length }));
} finally {
  await browser.close();
  server.kill();
  await new Promise(resolve => server.exitCode !== null ? resolve() : server.once('exit', resolve));
}
