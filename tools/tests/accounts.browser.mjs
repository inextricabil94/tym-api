import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdir, writeFile, readFile, readdir } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import path from 'node:path';
const require = createRequire(import.meta.url);
const { chromium } = require(process.env.TYM_PLAYWRIGHT_MODULE || 'playwright');
const root = path.resolve(import.meta.dirname, '../..');
const folder = path.join(root, 'build_verify', `accounts-${randomUUID()}`);
const library = path.join(folder, 'library'), pickup = path.join(folder, 'mail');
await mkdir(library, { recursive: true });
const text = 'CAPITOLUL UNU\n\u0218tefan a venit. \ud83d\ude42\n\nCAPITOLUL DOI\nAna a plecat.\n';
const boundary = text.indexOf('CAPITOLUL DOI');
const book = { id: 'fixture', label: 'Authored Romanian account fixture', language: 'ro', source: 'Authored test',
  character_count: text.length, original_character_count: text.length, sha256_utf8: createHash('sha256').update(text).digest('hex'),
  sections: [{ id: 's1', label: 'CAPITOLUL UNU', kind: 'chapter', start: 0, end: boundary, character_count: boundary },
    { id: 's2', label: 'CAPITOLUL DOI', kind: 'chapter', start: boundary, end: text.length, character_count: text.length - boundary }] };
await writeFile(path.join(library, 'fixture.txt'), text);
await writeFile(path.join(library, 'catalog.json'), JSON.stringify([book]));
const socket = createServer();
await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve));
const port = socket.address().port;
await new Promise(resolve => socket.close(resolve));
const api = `http://127.0.0.1:${port}`;
const requiresLogin = process.env.TYM_TEST_PUBLIC_READING !== 'true';
const server = spawn('dotnet', [path.join(root, 'bin/Release/net9.0/Tym.Api.dll'), '--urls', api], {
  cwd: root, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development', TYM_AUTH_ENABLED: 'true', TYM_AUTH_DATA_PATH: path.join(folder, 'accounts'),
    TYM_REQUIRE_LOGIN_FOR_TEXT: String(requiresLogin),
    TYM_SMTP_HOST: '', TYM_SMTP_FROM: '', TYM_SMTP_USER: '', TYM_SMTP_PASSWORD: '',
    TYM_LIBRARY_PATH: library, TYM_PUBLIC_API_URL: api, TYM_PUBLIC_UI_URL: 'http://127.0.0.1:8780', TYM_MAIL_PICKUP_PATH: pickup,
    TYM_GOOGLE_CLIENT_ID: 'authored-test.apps.googleusercontent.com' }
});
let logs = ''; server.stdout.on('data', chunk => { logs += chunk; }); server.stderr.on('data', chunk => { logs += chunk; });
const browser = await chromium.launch({ headless: true, ...(process.env.TYM_CHROMIUM ? { executablePath: process.env.TYM_CHROMIUM } : {}) });
const page = await browser.newPage({ viewport: { width: 1440, height: 1050 } });
const errors = []; page.on('pageerror', failure => errors.push(failure.message));
let lastMail = '';
async function mail() {
  const files = await readdir(pickup);
  const next = files.find(file => file !== lastMail);
  assert.ok(next, 'Expected a new email');
  lastMail = next;
  const encoded = await readFile(path.join(pickup, next), 'utf8');
  const decoded = /Content-Transfer-Encoding: quoted-printable/i.test(encoded)
    ? encoded.replace(/=\r?\n/g, '').replace(/=([A-F\d]{2})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16))) : encoded;
  return decoded.replaceAll('&amp;', '&');
}
function request(endpoint, body, access, extra = {}) {
  return fetch(api + endpoint, { method: body === undefined ? 'GET' : 'POST',
    headers: { ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...(access ? { Authorization: `Bearer ${access}` } : {}), ...extra },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
}
const email = 'reader@example.test', password = 'TYM-Test-Password-27!', newPassword = 'TYM-New-Password-28!';
const tokenOf = data => data.accessToken ?? data.access_token;
try {
  let ready = false;
  for (let i = 0; i < 200; i++) {
    try { if ((await fetch(api + '/health')).ok) { ready = true; break; } } catch {}
    if (server.exitCode !== null) throw new Error(logs);
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.ok(ready, logs);
  assert.equal((await request('/v1/account/register', { email: 'large@example.test', password: 'x'.repeat(17000) })).status, 413);
  const guest = await request('/v1/books/fixture/source');
  assert.equal(guest.status, requiresLogin ? 401 : 200); assert.equal(guest.headers.get('cache-control'), 'no-store');
  if (!requiresLogin) assert.equal((await guest.json()).text, text);
  assert.equal((await request('/v1/books/fixture/source', undefined, 'forged-token')).status, requiresLogin ? 401 : 200);
  assert.equal((await request('/v1/account/me', undefined, 'forged-token')).status, 401);
  assert.equal((await (await request('/v1/account/config')).json()).text_requires_login, requiresLogin);
  const evil = await fetch(api + '/v1/books/fixture/source', { method: 'OPTIONS', headers: {
    Origin: 'https://evil.example', 'Access-Control-Request-Method': 'GET', 'Access-Control-Request-Headers': 'authorization' } });
  assert.equal(evil.headers.get('access-control-allow-origin'), requiresLogin ? null : '*');
  const allowed = await fetch(api + '/v1/books/fixture/source', { method: 'OPTIONS', headers: {
    Origin: 'http://127.0.0.1:8780', 'Access-Control-Request-Method': 'GET', 'Access-Control-Request-Headers': 'authorization' } });
  assert.equal(allowed.headers.get('access-control-allow-origin'), requiresLogin ? 'http://127.0.0.1:8780' : '*');
  const accountOrigin = await fetch(api + '/v1/account/login', { method: 'OPTIONS', headers: {
    Origin: 'https://evil.example', 'Access-Control-Request-Method': 'POST', 'Access-Control-Request-Headers': 'content-type' } });
  assert.equal(accountOrigin.headers.get('access-control-allow-origin'), null);
  const registered = await request('/v1/account/register', { email, password });
  assert.equal(registered.status, 200, await registered.text());
  assert.equal((await request('/v1/account/login', { email, password })).status, 401, 'Unconfirmed email must not sign in');
  const confirmation = (await mail()).match(/http:\/\/127\.0\.0\.1:\d+\/v1\/account\/confirmEmail[^\s<"]+/)?.[0];
  assert.ok(confirmation, 'Missing canonical confirmation link');
  assert.equal((await fetch(confirmation)).status, 200);
  const signedIn = await request('/v1/account/login', { email, password });
  assert.equal(signedIn.status, 200);
  const access = tokenOf(await signedIn.json());
  const full = await request('/v1/books/fixture/source', undefined, access);
  assert.equal(full.status, 200); assert.equal(full.headers.get('cache-control'), 'no-store');
  assert.equal((await full.json()).text, text);
  const part = await (await request('/v1/books/fixture/source?sectionId=s2', undefined, access)).json();
  assert.equal(part.text, text.slice(boundary)); assert.equal(part.start, boundary); assert.equal(part.end, text.length);
  assert.equal(part.sha256_utf8, book.sha256_utf8);
  assert.equal((await request('/v1/books/fixture/source?sectionId=missing', undefined, access)).status, 404);
  const nonce = await (await request('/v1/account/google/nonce', {})).json();
  assert.equal((await request('/v1/account/google', { credential: 'forged.jwt.token', nonce: nonce.nonce })).status, 401);
  assert.equal((await request('/v1/account/google', { credential: 'forged.jwt.token', nonce: nonce.nonce })).status, 401);
  await page.route('**/config.js', route => route.fulfill({ contentType: 'application/javascript', body: `window.TYM_CONFIG = {apiBaseUrl:${JSON.stringify(api)},examples:[]};` }));
  // Do not invoke a real Google account in this authored integration test.
  await page.route('**/v1/account/config', route => route.fulfill({ contentType: 'application/json', body: JSON.stringify({
    enabled: true, email_enabled: true, email_delivery: 'local_test', text_requires_login: requiresLogin }) }));
  await page.goto(process.env.TYM_UI_URL || 'http://127.0.0.1:8780/');
  await page.locator('#hosted-book').selectOption('fixture');
  assert.deepEqual(await page.locator('#chapter option').allTextContents(), ['Capitolul 1', 'Capitolul 2']);
  if (requiresLogin) {
    assert.equal(await page.locator('.source-prose').count(), 0);
    await page.getByRole('button', { name: 'Sign in to read text', exact: true }).click();
  } else {
    await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('CAPITOLUL UNU'));
    assert.equal(await page.getByRole('button', { name: 'Sign in to read text', exact: true }).count(), 0);
    await page.getByRole('button', { name: 'Sign In', exact: true }).click();
  }
  await page.locator('#account-email').fill(email); await page.locator('#account-password').fill(password);
  await page.getByText('Local test mail only. Messages are saved on this computer, not sent to an inbox.', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Sign In With Email', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('CAPITOLUL UNU'));
  await page.locator('#chapter').selectOption('1');
  await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('CAPITOLUL DOI'));
  assert.equal(await page.locator('.source-prose').textContent(), text.slice(boundary));
  await page.getByRole('radio', { name: 'Full text', exact: true }).click();
  await page.waitForFunction(() => document.querySelector('.source-prose')?.textContent.includes('CAPITOLUL UNU'));
  assert.equal(await page.locator('.source-prose').textContent(), text);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1050 });
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1));
    await page.screenshot({ path: path.join(root, `build_verify/${requiresLogin ? 'accounts' : 'public'}-reader-${width}.png`), fullPage: true });
  }
  const signedOut = page.waitForResponse(response => response.url().endsWith('/v1/account/logout'));
  await page.getByRole('button', { name: 'Sign Out', exact: true }).click();
  assert.equal((await signedOut).status(), 204);
  assert.equal(await page.locator('.source-prose').count(), requiresLogin ? 0 : 1);
  if (!requiresLogin) assert.equal(await page.locator('.source-prose').textContent(), text);
  assert.equal((await request('/v1/account/me', undefined, access)).status, 403, 'Logout must revoke old tokens');
  assert.equal((await request('/v1/books/fixture/source', undefined, access)).status, requiresLogin ? 403 : 200);
  assert.equal((await request('/v1/account/login?useCookies=true', { email, password })).status, 400);
  const secondLogin = await (await request('/v1/account/login', { email, password })).json();
  assert.equal((await request('/v1/account/forgotPassword', { email })).status, 200);
  const resetLink = (await mail()).match(/http:\/\/127\.0\.0\.1:8780\/#resetEmail=[^\s<"]+/)?.[0];
  assert.ok(resetLink);
  const reset = new URLSearchParams(new URL(resetLink).hash.slice(1));
  await page.goto(resetLink);
  assert.equal(await page.locator('#account-email').inputValue(), email);
  await page.locator('#account-password').fill(newPassword);
  await page.locator('#account-confirm').fill(newPassword);
  const resetResponse = page.waitForResponse(response => response.url().endsWith('/v1/account/resetPassword'));
  await page.getByRole('button', { name: 'Save Password', exact: true }).click();
  assert.equal((await resetResponse).status(), 200);
  await page.getByText('Password updated. You can sign in.', { exact: true }).waitFor();
  assert.ok(!page.url().includes('resetCode='), 'Reset credential must be removed from the address bar');
  assert.equal((await request('/v1/account/me', undefined, tokenOf(secondLogin))).status, 403);
  assert.equal((await request('/v1/account/login', { email, password: newPassword })).status, 200);
  assert.deepEqual(errors, []);
  console.log(JSON.stringify({ passed: true, text_requires_login: requiresLogin, guest_text_blocked: requiresLogin, forged_account_token_rejected: true,
    confirmed_email_required: true, utf16_exact_source: true, chapter_labels_numeric: true,
    logout_reading_policy_preserved: true, password_reset_revokes: true, strict_account_cors: true,
    reader_viewports: 2, real_google_or_smtp_tested: false }, null, 2));
} catch (error) { console.error(logs.slice(-6000)); throw error; }
finally {
  await browser.close(); server.kill();
  if (server.exitCode === null) await new Promise(resolve => server.once('exit', resolve));
}
