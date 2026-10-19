# Book Library API

The optional `TYM_LIBRARY_PATH` contains `catalog.json` and ID-named UTF-8 TXT files. It is not a static-file directory. Missing configuration means an empty library; bad hashes, IDs or section boundaries fail integrity validation. Source reading is temporarily public by default at the user's request; `TYM_REQUIRE_LOGIN_FOR_TEXT=true` restores the confirmed-account restriction.

- `GET /v1/books`: `{ books: LibraryBook[] }`, with `Cache-Control: no-store`.
- `POST /v1/books/{id}/diagrams`: `BookAnalysisRequest` below; response is the existing `DiagramResponse`, including the requested density cache. Shares the normal five-per-IP daily diagram quota.
- `POST /v1/books/{id}/entities`: same selection; response is the existing entity/POS/reference result. Shares thirty lookups per minute per IP. No diagram usage.
- `GET /v1/books/{id}/source?sectionId=s1`: available to every user type by default; omit `sectionId` for the complete book. Returns `{ book_id, section_id?, language, sha256_utf8, start, end, text }` with exact cleaned UTF-16 ranges and `Cache-Control: no-store`. The hash identifies the complete cleaned source. With `TYM_REQUIRE_LOGIN_FOR_TEXT=true`, anonymous/invalid sessions return 401 and revoked sessions return 403. Unknown IDs return 404 after any required authorization. Separate 60/minute/IP reading limit. See [account setup](accounts.md).

```json
{
  "section_id": "s1",
  "options": {
    "layout": "both",
    "width": 1200,
    "track_density": 50,
    "cache_track_densities": true,
    "progressive_density_cache": true,
    "highlight_entities": []
  }
}
```

`section_id: null` selects the complete hosted book without a per-section character cap. IDs select precomputed sections only; callers cannot supply arbitrary file paths or source ranges. Unknown books/sections return 404, invalid options return 400, quota excess returns 429, and unavailable POS models return 503. Language always comes from the book. Book XML strings are empty to avoid duplicate source exports.

With progressive caching, the selected density is returned first. `density_cache.session_id` identifies an IP-bound, memory-bounded analysis session; `pending_densities` lists the other presets from 10 to 100. `GET /v1/density-sessions/{id}/{density}` returns `{ density, view: CachedDensityView }` using the existing NLP result, without another daily permit. Sessions expire after ten idle minutes or eviction. Density requests have a separate 60/minute/IP limit, and invalid/expired/foreign sessions return 404. The browser loads layouts sequentially, keeps its selected view, and aborts warming when the input/resolution changes. Partial cache failures do not remove the current diagram.

Each metadata entry contains `id`, `label`, `language`, `source`, `character_count`, `original_character_count`, `sha256_utf8` and `sections`. Each section contains `id`, `label`, `kind`, `start`, `end`, `character_count`; offsets are half-open UTF-16 offsets in the cleaned book. Returned event spans are relative to the analyzed selection. Catalog labels/attribution are metadata, not source previews.

Public analysis is not DRM: results may contain source excerpts, and clients can inspect network responses even when the workbench omits text/export controls. Source files stay out of Git, static website assets and normal builds; explicit library deployments require the operator's redistribution/analysis rights.
