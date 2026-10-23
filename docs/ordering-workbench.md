# Ordering Workbench

## Inspection and catalog decision (23 October 2026)

The application is ASP.NET Core with plain React `createElement` modules, no
Node package/bundler or TypeScript configuration. Node's test runner covers pure
UMD modules; Playwright scripts cover the UI; `Tym.GenerationChecks` covers the
.NET extraction/temporal models. There is no configured frontend lint command.

Inspected current sources, documentation, configuration, all local/remote Git
refs and history searches for the ten sorting names. No definitive ten-sort
catalog was found. Historical commits `55ce998` and `ee5a491` contain a separate
corpus ML classification/benchmark catalog, not a sorting catalog. **The task's
fallback is used**, not claimed as a previously verified TYM selection:
Bubble, Selection, Insertion, Merge, Quick, Heap, Shell, Counting, Radix, Bucket.

`TimeSegment` and `TimeTrack` are in `Program.cs`. Stable TS identity, `event_ids`,
UTF-16 source offsets and `track_id` already exist. `text_order` is narrative
order. `story_order` and `temporal_ordering.placements` are density-independent
partial-order display positions, with text order breaking ties, not dates.
Accepted event constraints live in `diagram.temporal_ordering.constraints`, with
`source`, `cue`, `evidence`; rejected claims live separately in `conflicts`.
`diagram.relations` projects accepted event evidence onto segments, explicitly
without asserting whole-segment interval semantics. TimeML event-instance links
map through `make_instances.tym_event_id`. Anchor TLINKs do not order segments.

Numeric sorting keys must be explicitly selected/prepared. Neither narrative
order, these keys, nor researcher display preferences establish chronology.

## Phase gates

Implementation order: pure temporal validator and tests; pure ordering state
and persistence tests; Arrange with application data; ten sorting engines and
instrumentation tests; trace replay; Compare; full integration/regression checks.
No animation is implemented before the validator/state gates pass.

Gates completed: 8 validator tests, then 14 validator/state tests pass. Arrange
passes Playwright with the existing API-generated temporal-development fixture:
rejected constraint violation, precise numeric movement, drag, arrows, undo/redo,
save/restore, stable TS/TT identity and a 390px responsive layout. Source order in
that flashback fixture is intentionally incompatible with accepted evidence;
reset is rejected with diagnostics instead of committing an invalid layout.

## Implemented phases

| Phase | Result |
| --- | --- |
| 0 | Repository/domain inspection and explicit fallback catalog decision above. Existing uncommitted backend temporal work was preserved. |
| 1 | Pure compiled constraint model and whole-permutation validator, structured evidence diagnostics, iterative cycle detection and transitive witnesses. |
| 2 | Framework-independent immutable Original/User Adjusted/Algorithm Suggested state, validated movement/application/history, local versioned persistence and stale-data detection. |
| 3 | Ordering → Arrange, TT rows, numbered horizontal global positions, drag preview, arrow/Alt+Arrow/numeric moves, source-order disclosure, inspector, Undo/Redo/Reset/Save/Restore. Verified with real API-generated data before replay development. |
| 4 | Ten independent sorting engines, prepared numeric keys, deterministic ties, compatibility guards, correctness verification and operation metrics/traces. All ten engines tested before replay UI. |
| 5 | Cancellable worker computation; deterministic Play/Pause/Previous/Next/Restart/speed/scrubbing; operation history; algorithm structures; checkpoint reconstruction; explicit validated Apply. |
| 6 | Original/Suggested/User comparison, synchronized selection/page/optional replay step, moved-position labels, separate correctness/temporal results and accumulated per-algorithm statistics. |
| 7 | 24-position pages retain complete logical order; bounded replay/auxiliary previews; responsive inspector; accessible labels/focus/keyboard controls; reduced motion; EN/RO; offline shell and worker support. |
| 8 | Unit, browser, actual local API, existing temporal/segmentation/NER/density/cache regressions and Release build verification. Details below. |

## Temporal validation safeguards

`ordering-core.js` clones/freezes a read-only domain snapshot. Accepted event
constraints and event-instance TLINKs map through existing `event_ids`; TS
relations remain compatible with the existing event-to-segment projection
semantics. AFTER/IAFTER directions normalize exactly once across both graph
layers. Accepted precedence with missing/ambiguous TS ownership is diagnosed.

Every committed move, reset, undo/redo, restored layout and applied suggestion
passes `validate(model, completePermutation)`. Checking all direct strict edges
also enforces every transitive path; `model.path` supplies provenance witnesses.
Iterative strongly connected components detect cycles without recursion. Strict
precedence conflicting with accepted equality/overlap blocks movement. Withheld
source conflicts remain separate from accepted claims and are surfaced as such.

UNKNOWN/VAGUE, unsupported relations and absent evidence never acquire strict
edges. Containment/begin/end relations remain evidence and do not define the
left-to-right order of display blocks. Simultaneous events do not equate entire
multi-event TS intervals; explicit TS equality propagates precedence while
allowing separate layout positions. Event-time anchors are retained by the
domain model and never treated as event-event precedence.

Incomplete/duplicate/sparse arrangements are rejected. Diagnostics identify TS
IDs, original relationship types, source event IDs and available source/cue/
evidence/link IDs. Missing inspector evidence is marked unavailable. No movement
changes source offsets/text, accepted annotations, TimeML, TT membership, or
source `text_order`; `story_order` remains the API's partial-order display key.

## State and persistence

The workflow-guide follow-up adds the navy/cyan three-pane Ordering view, colored
A–D display bands, text-excerpt cards, sticky TT labels, inspector sections,
conflict/evidence feedback, a legend and an inline workflow guide. Other top-level
views keep their existing theme. Light mode and the stacked mobile inspector are
supported.

Display groups are **fixed position ranges**, not temporal annotations or new TS
objects. Four balanced ranges are created for documents with at least four TS;
smaller documents have one range. Ten positions produce A=1–3, B=4–5, C=6–8,
D=9–10, matching the reference. Choosing a group only prepares the destination;
drag, numeric confirmation or keyboard movement commits it after group-range and
whole-permutation temporal validation. Insertion shifts other items, whose
display groups follow their resulting position. TT membership never changes.
Reset and explicit algorithm application operate on the full validated layout;
display-group membership then follows the resulting positions.

Plain/Alt arrows move within the chosen group; Ctrl/Command+Arrow moves to its
start/end. Ctrl/Command+S saves, Z undoes, Y/Shift+Z redoes. Text-editing undo is
preserved. The inspector offers Overview, Events, Relationships and Evidence.

The pure transition API owns three separate arrangements. Original is immutable
source order. Suggested stores an algorithm result and its independent temporal
validation; generating a trace never commits it. User Adjusted stores the
researcher's display preference. Selection uses stable TS identity, not an array
index. History contains up to 100 validated changes; reset and restore are
undoable. Switching top-level views retains unsaved ordering; hiding Ordering
pauses replay.

Initial User Adjusted uses an accepted feasible partial-order layout. Original
can violate accepted flashback evidence; in that case Reset reports the conflict
without committing it. Inconsistent accepted graphs present an explicit error.

Schema v2 saves group ranges, TS IDs, selected identity, document identity and change
fingerprints in per-origin localStorage. A stable document key distinguishes API
and book/section, or a digest of custom input. Offline diagram metadata preserves
that opaque key across reopening and viewport changes. Source/TS identity,
membership and constraint fingerprints independently detect stale saves; unknown
versions, corrupt JSON, invalid group ranges and invalid restored permutations are rejected. V1 records migrate to default groups; legacy
fingerprint-keyed records remain readable. No source text or annotations are
stored in arrangement records. These digests detect accidental changes; they
are not a cryptographic authenticity mechanism.

## Sorting, instrumentation and replay

Explicit keys come from selected `text_order`, selected partial-order
`story_order`, or researcher-prepared TS→number JSON. Each trace captures User
Adjusted and its keys at generation. All engines use ascending numeric keys;
duplicates retain captured input order through deterministic tie-breaking.
None extracts temporal events, infers dates or writes annotations.

Comparison engines accept finite numbers. Counting/Radix require safe integers;
Counting rejects oversized/sparse ranges (at most 100,001 count slots), Radix uses
a BigInt offset for negative keys and safe-integer extremes, and Bucket handles
finite extremes and subnormal values. Correctness checks identity, key order and
duplicate stability independently from temporal compatibility.

Compact operations contain IDs, changed main-array slots and applicable
`compare`, `swap`, `write`, `move`, `pivot`, `partition`, `merge`, `count`, `bucket`,
`complete` events. Metrics expose comparisons, swaps, writes (including auxiliary
writes), item movements, peak auxiliary logical slots and total operations.
Computation time measures a separate untraced instrumented execution. Trace
overhead is the nonnegative traced-minus-untraced estimate, subject to timing
noise; checkpoint preparation time is reported separately. Playback duration is
never included. Trace/checkpoint storage is excluded from auxiliary-algorithm
slots and is bounded separately.

A worker protects UI responsiveness; terminating it cancels pending computation,
and request IDs reject late results. A pure replay controller invalidates stale
ticks on pause/cancel/seek/replacement. Checkpoints bound reconstruction work and
storage; counts/bucket/merge previews show up to 40 entries. Intermediate buffered
writes can repeat a TS in the working array; they are never committed layouts.
Pivot/partition, heap boundary, gap, merge buffer, counts, buckets and completed
regions are shown where applicable. Final output requires explicit **Apply
Suggested Order**, guarded by temporal validation and sorting correctness.

## Files

Added application modules under `ui/Tym.Ui/wwwroot/`:

- `ordering-core.js`: domain adapter, validation, state/history/persistence.
- `ordering-sort.js`: catalog, ten engines, instrumentation and reconstruction.
- `ordering-replay.js`: pure playback state transitions.
- `ordering-worker.js`: cancellable trace generation.
- `ordering-view.js`: Arrange/Replay/Compare React integration and inspector.

Updated `app.js`, `index.html`, `styles.css`, `i18n.js`, `offline-cache.js`,
`service-worker.js` and `README.md`. Arrangement identity is opaque offline
metadata; annotation payloads are unchanged.

Added `tools/tests/ordering-{validator,state,sort,replay}.test.cjs`,
`ordering.browser.mjs`, `ordering-scale.browser.mjs`, `ordering-live.browser.mjs`.
Extended offline-cache and i18n tests and offline browser coverage. Updated two
older presentation-only browser fixtures (i18n and SVG panels) to include the
already-required temporal-development/ordering contract. No source semantics
were relaxed to pass those tests.

Pre-existing modifications to `Program.cs`, `TemporalOrdering.cs`, OpenAPI,
segmentation modules and related verification files were preserved, not replaced
or attributed to this workbench implementation.

## Verification on 23 October 2026

- **106 Node tests pass**, including 47 focused Ordering tests. Validator/state
  gates passed before replay; later reviews added projection, malformed-input,
  persistence, instrumentation and memory-bound regressions.
- **Ordering browser suite passes** on API-generated data: valid/rejected numeric
  moves, drag, arrows, undo/redo/reset rejection, save/restore, all ten worker
  engines, Apply guards, play/pause/seek/steps/restart/cancel, synchronized Compare,
  stable IDs/TT, reduced motion, 390px mobile and EN/RO.
- **Authored scale browser suite passes**: 1,003 TS across 42 pages, maximum 24
  rendered TS, complete logical persistence, invalid drag preview, keyboard focus
  from position 24→25, numeric last-page movement, hidden playback pause, retained
  unsaved layout and cancellation of a pending worker.
- **Fresh loopback API integration passes**: actual generated temporal data,
  manual move, worker trace, validated Apply and three-way comparison, zero page
  errors. Bilingual temporal HTTP checks pass with all twenty density views.
- **Existing regressions pass**: 36 segmentation browser cases; offline browser
  suite including worker replay without network; i18n (10 responsive cases,
  20 header/cache alignment cases); SVG panels (20 locale/theme/responsive cases).
- **Full .NET checker passes**: library integrity, trained EN/RO NER, three POS
  fixtures, nineteen reference fixtures, 93 segmentation cases, 78 bilingual
  temporal-order cases with density/resolution invariance, and 202 density cases.
  Separate SQLite restart/TTL/bounds/identity/storage checks pass.
- **Solution, API and UI Release builds pass with zero warnings/errors**
  (`dotnet build Tym.Api.sln -c Release -m:1 -nodeReuse:false`).
  JavaScript syntax checks and `git diff --check` pass. There is no configured
  frontend lint or TypeScript/type-check task in this plain-JavaScript repository;
  no nonexistent checks are claimed.

Reproduce with installed .NET 9+ and Node 24; browser scripts accept
`TYM_PLAYWRIGHT_MODULE`, `TYM_CHROMIUM`, `TYM_UI_URL` and, for live Ordering,
`TYM_API_URL`. Existing generation checks produce the real API fixture files.

```powershell
node --test tools/tests/*.test.cjs tools/tests/*.test.mjs
dotnet build Tym.Api.csproj -c Release -o build_verify/ordering-final-api
dotnet build ui/Tym.Ui/Tym.Ui.csproj -c Release -o build_verify/ordering-final-ui
dotnet build tools/Tym.GenerationChecks/Tym.GenerationChecks.csproj -c Release -o build_verify/ordering-checks
dotnet build_verify/ordering-checks/Tym.GenerationChecks.dll
dotnet build_verify/ordering-checks/Tym.GenerationChecks.dll --cache-only
# UI on 8793; local API on 8798:
node tools/tests/ordering.browser.mjs
node tools/tests/ordering-scale.browser.mjs
node tools/tests/ordering-live.browser.mjs
./tools/tests/temporal-ordering.http.ps1 -Api http://127.0.0.1:8798
```

## Limits and follow-ups

- Replay accepts up to 20,000 TS and 200,000 recorded operations by default.
  Costly quadratic traces stop with a clear capacity error; complete manual
  layout ordering and saved arrangements remain intact. Playback displays a
  paged, bounded window of the complete trace/order.
- Constraint reasoning follows existing TYM precedence/equality/overlap
  semantics, not full Allen interval algebra. Large explicit simultaneous groups
  can expand precedence edges quadratically during compilation; sparse graphs
  are validated linearly, including the tested 20,000-node chain. Compact group
  constraints are a recommended follow-up if bulk simultaneous evidence is added
  to the API (the current extractor does not emit it).
- Saves are local to this browser origin; no server sync or deployment was
  requested. New source content is a distinct custom document; changes to
  segmentation/constraints for the same source invalidate its saved layout.
- Browser checks used installed Chromium on Windows, not a physical touchscreen
  or a screen-reader product. Keyboard, names, focus, responsive layout and
  reduced-motion behavior are covered by automated engineering checks.

This work does not claim improved NLP accuracy, new historical chronology, or
production deployment. Original narrative order, accepted evidence and display
preferences remain separate throughout.

Follow-up verification: **109 Node tests pass** (including group range,
assignment/history/annotation preservation, v2 persistence and v1 migration).
The new `ordering-groups.browser.mjs` passes range/temporal rejection, preview vs
commit, group-aware native drag, keyboard group boundaries/history/save,
responsive navy rendering and saved-layout reopening. Existing Ordering and
1,003-segment browser suites also pass after adopting explicit destination-group
selection for cross-group moves. The solution Release build passes with zero
warnings/errors. Desktop/mobile screenshots are in `build_verify/ordering/`.
