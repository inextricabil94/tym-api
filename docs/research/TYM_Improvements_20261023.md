# TYM Improvements

23 October 2026 | Engineering review and research roadmap

**Scope:** the current .NET API and React workbench. This is a code-based
improvement plan, not a new evaluation of the supplied papers or a gold-corpus
accuracy report. The 20 October research audit remains a dated baseline.

## Current Implementation

| Area | Implemented | Remaining limit |
| --- | --- | --- |
| Viewer controls | Saved diagrams and Cached TT density share aligned label, control and status rows. | Native menus shorten long labels in narrow columns. |
| Browser persistence | IndexedDB stores catalog metadata and successful generated responses; saved views match the API and exact window resolution. | No offline NLP or persistent raw source-text reader. |
| Offline shell | Service worker, self-hosted React, fonts, flags and images support offline reloads after installation. | Asset updates need a deliberate, state-preserving upgrade flow. |
| API database cache | Private SQLite responses persist across process restarts; density sessions are renewed. | Azure persistence has not been configured for this increment. |
| Boundary classification | Rules, ML.NET logistic regression and averaged perceptron are selectable. | Learned boundary models use authored seed patterns, not a validated literary corpus. |

## Verification Evidence

The selector change passed **20 side-by-side alignment cases** across English and
Romanian, guest/signed-in states, and 320/390/768/1440/1920-pixel widths. Six
additional cases enlarged selector text to 26 pixels and checked wrapping,
non-overlap and page overflow. Screenshots were reviewed at desktop/mobile sizes.

The preceding cache increment passed 51 Node tests, SQLite persistence/expiry and
eviction checks, an actual local API restart test with nine progressive density
views, and real IndexedDB/service-worker offline browser tests. Existing NER/POS
checks, 202 density cases and the 36-case segmentation presentation suite passed.
These are engineering regression results, **not NLP accuracy percentages**.

**Deployment distinction:** the 23 October release publishes the browser cache,
offline shell, aligned selectors and API cache capability. Production SQLite
remains disabled because no persistent volume is configured. No database,
storage volume or paid service was added. Release tag: `release-20261023-offline`.
Deployment checks are recorded separately in the repository verification notes.

<!-- pagebreak -->

## Prioritized Engineering Work

### P0 - Release and Data Safety

**Make the cache deployment explicit.** Select a private persistent location
before enabling production SQLite. Verify write permissions, storage overhead,
restart/replacement persistence and recovery from an unavailable database. Do not
reuse the account database or put cache files in images or static website storage.
Production database caching currently requires `TYM_DIAGRAM_CACHE_PATH`.

**Acceptance:** an unchanged request returns a cache hit after container
replacement; a changed model namespace misses; unavailable storage does not stop
analysis. Check the approved EUR 20 shutdown guard before release. Cost reporting
delays still prevent a guaranteed hard billing cap.

**Keep offline access honest.** Generated JSON/SVG can contain substantial parts
of an analyzed section. Raw source responses are not saved, but previously public
offline diagrams cannot be remotely revoked while a device is disconnected.
Restricted-book results must never enter persistent browser storage.

**Acceptance:** policy changes, sign-out, denied storage, eviction and cache
clearing have adversarial tests; browser caches contain no tokens, source-reader
responses or private configuration. Cache purge cannot be undone by stale writes.

### P1 - Cache Reliability and Performance

**Add safe application updates.** Offer an update action when a new service worker
is ready; preserve current work rather than unexpectedly reloading. Automate the
shell/data version bump and validate the precache manifest in CI.

**Strengthen cache identity.** Record a fingerprint of the extractor, model
artifacts, source revision, normalization and renderer. Retain explicit ownership
and access scope. Add cache schema migrations and corruption/recovery checks.

**Measure the benefit.** Collect hit/miss/skip counts, cold/warm latency, payload
bytes, eviction, browser/server memory and cancellation time. Benchmark short
texts, chapters and complete books separately. Coalesce identical in-flight
analysis and consider deduplicating shared event data across density records.

**Acceptance:** no cross-input, chapter, model, client or resolution reuse; bounded
payload/storage growth; identical semantic outputs on cache hit and miss.
Performance improvements must be demonstrated on matched inputs, not asserted.

### P2 - Long-Text and Interaction Work

Add bounded cancellable analysis jobs and stage progress for expensive inputs.
Virtualize large track views before transferring/rendering an entire SVG. Keep
full-text coverage and original UTF-16 offsets. Extend keyboard/screen-reader and
enlarged-text checks beyond the selectors to the full viewer and dialogs.

<!-- pagebreak -->

## Research Improvements

### Separate Semantics from Display

Boundary metadata is retained across density views, but that alone does not prove
that the full TT graph is density-independent. Infer one canonical event/segment
and temporal-relation graph; derive visual lane groups and display-only edges
from it. Preserve canonical IDs, source spans and temporal assertions when
density, viewport or interface language changes.

**Acceptance:** compare complete canonical graphs across all ten densities and
both interface languages. Layout coordinates and lane groupings may vary; claimed
temporal facts must not change solely because a slider moved.

### Build a Defensible Annotation Pilot

1. Select permitted English/Romanian passages, with separate dialogue, flashback,
   simultaneous, conditional, ambiguous-reference and OCR/diacritic cases.
2. Annotate event/TS boundaries, participants, temporal expressions and relations
   with written guidelines and adjudication. Keep uncertainty explicit.
3. Hold out complete works and translation families. Fit features and calibrate
   probabilities using training/validation data only.
4. Compare rules, both ML.NET models and simple sentence/paragraph baselines.
   Report boundary precision/recall/F1, Pk/WindowDiff, entity and temporal-relation
   metrics separately, with supports and document-level uncertainty estimates.

Do not present the existing authored-seed classifier scores, observed track
counts or test pass rates as literary accuracy. Perceptron margins are not
probabilities. Add abstention when temporal or coreference evidence is inadequate.
Evidence spans and user corrections should be auditable and versioned separately
from the source; corrections must not silently become training labels.

## Release Gate

- Re-run builds, SQLite/restart tests, offline reloads and bilingual UI/segmentation
  regressions against the same candidate.
- Test partial density warming, unavailable storage, concurrent writers, clearing
  and model/source invalidation.
- Preserve source ranges and protected-result export policy. Never publish private
  cache files or local source books.
- Validate service-worker/data versions and asset hashes; retain rollback on both
  UI hosts.
- Configure persistent storage only after separate approval. Record each release's
  image/revision IDs, verify live URLs and retain the EUR 20 shutdown guard.

**Repository evidence:** `Program.cs`, `DiagramDatabaseCache.cs`,
`ui/Tym.Ui/wwwroot/offline-cache.js`, `service-worker.js`, `app.js`, `styles.css`,
`tools/tests/i18n.browser.mjs`, and `docs/offline-caching.md`.
The historical research audit and `docs/segmentation-classifiers.md` provide
context; proposed items above are not claimed as implemented.
