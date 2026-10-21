# Selectable Temporal Segmentation

## Scope

Version 0.8 adds selectable **event-to-TS boundary classification**. This is
different from event detection, the NAR/REM/SUP/GEN/FIC label assigned to a TS, or
the TT density used to lay out tracks. Those existing stages retain their prior
behavior. No LLM, cloud inference, subscription, model download, or additional
NuGet package is required.

The workbench's **Segmentation algorithm** menu and
`options.segmentation_algorithm` offer:

| Value | Implementation | Score |
| --- | --- | --- |
| `article_rules` | Existing article-guided actor/category/anchor/cue compatibility rules; default | None |
| `mlnet_logistic` | ML.NET SDCA binary logistic regression, 100 iterations, L1=0, L2=0.0001, one training thread, seed 42 | Model probability, not empirically corpus-calibrated |
| `mlnet_perceptron` | ML.NET averaged perceptron, 100 iterations, seed 42 | Signed decision margin, not a probability |

Both ML algorithms are real trained binary models. They learn from **48 authored
feature-pattern rows** in [Segmentation.cs](../Segmentation.cs), including
repeated continuation examples for weighting. These are **not annotated literary
passages, a pretrained language model, or an evaluation corpus**. The models are
small experimental baselines, not research-validated segmentation systems. No
accuracy, F1, calibration, or generalization claim follows from the checks below.

Eight adjacent-event features are used: actor change, temporal-category change,
explicit before/after/simultaneous cue, conflicting nonempty time anchors,
changed known location, paragraph break, missing actor evidence, and same-sentence
continuity. English and Romanian reuse these structural features; upstream
language-specific extraction can still miss or misclassify the evidence.

Models initialize lazily once per process and protect the reused ML.NET
prediction engine with a lock. Choosing ML does not silently fall back to rules
if model training or prediction fails. The document-start boundary is structural,
not a model prediction, and has no score. Every extracted event belongs to exactly
one contiguous TS; its original UTF-16 offsets and order remain unchanged.

## API

```json
{
  "text": "Adam waited in Jakarta. Adam searched in Jakarta. Later, Adam traveled to London.",
  "options": {
    "language": "en",
    "segmentation_algorithm": "mlnet_logistic",
    "track_density": 50
  }
}
```

Use the same option in hosted-book diagram requests. The book catalog still
determines text language, and offsets remain relative to the selected chapter or
complete analyzed input. Unknown algorithms return HTTP 400 rather than being
ignored. Omitted options retain `article_rules` for backward compatibility.

Each diagram response adds `segmentation` with algorithm, model version, score
kind, seed provenance/count, and one decision per extracted event. Decisions
contain the preceding/current event IDs, assigned TS ID, boundary flag, source
start offset, optional score, and observed feature names. Feature observations
are not causal feature-attribution explanations. Existing `segment.confidence`
and `segment.classifier` still describe the **TS type**, not boundary prediction.

Boundary decisions are calculated once before density layout. All ten cached TT
density views retain the same segmentation metadata, event coverage and TS spans;
only track assignments and related presentation change. Algorithm changes in the
UI invalidate the current result and density cache, cancel background cache work,
and reject stale generation responses. A legacy API cannot silently provide a
rule result when the user selected ML.

## Marked-Text View

The **Segmentation** result tab and **Inspect segments** button open the source
view. TS ID buttons select a segment; colored source spans and the type legend
mark its boundaries. The inspector reports its type, TT, event IDs, exact
half-open UTF-16 range, boundary score and observed features. The TT arrow opens
the corresponding track group in the SVG view.

The view slices original source by offsets, never by `IndexOf` searches or the
normalized segment text. Whitespace, unclassified gaps, emoji and Romanian
diacritics are preserved. Twenty TS spans are shown per page; all pages together
retain the full source. Invalid or overlapping spans are rejected visibly instead
of applying misleading marks. Hosted-book source is reused from the existing
reader, with no extra source request and no bypass of login restrictions. No
source copy/download command or new public storage endpoint was added.

Interface labels are available in English and Romanian. Probabilities and
perceptron margins have distinct labels; margins are not displayed as confidence
percentages. The small-model provenance warning is shown next to the algorithm.

## Checks: 21 October 2026

- Release API/checker and UI builds passed with zero warnings/errors using
  isolated build outputs; the previous preview's locked DLL was not overwritten.
- Six bilingual algorithm cases verify exact event coverage, nonoverlapping
  UTF-16 spans, boundary/TS correspondence, score semantics, density invariance,
  cache metadata, repeated/concurrent prediction, the known continuity seed, and
  invalid/default options.
- On the authored five-event fixtures in each language, rules produced three
  segments, logistic regression four and perceptron four. These are reproducible
  **outputs, not quality scores**. They demonstrate different executed algorithms,
  not that one segmentation is correct or superior.
- The existing generation suite passed trained EN/RO NER, three POS fixtures,
  nineteen reference fixtures and 202 density cases.
- Forty-five Node tests passed, including five new source-marking/cache checks.
- Thirty-six new browser cases covered algorithms, languages, 320/390/1440-pixel
  widths and both themes, with no browser errors. Additional checks covered
  paging, chapter-local offsets, unchanged source, cache invalidation, legacy API
  rejection, and restricted-source handling. These use real-model responses from
  the authored fixtures; the separate 45-segment paging fixture is UI-only.
- The existing viewer suite passed twenty responsive/language/theme cases; the
  existing bilingual suite passed ten responsive cases.
- Local HTTP checks executed all three algorithms and rejected an unknown one.
  These local checks used no live Azure diagram allowance or paid model endpoint.
- A real local Romanian UI/API test generated four segments with the perceptron,
  preserved exact marked source, loaded all ten progressive density caches from
  one generation, and produced zero browser errors. Screenshots at desktop/mobile
  widths were reviewed. The local API was refreshed after testing so its daily
  allowance is available for preview use.

To reproduce the focused backend and browser fixtures:

```powershell
dotnet build tools/Tym.GenerationChecks/Tym.GenerationChecks.csproj -c Release -o build_verify/segmentation-checks
dotnet build_verify/segmentation-checks/Tym.GenerationChecks.dll --segmentation-only --write-segmentation-fixtures
node --test tools/tests/segmentation.test.cjs
node tools/tests/segmentation.browser.mjs
```

Browser checks accept `TYM_UI_URL`, `TYM_PLAYWRIGHT_MODULE` and `TYM_CHROMIUM`.
The default segmentation preview is `http://127.0.0.1:8791`. The separate
`segmentation-live.browser.mjs` performs one real diagram request and checks
all ten progressively cached densities without a second generation request.

## Azure Publication: 21 October 2026

The API and both UI hosts now serve this increment. The header language selector,
account button and theme toggle share a 44-pixel control height and vertical
alignment. On mobile, flags retain accessible language names without visible
labels; status moves to its own line. Twenty guest/signed-in language/viewport
alignment cases passed locally and on the published static assets.

Live checks executed all three algorithms, retained the sixteen-book metadata
checksum and English/Romanian trained NER, and verified exact Romanian source
marking with ten progressive cached densities. These used three authored diagram
generations and one invalid-option request, not a literary accuracy benchmark.
See [release verification](verification/segmentation-20261021.md) for deployment
identifiers and regression evidence. No paid model service was added.

## Research Next Step

Replace the authored feature patterns with independently annotated English and
Romanian narrative boundaries. Split by work/translation/author, not adjacent
sentences. Report boundary precision/recall/F1, WindowDiff/Pk, TS label accuracy,
uncertainty and runtime separately. Compare rules, both learned classifiers and
paragraph/sentence baselines on held-out books. Calibrate probabilities on a
separate validation split before using them for automatic confidence thresholds.

Primary implementation references:
[ML.NET SDCA logistic regression](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.standardtrainerscatalog.sdcalogisticregression),
[ML.NET averaged perceptron](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.standardtrainerscatalog.averagedperceptron).
