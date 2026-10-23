# Selectable Temporal Segmentation

## Scope

Version 0.9 corrects **temporal-development segmentation**. Sentences/clauses
remain lower-level event candidates, not TS boundaries. A TS may span many
sentences and paragraphs in a coherent development. Context is retained until
supported temporal or development evidence changes it. No LLM, cloud inference,
subscription, model download or additional NuGet package is required.

This implements a conservative subset of the supplied SYNASC 2016 model, not a
complete automatic parser for its actor/time/space/type/perspective definition.
Literal entity-set, location, paragraph and tense-label changes alone no longer
delimit TSs. A journey can remain one TS; explicit parallel developments,
communication/separation cues, world-plane or perspective changes can delimit
developments. The paper supplies the model, not this implementation's classifier.
See [the dated changes document](research/TYM_Temporal_Segmentation_Changes_20261023.md).

The workbench's **Temporal segmentation algorithm** menu and
`options.segmentation_algorithm` offer:

| Value | Implementation | Score |
| --- | --- | --- |
| `article_rules` | Stateful temporal/development continuity rules; default, compatibility ID retained | None |
| `mlnet_logistic` | ML.NET SDCA binary logistic regression, 100 iterations, L1=0, L2=0.0001, one training thread, seed 42 | Model probability, not empirically corpus-calibrated |
| `mlnet_perceptron` | ML.NET averaged perceptron, 100 iterations, seed 42 | Signed decision margin, not a probability |

Both ML algorithms are real trained binary models. They learn from **48 authored
temporal feature-pattern rows** in [Segmentation.cs](../Segmentation.cs), including
repeated continuation examples for weighting. These are **not annotated literary
passages, a pretrained language model, or an evaluation corpus**. The models are
small experimental baselines, not research-validated segmentation systems. No
accuracy, F1, calibration, or generalization claim follows from the checks below.

Eight features are used: changed supported time anchor, retrospective entry,
narrative-time return, explicit forward jump, parallel-development cue,
development-frame change, weak perfect/future tense candidate and same-time
reference. Context survives unanchored events. ISO dates use DateOnly validation;
year/date containment is compatible and a year does not erase a finer date.
Weekdays, clocks and a bounded English/Romanian relative/deictic cue set are
supported. Durations alone are not new time frames. Standalone temporal headings
are attached to the following recognized event, retaining original offsets.

Models initialize lazily once per process and protect the reused ML.NET
prediction engine with a lock. Choosing ML does not silently fall back to rules
if model training or prediction fails. Explicit context shifts are mandatory;
no temporal/development evidence vetoes a boundary. ML decides weaker tense
candidates; it is not used to manufacture semantic shifts from syntax. Raw
scores need not agree with a forced/vetoed final boundary. The document-start boundary is structural,
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
start offset, optional raw score, observed features, `basis`, `frame` and
`time_reference`. `segmentation.unit` is `temporal_development`. Feature observations
are not causal feature-attribution explanations. Existing `segment.confidence`
and `segment.classifier` still describe the **TS type**, not boundary prediction.

Boundary decisions are calculated once before density layout. All ten cached TT
density views retain the same segmentation metadata, event coverage and TS spans;
only track assignments and related presentation change. Algorithm changes in the
UI invalidate the current result and density cache, cancel background cache work,
and reject stale generation responses. A legacy API cannot silently provide a
rule result when the user selected ML. The v0.9 UI also rejects responses that do
not advertise temporal-development semantics. API model and browser cache epochs
are bumped so v0.8 phrase-like results are not silently reused. Older saved
diagrams become ineligible; the new epoch does not promise recovery of old data.

## Marked-Text View

The **Segmentation** result tab and **Inspect segments** button open the source
view. TS ID buttons select a segment; colored source spans and the type legend
mark its boundaries. The inspector reports its type, TT, event IDs, exact
half-open UTF-16 range, context, retained time reference, boundary basis, raw model
score and observed features. The TT arrow opens
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

## Current Checks: 23 October 2026

The release passes 93 temporal-development cases across all three
algorithms, including a 180-event/60-paragraph single-scene test. Six bilingual
source/span/score/concurrency cases remain. Exact TS text comes from original
source ranges; density does not alter TS boundaries. The 21 October counts below
describe the older implementation and are not the current expected outputs.
Published on 23 October 2026 as `release-20261023-temporal` on the API and both UI
hosts. Production SQLite remains disabled. See the
[publication verification](verification/temporal-20261023.md).

## Historical Checks: 21 October 2026

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
