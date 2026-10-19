# Book Library and Window Cache Verification

Checked locally on 19 October 2026. These are software checks, not NLP precision/recall measurements.

- All 16 existing complete inputs passed original SHA-256/UTF-16 length checks before preparation. Originals were not modified.
- Cleaned copies contain 9,460,174 UTF-16 units across 468 contiguous chapters/reading sections. Comparing every original and cleaned file after removing whitespace found identical non-whitespace content. Section concatenation reconstructs every cleaned file exactly.
- 30 Node tests passed, including prose normalization/idempotence, complete chapter coverage, Unicode boundaries, density cache selection, grouping and window-resolution keys.
- .NET library checks passed: full source retained, exact UTF-16 section slices, forced book language, unknown ID rejection, path traversal rejection, hash/boundary validation and empty unconfigured library.
- Existing .NET checks passed: three POS fixtures, 19 anaphora cases, 202 density cases, 22 cached-preset comparisons and identical-view deduplication. Four Python worker tests passed.
- Playwright used authored library fixtures and the real local API. It verified book/chapter selection without implicit NLP requests, explicit entity lookup, generation by section ID without a source payload, eleven density presets, no additional requests for cached switches, no source editor/JSON/XML exports in book mode, and source GET routes returning 404.
- Normal and book generation shared the same five-per-IP daily limit. Book entity rejection reported the separate thirty-per-minute limit correctly.
- Images loaded successfully with attribution. Screenshots were checked at widths 320, 390, 768, 1440 and 1920. The narrow-layout density selector no longer stretches vertically.
- Window width sets the requested diagram width within 720-2400 pixels. Width-only/height-only changes clear the cache and diagram without requests. Returning to an old size does not recover its previous cache. A delayed old-resolution response was ignored. Generating again at 1920 returned a 1920-pixel SVG.
- One complete existing Verne chapter was also analyzed locally: 14,646 characters, 111 extracted events, 79 segments, 70 tracks at density 50, eleven cached presets, about 0.9 seconds on this machine. These counts/timing are not research accuracy scores or a cloud latency promise.
- OpenAPI YAML parsed successfully and all 52 local references resolved.
- Local Release build passed. NuGet vulnerability metadata remained unavailable (`NU1900`), so this does not certify dependency vulnerability status.

Azure verification also passed on 19 October 2026: both UI hosts served byte-identical JavaScript/styles/images and an empty private-example configuration; the API catalog reported all sixteen complete books and 468 sections. Public source-file GETs returned 404. The private registry had anonymous pulls disabled. Two live Verne chapter generations at windows 1440x1000 and 390x844 returned widths 1440 and 720 respectively, the same 111 events/79 segments, and eleven cached densities each. Cached switches and resizing made no generation requests; height-only resizing removed the previous diagram/cache. The two live checks used two daily generation permits.

Deployed API image: `tym-api-serban:release-20261019-library` (revision `r20261019library`). Deployed UI image: `tym-ui-serban:release-20261019-library2` (revision `r20261019library2`). Static hosting contains only application assets, fonts/license notices and four images, not books. The source books were uploaded to the private API image only after the user's explicit approval for Azure upload and public no-sign-in analysis.

Reproduce core checks:

```powershell
node --test tools/tests/*.test.cjs
dotnet run --project tools/Tym.GenerationChecks -c Release --no-restore
```

For browser checks, install Playwright with Chromium (or set `TYM_PLAYWRIGHT_MODULE` and `TYM_CHROMIUM`), start the UI locally, then run `node tools/tests/book-library.browser.mjs`. `TYM_UI_URL` can override the loopback UI address. Tests start an isolated API and generate authored fixture files under ignored `build_verify/`; they do not publish books.
