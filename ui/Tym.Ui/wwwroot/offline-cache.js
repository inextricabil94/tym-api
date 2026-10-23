(function (root, factory) {
  const api = factory(root);
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.TymOffline = api;
})(typeof globalThis === 'object' ? globalThis : this, function (root) {
  'use strict';
  const epoch = 'tym-v0.10-temporal-order-1';
  const normalizeApi = value => value.trim().replace(/\/+$/, '');
  const copy = value => JSON.parse(JSON.stringify(value));
  function catalog(value) {
    if (!Array.isArray(value) || value.some(book => typeof book.id !== 'string' || typeof book.label !== 'string'
      || !['en', 'ro'].includes(book.language) || !Array.isArray(book.sections))) throw new Error('Invalid book catalog.');
    return value.map(book => {
      const result = {};
      for (const key of ['id', 'label', 'language', 'source', 'character_count', 'original_character_count', 'sha256_utf8'])
        if (book[key] !== undefined) result[key] = book[key];
      result.sections = book.sections.map(section => {
        if (typeof section.id !== 'string' || typeof section.label !== 'string' || typeof section.kind !== 'string'
          || !Number.isInteger(section.start) || !Number.isInteger(section.end) || section.start < 0 || section.end < section.start)
          throw new Error('Invalid book catalog.');
        return Object.fromEntries(['id', 'label', 'kind', 'start', 'end', 'character_count'].filter(key => section[key] !== undefined).map(key => [key, section[key]]));
      });
      return result;
    });
  }
  function savedResponse(response) {
    if (!response?.render?.svg || !Array.isArray(response.diagram?.segments) || !Array.isArray(response.diagram?.tracks)) return null;
    const result = copy(response);
    if (result.density_cache) {
      delete result.density_cache.session_id;
      result.density_cache.pending_densities = [];
    }
    return result;
  }
  async function identity(api, payload, resolution, book) {
    const options = payload.options || {};
    // Only the digest is stored, not the source text or original request body.
    const canonical = JSON.stringify([epoch, normalizeApi(api), resolution, book?.id || '', book?.sha256_utf8 || '',
      book?.character_count ?? null, book?.sections || [], payload.section_id || '', payload.text ?? null,
      options.layout, options.width, options.language, options.segmentation_algorithm || 'article_rules',
      [...(options.highlight_entities || [])].sort(), !!options.include_debug]);
    const bytes = await root.crypto.subtle.digest('SHA-256', new TextEncoder().encode(canonical));
    return 'diagram:' + [...new Uint8Array(bytes)].map(value => value.toString(16).padStart(2, '0')).join('');
  }
  function create({ database = 'tym-offline-v1', indexedDB = root.indexedDB, now = Date.now,
    maxBytes = 50 * 1024 * 1024, maxEntries = 28, ttl = 7 * 86400000 } = {}) {
    const memory = new Map();
    let opening, queue = Promise.resolve(), revision = 0, storageFailed = false;
    function stamp() {
      let shared = '';
      try { shared = root.localStorage?.getItem('tym-offline-clear') || ''; } catch {}
      return shared + ':' + revision;
    }
    async function db() {
      if (storageFailed) return null;
      if (!opening) opening = new Promise(resolve => {
        if (!indexedDB) { resolve(null); return; }
        let request, settled = false;
        const finish = value => { if (settled) { value?.close(); return; } settled = true; resolve(value); };
        try {
          request = indexedDB.open(database, 1);
          request.onupgradeneeded = () => request.result.createObjectStore('records', { keyPath: 'key' });
          request.onsuccess = () => {
            request.result.onversionchange = () => { request.result.close(); opening = null; };
            finish(request.result);
          };
          request.onerror = () => finish(null);
          request.onblocked = () => finish(null);
          setTimeout(() => finish(null), 2000);
        } catch { finish(null); }
      });
      return opening;
    }
    async function readAll() {
      const database = await db();
      if (!database) return [...memory.values()];
      return new Promise(resolve => {
        try {
          const tx = database.transaction('records');
          const request = tx.objectStore('records').getAll();
          request.onsuccess = () => resolve(request.result);
          request.onerror = () => resolve([...memory.values()]);
          tx.onabort = () => resolve([...memory.values()]);
        } catch { resolve([...memory.values()]); }
      });
    }
    async function change(action) {
      const database = await db();
      function remember(records) { memory.clear(); for (const record of records) memory.set(record.key, record); }
      if (!database) { remember(action([...memory.values()])); return false; }
      return new Promise(resolve => {
        try {
          const tx = database.transaction('records', 'readwrite'), store = tx.objectStore('records');
          const read = store.getAll();
          read.onsuccess = () => {
            // Read/prune/write share one transaction, including across workbench tabs.
            const records = action(read.result); remember(records);
            store.clear(); for (const record of records) store.put(record);
          };
          tx.oncomplete = () => resolve(true);
          tx.onerror = tx.onabort = () => { storageFailed = true; resolve(false); };
        } catch { storageFailed = true; remember(action([...memory.values()])); resolve(false); }
      });
    }
    function mutate(action) {
      const result = queue.then(action, action); queue = result.catch(() => {}); return result;
    }
    async function put(record, expectedRevision = stamp()) {
      return mutate(async () => {
        if (expectedRevision !== stamp()) return { saved: false, persistent: Boolean(await db()) };
        const value = copy({ ...record, epoch, created: now(), expires: now() + ttl });
        value.bytes = new TextEncoder().encode(JSON.stringify(value)).length;
        if (value.bytes > Math.min(maxBytes, 16 * 1024 * 1024)) return { saved: false, tooLarge: true };
        let saved = false;
        const persistent = await change(current => {
          if (expectedRevision !== stamp()) return current;
          const records = current.filter(item => item.epoch === epoch && item.expires > now() && item.key !== value.key);
          records.push(value); records.sort((a, b) => a.created - b.created);
          let total = records.reduce((sum, item) => sum + item.bytes, 0);
          while (records.length > maxEntries || total > maxBytes) total -= records.shift().bytes;
          saved = records.some(item => item.key === value.key); return records;
        });
        return { saved, persistent };
      });
    }
    async function get(key) {
      await queue;
      const found = (await readAll()).find(item => item.key === key && item.epoch === epoch && item.expires > now());
      return found ? copy(found) : null;
    }
    return {
      revision: stamp,
      async getCatalog(api) { return get('catalog:' + normalizeApi(api)); },
      async putCatalog(api, books, expectedRevision) {
        return put({ key: 'catalog:' + normalizeApi(api), kind: 'catalog', api: normalizeApi(api), books: catalog(books) }, expectedRevision);
      },
      getDiagram: get,
      async putDiagram(key, api, resolution, response, metadata, expectedRevision) {
        const safe = savedResponse(response);
        if (!safe) return { saved: false };
        return put({ key, kind: 'diagram', api: normalizeApi(api), resolution, response: safe,
          orderingKey: typeof metadata.orderingKey === 'string' ? metadata.orderingKey : null,
          label: String(metadata.label || ''), algorithm: response.segmentation?.algorithm || 'article_rules', book: Boolean(metadata.book) }, expectedRevision);
      },
      async diagrams(api, resolution) {
        await queue;
        return (await readAll()).filter(item => item.kind === 'diagram' && item.epoch === epoch && item.expires > now()
          && item.api === normalizeApi(api) && item.resolution === resolution).sort((a, b) => b.created - a.created)
          .map(({ key, label, algorithm, book, created }) => ({ key, label, algorithm, book, created }));
      },
      async clear() {
        revision++;
        try { root.localStorage?.setItem('tym-offline-clear', String(now()) + ':' + Math.random()); } catch {}
        return mutate(() => change(() => []));
      },
      async removeBooks(api) { return mutate(() => change(records => records.filter(item => !(item.kind === 'diagram' && item.book && item.api === normalizeApi(api))))); }
    };
  }
  return { epoch, normalizeApi, identity, catalog, savedResponse, create };
});
