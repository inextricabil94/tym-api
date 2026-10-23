import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { mkdir, mkdtemp } from 'node:fs/promises';
import path from 'node:path';
const root = path.resolve(import.meta.dirname, '../..');
await mkdir(path.join(root, 'build_verify'), { recursive: true });
const directory = await mkdtemp(path.join(root, 'build_verify/database-cache-'));
const socket = createServer(); await new Promise(resolve => socket.listen(0, '127.0.0.1', resolve));
const port = socket.address().port; await new Promise(resolve => socket.close(resolve));
const api = `http://127.0.0.1:${port}`;
const dll = process.env.TYM_API_DLL || path.join(root, 'build_verify/offline-checks/Tym.Api.dll');
let server;
async function start() {
  server = spawn('dotnet', [dll, '--urls', api], { cwd: root, windowsHide: true, stdio: 'ignore',
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', TYM_AUTH_ENABLED: 'false', TYM_REQUIRE_LOGIN_FOR_TEXT: 'false',
      TYM_LIBRARY_PATH: '', TYM_DIAGRAM_CACHE_PATH: path.join(directory, 'diagrams.db') } });
  for (let index = 0; index < 100; index++) {
    try { if ((await fetch(api + '/health')).ok) return; } catch {}
    if (server.exitCode !== null) throw new Error('API exited during startup');
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  throw new Error('API did not start');
}
async function stop() { if (server && server.exitCode === null) { const ended = new Promise(resolve => server.once('exit', resolve)); server.kill(); await ended; } }
const payload = { text: 'Adam waited by the house. Years earlier, Karl traveled alone. Margaret remembered her mother.',
  options: { width: 1440, language: 'en', track_density: 50, progressive_density_cache: true, segmentation_algorithm: 'mlnet_logistic' } };
const post = () => fetch(api + '/v1/diagrams', { method: 'POST', headers: { 'Content-Type': 'application/json', Origin: 'https://workbench.example' }, body: JSON.stringify(payload) });
try {
  await start();
  let response = await post(); assert.equal(response.status, 200); assert.equal(response.headers.get('x-tym-cache'), 'MISS');
  assert.ok(response.headers.get('access-control-expose-headers').includes('X-TYM-Cache'));
  const first = await response.json(), views = new Map();
  for (const density of first.density_cache.pending_densities) {
    const result = await fetch(`${api}/v1/density-sessions/${first.density_cache.session_id}/${density}`);
    assert.equal(result.status, 200); views.set(density, (await result.json()).view);
  }
  response = await post(); assert.equal(response.headers.get('x-tym-cache'), 'HIT');
  const second = await response.json();
  assert.notEqual(first.density_cache.session_id, second.density_cache.session_id);
  assert.deepEqual(first.diagram, second.diagram);
  await stop(); await start();
  response = await post(); assert.equal(response.status, 200); assert.equal(response.headers.get('x-tym-cache'), 'HIT');
  const restarted = await response.json(); assert.deepEqual(restarted.diagram, first.diagram);
  assert.notEqual(restarted.density_cache.session_id, second.density_cache.session_id);
  for (const density of restarted.density_cache.pending_densities) {
    const result = await fetch(`${api}/v1/density-sessions/${restarted.density_cache.session_id}/${density}`);
    assert.equal(result.status, 200); assert.deepEqual((await result.json()).view, views.get(density));
  }
  console.log(JSON.stringify({ passed: true, databaseHit: true, survivesApiRestart: true, progressiveViews: 9,
    freshIpBoundSessions: true, corsCacheHeader: true, unchangedDiagrams: true }));
} finally { await stop(); }
