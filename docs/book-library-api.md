# Book Library API

The optional `TYM_LIBRARY_PATH` contains `catalog.json` and ID-named UTF-8 TXT files. It is not a static-file directory. Missing configuration means an empty library; bad hashes, IDs or section boundaries fail integrity validation. There is no raw book-content route.

- `GET /v1/books`: `{ books: LibraryBook[], analysis_character_limit: 100000 }`, with `Cache-Control: no-store`.
- `POST /v1/books/{id}/diagrams`: `BookAnalysisRequest` below; response is the existing `DiagramResponse`, including the requested density cache. Shares the normal five-per-IP daily diagram quota.
- `POST /v1/books/{id}/entities`: same selection; response is the existing entity/POS/reference result. Shares thirty lookups per minute per IP. No diagram usage.

```json
{
  "section_id": "s1",
  "options": {
    "layout": "both",
    "width": 1200,
    "track_density": 50,
    "cache_track_densities": true,
    "highlight_entities": []
  }
}
```

`section_id: null` selects the complete book if it fits the 100,000 UTF-16 unit analysis limit. IDs select precomputed sections only; callers cannot supply arbitrary file paths or source ranges. Unknown books/sections return 404, excessive selections and invalid options return 400, quota excess returns 429, and unavailable POS models return 503. Language always comes from the book, not the request.

Each metadata entry contains `id`, `label`, `language`, `source`, `character_count`, `original_character_count`, `sha256_utf8` and `sections`. Each section contains `id`, `label`, `kind`, `start`, `end`, `character_count`; offsets are half-open UTF-16 offsets in the cleaned book. Returned event spans are relative to the analyzed selection. Catalog labels/attribution are metadata, not source previews.

Public analysis is not DRM: results may contain source excerpts, and clients can inspect network responses even when the workbench omits text/export controls. Source files stay out of Git, static website assets and normal builds; explicit library deployments require the operator's redistribution/analysis rights.
