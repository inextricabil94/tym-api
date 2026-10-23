'use strict';
const SHELL = 'tym-shell-20261023-selectors-2';
const ASSETS = ['/index.html', '/app.js', '/offline-cache.js', '/offline-register.js', '/i18n.js', '/styles.css',
  '/fonts.css', '/theme.js', '/accounts.js', '/chapter-splitter.js', '/track-groups.js', '/density-cache.js',
  '/segmentation.js', '/viewport.js', '/assets/vendor/react.production.min.js', '/assets/vendor/react-dom.production.min.js',
  '/assets/flags/gb.svg', '/assets/flags/ro.svg', '/assets/examples/jakarta.jpg', '/assets/examples/jules-verne.jpg',
  '/assets/examples/christmas-carol.jpg', '/assets/examples/leagues-frontispiece.jpg',
  '/assets/fonts/space-grotesk-latin.woff2', '/assets/fonts/space-grotesk-latin-ext.woff2',
  '/assets/fonts/inter-latin.woff2', '/assets/fonts/inter-latin-ext.woff2',
  '/assets/fonts/ibm-plex-mono-400-latin.woff2', '/assets/fonts/ibm-plex-mono-400-latin-ext.woff2',
  '/assets/fonts/ibm-plex-mono-500-latin.woff2', '/assets/fonts/ibm-plex-mono-500-latin-ext.woff2'];
self.addEventListener('install', event => event.waitUntil((async () => {
  const cache = await caches.open(SHELL);
  await cache.addAll(ASSETS);
  const response = await fetch('/config.js', { cache: 'no-store' });
  const source = await response.text();
  try {
    const config = JSON.parse(source.replace(/^window\.TYM_CONFIG\s*=\s*/, '').replace(/;\s*$/, ''));
    const url = new URL(config.apiBaseUrl);
    if (!response.ok || !['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.search || url.hash
      || !Array.isArray(config.examples) || config.examples.length) throw new Error('Private configuration');
    await cache.put('/config.js', new Response('window.TYM_CONFIG=' + JSON.stringify({ apiBaseUrl: config.apiBaseUrl, examples: [] }) + ';',
      { headers: { 'Content-Type': 'application/javascript' } }));
  } catch {
    await caches.delete(SHELL);
    throw new Error('A public configuration is required for the offline shell.');
  }
  await self.skipWaiting();
})()));
self.addEventListener('activate', event => event.waitUntil((async () => {
  for (const name of await caches.keys()) if (name.startsWith('tym-shell-') && name !== SHELL) await caches.delete(name);
  await self.clients.claim();
})()));
self.addEventListener('fetch', event => {
  const request = event.request, url = new URL(request.url);
  if (request.method !== 'GET' || url.origin !== self.location.origin) return;
  // API, account, source and local-example responses are intentionally never cached here.
  if (request.mode === 'navigate' && url.pathname === '/') {
    event.respondWith(fetch(request).catch(async () => (await caches.open(SHELL)).match('/index.html')));
  } else if (url.pathname === '/config.js') {
    event.respondWith(fetch(request).catch(async () => (await (await caches.open(SHELL)).match('/config.js')) || Response.error()));
  } else if (ASSETS.includes(url.pathname)) {
    event.respondWith((async () => (await (await caches.open(SHELL)).match(url.pathname)) || fetch(request))());
  }
});
