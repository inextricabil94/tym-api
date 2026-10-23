# Offline and Database Caching

Published on 23 October 2026 as `release-20261023-offline`. Browser persistence,
offline reloads and aligned viewer selectors are enabled on both public UI hosts.
The API cache implementation is included, but production SQLite remains disabled:
the existing Container App has no persistent volume. No paid storage was added.

## Browser

The workbench saves public library/chapter **metadata** and successful generated
diagram responses in IndexedDB (`tym-offline-v1`). The viewer's **Saved diagrams**
menu opens them without another generation request. Only records for the current
API and exact window width/height are listed. All available TT density views are
saved together; incomplete density bundles advertise only completed views.

- Cache identity hashes the exact input/book hash, chapter, language, algorithm,
  highlight, layout and window resolution. Original request text is not stored.
- Catalog metadata is reused for five minutes, then revalidated. Refresh always
  requests the catalog again and retains a working cached list on failure.
- Records expire after seven days. Capacity is 28 records / 50 MiB of serialized
  payload; each record is at most 16 MiB. Oldest saved records are evicted first.
- Repeat generation prefers a matching live bundle over an older disk snapshot
  and flushes its completed views before reuse. It keeps background warming
  running and selects the requested density, not the stored bundle's base density.
- **Clear offline cache** removes the browser catalog/diagram records. It does not
  delete the current input, server database, or public application shell.
- The refresh icon beside Generate bypasses the browser diagram cache. The API
  may still reuse its database result. Normal cached browser hits make no POST
  and consume no daily generation allowance. Actual API requests, including
  database hits, still pass the existing rate limiter.
- Blocked/unavailable/quota-exhausted IndexedDB falls back to bounded memory and
  reports session-only storage. Oversized responses remain viewable but are not
  saved. Clearing also invalidates older in-flight writes.

A service worker caches an explicit public application shell, pinned self-hosted
React 18.3.1, fonts, flags and example images. After an initial successful online
visit/installation, the workbench can reload offline and open saved diagrams.
Only a validated empty-private-catalog configuration is saved. No `/v1/`, account,
source, local-example or arbitrary external response is put in the shell cache.
Service workers require HTTPS or loopback HTTP. Bump the shell version when
changing its public assets, and the offline data epoch when model/schema
compatibility changes. No offline NLP computation is performed.

## Source and Access

Raw `/source` responses and full input drafts are not persisted. Generated diagram
JSON/SVG can contain narrative excerpts, potentially most of an analyzed section,
and is saved locally by design. Book
results retain their no-JSON/XML-export UI policy when opened from history.
The marked-source view is unavailable for a historical diagram without its exact
current source; no source is reconstructed or fetched to bypass access checks.

Book results are persisted only when the current API explicitly reports public
text access. Restricted-book results are not saved, and existing saved book
records are removed when a known restricted policy is loaded. Previously public
offline copies cannot be remotely revoked while a device is offline. Browser
storage is not an encrypted private vault; clear it on shared devices.

## API Database

The API uses the already-referenced Microsoft.Data.Sqlite package. Development
defaults to ignored `private-models/diagram-cache/diagrams.db`. Production has no
database cache unless `TYM_DIAGRAM_CACHE_PATH` explicitly names a private writable
file outside `wwwroot`. No Azure database/resource was provisioned.

```powershell
$env:TYM_DIAGRAM_CACHE_PATH = 'D:/persistent-tym-cache/diagrams.db'
$env:TYM_DIAGRAM_CACHE_VERSION = '1'
```

For Azure Container Apps, mount persistent storage at the chosen path before
enabling it. An ordinary container filesystem does not preserve this cache across
replacement. Do not reuse the account database, upload SQLite files into images,
or put cache files in static website storage.

- Keys hash exact source, client identity, all options, XML-omission policy and
  the API model/operator namespace. No input request or raw client IP column is
  stored. Results can contain excerpts, so secure the private file/volume.
- Fixed expiry: 24 hours. Limits: 100 entries / 128 MiB serialized payload, with
  a 16 MiB entry ceiling. Least-recently-accessed entries are evicted. SQLite
  pages/journals add overhead; these are payload budgets, not strict disk quotas.
- `X-TYM-Cache` reports `HIT`, `MISS` or `DISABLED`, exposed through CORS.
- Progressive requests receive fresh IP-bound density sessions. The persistent
  cache stores each density response without session IDs. Missing views lazily
  reuse one prepared analysis; stored views can be read after an API restart.
- SQLite failure is logged by error type and generation continues uncached.
  Bump `TYM_DIAGRAM_CACHE_VERSION` when replacing external models/configuration
  without changing the API model version. No public cache lookup/delete route
  was introduced.

## Checks

Focused SQLite checks cover reopen persistence, TTL, eviction, namespace/input
isolation, session removal and unavailable storage. The isolated HTTP suite
verifies database hits, nine progressive views after an API restart, fresh session
IDs and the exposed cache header. Browser checks exercise real IndexedDB and
service-worker offline reloads, no repeat generation, density selection, exact
resolution filtering, clearing, source exclusion and restricted-book handling.
Existing bilingual/header and 36-case segmentation browser suites passed. The
existing NER/POS/reference tests and 202 density cases passed unchanged.
Both Release builds passed without warnings/errors; 51 Node unit tests passed.
The preview uses the existing sixteen-book local library without copying it into
Git or static assets.
