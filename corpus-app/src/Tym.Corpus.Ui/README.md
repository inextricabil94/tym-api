# TYM Corpus UI

An independent .NET 10 static app. Browser source is strict TypeScript in `client/`; `npm run build` emits one dependency-free `wwwroot/app.js`. The served page uses system fonts and no CDN. `wwwroot/app.js` is generated; edit the TypeScript sources.

## Build and run

Use Node 24 and the .NET 10 SDK:

```powershell
npm ci --ignore-scripts
npm run typecheck
dotnet build -c Release
$env:TYM_API_BASE_URL='http://127.0.0.1:8871'
dotnet run -c Release --no-build --urls http://127.0.0.1:8791
```

The normal .NET build compiles TypeScript. Docker compiles it in a pinned-dependency Node stage and publishes the static app in the .NET 10 stage. `/health` is the UI liveness check. `/config.js` reads `TYM_API_BASE_URL`; it contains no credentials. `/results/` downloads stay on the UI origin. The API must permit the configured UI origin through CORS.

## Classifiers and document review

The catalog drives model availability, labels and training metadata. Ten lexical classifier flows retain two original editable examples each. Six structured TimeBank variants reuse the corresponding task examples and are shown as separate catalog choices. Displayed predictions and paired diagnostics do not establish independent gold accuracy.

Document analysis proposes draft source spans using rules and the narrative model. The workbench preserves unchanged text and half-open UTF-16 offsets. Select a span to add a segment or mention, correct labels/normalized values, inspect per-field provenance, and explicitly mark individual annotations reviewed. Corrections return the affected annotation to draft. A reviewed annotation records a reviewer decision, not adjudication.

Relations have explicit family, From and To IDs. TYM endpoints are segments; TLINK uses event/time mentions; SLINK/ALINK use event mentions. Relations start in draft. Validation uses reviewed temporal relations to display a partial order, simultaneous groups and cycle diagnostics; unlinked annotations retain unknown order. Graph layout positions do not imply dates or a total timeline. Export is enabled only for a validated unchanged draft; edits invalidate that result. Exported JSON includes source and provenance. Spatial candidates are mentions, without geocoding or inferred map coordinates.

## Algorithm comparison

The independent comparison panel loads only the same-origin aggregate `wwwroot/results/algorithm-comparison.json`. It filters conditional classification by task and both classification/book exploration by language, reports grouped accuracy/macro F1 and local batched timing, and keeps book semantic accuracy N/A. It requires a completed seventeen-family report with finite values; missing, malformed, incomplete or timed-out data displays an inline reload state without blocking prediction or document review. JSON/CSV/Markdown downloads use fixed local paths. Small C# neural models are scratch experiments, and the panel does not imply independent gold accuracy or a deployed replacement.

Each row can include optional `space` measurements. Accessible “Space and storage” details show feature/vocabulary ranges, sequence limits, neural parameter counts, estimated float32 weight bytes, shared-process working-set samples/lifetime peaks in MiB, and actual exported artifact bytes. The report retains `_mb` property names for those MiB memory values. Process memory includes shared runtime and earlier trials; it does not measure isolated model allocation. Estimated parameter bytes are not exported file sizes. Missing measurements display as unavailable, preserving older summaries without `space`. Supplied numeric space values must be finite and nonnegative; inconsistent minimum/maximum ranges are rejected. Scopes render as inert text.

## UI tests

```powershell
# Uses installed Chrome; no browser download is required here.
npm run test:ui

# Optional actual private Books.zip passages, sampled at test time using .NET.
$env:TYM_PRIVATE_BOOKS_ZIP='C:\Users\serba\Desktop\Books.zip'
$env:TYM_UI_TEST_API_URL='http://127.0.0.1:8871'
npm run test:books

# Or use the private sample JSON already produced by Tym.Corpus.Data:
$env:TYM_PRIVATE_BOOK_SAMPLES='C:\Users\serba\Documents\ChatGPT\PhD\tym-private-research\book-ui-samples.json'
```

Build the API/models and `tools/Tym.Corpus.Data` first. `TYM_CORPUS_DATA_DLL` can override the sample CLI path. The CLI command is `dotnet <dll> book-sample --zip <zip> --out <private-json> --count 5 --max-chars 1200`. Without either private input environment variable, private tests are explicitly skipped. The supplied Books.zip samples are Romanian; the English flow uses original prose authored for this app.

Synthetic tests cover all 16 catalog choices, both examples, edit retention, blank/error/retry states, mobile overflow, fixed report links, review provenance, partial graph, cycles and JSON export. Five optional private passage tests exercise real narrative scoring, source preservation, annotation editing, validation and export. Their expected labels are not asserted as gold. All private network requests are restricted to localhost. Traces, screenshots and video are disabled to keep private excerpts out of test artifacts; exports are read in memory. Do not commit QA captures, model weights, raw books, private samples or private test output.

TypeScript reference: https://www.typescriptlang.org/tsconfig/. Test API: https://playwright.dev/docs/api/class-test. Recording options: https://playwright.dev/docs/test-use-options#recording-options.
