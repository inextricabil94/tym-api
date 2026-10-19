# TYM .NET API Prototype

TYM is a small ASP.NET Core Minimal API for turning narrative text into time-yard diagrams. It is based on the time-yard model from Cristea and Macovei: a text is represented as time tracks (`TT`), time segments (`TS`), endpoints, and temporal relations. The generated SVG mirrors the example image by showing:

- a text-order segment chain, where `TS` items appear in reading order
- a time-yard view, where segments are arranged by narrative tracks
- start/stop markers, joins/splits, rupture/commute hints, and segment labels

This prototype now follows the implementation directions from the supplied papers more closely. It first extracts event-like clauses, attaches actors, temporal anchors, locations, actions, and a `Past`/`Present`/`Future` temporal category, then groups contiguous compatible events into time segments. ML.NET is used for event temporal-category classification and for each candidate time segment's narrative type (`NAR`, `REM`, `SUP`, `GEN`, or `FIC`). The bundled models are trained at startup from seed examples in `Program.cs`; a production implementation should replace those examples with the annotated TYM corpus.

No LLM is used by the API pipeline. Event/temporal extraction uses rules plus ML.NET classifiers; entity highlighting additionally uses local statistical spaCy POS models. The renderer is deterministic JSON/SVG/XML generation. The JSON/XML output now also includes a TimeML-style layer with `EVENT`, `TIMEX3`, `SIGNAL`, `MAKEINSTANCE`, and `TLINK` annotations so the TYM diagram can be compared with standard temporal NLP tooling. Each response also includes an `analysis` object with extractor counts, confidence averages, classifier sources, distribution statistics, and quality issues.

English is the default extraction language. Romanian narrative text is supported with `options.language = "ro"` using Romanian rule profiles for named-entity candidates, temporal anchors, temporal signals, event tense, TimeML-style labels, and TYM segment/track creation.

## Example

Input text:

> Adam and Johan grew up in the same house. Years earlier, Karl had carried Adam through the rain. Margaret remembered her mother in Jakarta. Meanwhile, Karl traveled alone across the island. Adam searched for Margaret after the storm. Margaret found Adam at the doorway. Adam disappeared before dawn.

Generated diagram:

![Generated TYM diagram](sample_output.svg)

The generated SVG includes a legend for solid lines, dotted lines, track start/stop triangles, segment boxes, boundary ticks, and inferred JOIN/SPLIT endpoint labels.

### Diagram readability and tag icons

Time tracks display their identifiers only (for example, `TT1`), without generated titles. Long tracks continue on new rows rather than shrinking markers/labels into the available width. Segment descriptions wrap across up to three lines; SVG tooltips retain their full text. Track identifiers, endpoint badges, mode labels, and segment markers occupy separate vertical bands. The text-order strip samples evenly spaced IDs on large inputs, including the first and last, while the time-yard SVG retains every segment.

The viewer opens at 100% with scrolling and filters by track ID or character name. **Display groups** offers 0-10: **0** keeps twelve tracks per page; **1-10** partitions the total tracks into that many balanced, contiguous displays. Impossible group counts are disabled when there are fewer tracks. A group selector, track ranges, and previous/next controls navigate the displays. Search finds matching tracks across all groups while preserving their original group boundaries. Paging, grouping, and filtering use the existing SVG without another analysis request. Fit, zoom in/out, and actual-size controls remain available. SVG downloads contain all tracks; the displayed group/page is only a navigation view.

The UI uses the supplied DataSpace visual system: charcoal surfaces, emerald/mint accents, a 64-pixel grid, restrained 3-pixel controls, and self-hosted Space Grotesk, Inter, and IBM Plex Mono fonts with Romanian support. Dark is the default; the header theme button toggles the matching light palette and stores only the theme preference locally. The on-screen SVG follows the selected theme; downloaded SVGs keep their original self-contained print colors. No DataSpace biography, contact form, or CV is copied into TYM.

**Highlight entity** is a select menu populated by `POST /v1/entities` for the exact current text/chapter and extraction language. Contextual English/Romanian spaCy POS tagging filters the rule-based entity candidates: names require `PROPN`; standalone reference options require `PRON`. Verbs, auxiliaries, adverbs, interjections, conjunctions, articles/determiners, prepositions, common nouns, numerals, punctuation and symbols cannot become standalone options under their assigned tags. People and places show explicit plus resolved mention counts and reference forms such as `he`, `his`, `el`, or `ea`. Resolved possessives/place adverbs can remain linked evidence, not separate determiner/adverb options. A separate **Unresolved references** group permits literal pronoun highlighting when an antecedent cannot be chosen safely. Lookup is debounced by 600 ms; stale requests are canceled when the text, chapter, language, or API URL changes. The refresh icon retries a failed lookup. Choose an option and generate to emphasize matching tracks. The API response records UTF-16 spans, reference antecedent(s), resolution reason, `parts_of_speech`, `pos_filter` model metadata and method `spacy-pos+rule-ner-anaphora-en-ro-v3`.

The anaphora baseline uses a two-sentence active context, unambiguous recent antecedents, local reflexives, adjacent relative pronouns, explicit coordinated groups, and place references (`here`/`there`, `aici`/`acolo`). Grammatical-gender evidence comes from resolved pronouns, not a guessed gender dictionary of names. First/second-person, unsupported neuter/demonstrative, ambiguous, and quoted references remain unresolved. There is no LLM, pretrained coreference model, or full syntactic/discourse parser. These are conservative heuristics, not measured coreference accuracy. This feature affects the highlight picker and highlight spans only: it does not rewrite actor extraction, track grouping, narrative events or TimeML. Named-entity highlighting uses actual explicit/resolved source spans instead of assuming every inherited actor mention is correct.

Entity lookup accepts up to 2,000,000 UTF-16 code units and uses a separate 30-requests/minute/IP limit. It does not consume the five daily diagram generations. Like diagram generation, it sends the current text to the selected API URL. HTTP responses use `no-store`; neither text nor entity lists are stored in browser storage. The API caches at most eight POS results (bounded by 2,000,000 input code units) in process memory. Tagging is local to the API host; no LLM/cloud NLP service receives text. Missing models/runtime return HTTP `503`, with no unfiltered fallback. POS errors can still omit names or retain mistagged words, especially literary/OCR text and homographs; this is not measured NER/coreference accuracy. See [POS setup and limitations](docs/plans/pos-filtering.md).

### Track titles and density

TT names in JSON/XML remain short extractive summaries of their events, but are not displayed in SVG diagrams or TT tooltips. A deterministic, bilingual stopword-filtered term-frequency scorer selects a representative event, then limits the title to twelve words and at most 99 characters. It does not call an LLM or an external service. Titles are baseline summaries, not validated semantic annotations; actor-based search and highlighting still use the underlying segment actors.

The UI's **TT density** slider sets `options.track_density` (integer 0-100, default 50). **0** gives an overview by coarsening actor/perspective tracks within the same narrative mode (main, memory, parallel) and temporal category. **50** preserves the original actor/perspective/temporal grouping. **100** creates one TT per temporal segment. Intermediate values deterministically refine these groups; discrete inputs can produce plateaus. Every event, segment, source span, and TimeML annotation remains available at every density. Track-level boundaries, relations, and endpoints are recomputed for the chosen grouping. Low-density overviews are not claims that different characters share one development.

`track_generation` in the response records the effective density and `extractive-event-summary-v1` title method. Change the slider and generate again to apply it; each generation uses the normal daily quota.

The UI generates a **density cache** for **1, 10, 20, 30, 40, 50, 60, 70, 80, 90 and 100** with each analysis. **Cached TT density** in the viewer switches diagrams immediately without a request or another daily usage. Counts, track search/groups, Analysis, JSON/XML and downloads follow the selected view. Input changes (text/chapter, language, API URL, width or highlight) discard the cache; the cache is held only in page memory, never local storage. The slider can select an already cached preset directly; other slider values require a new generation. The originally requested density remains available even when it is not a preset.

**Diagram width** follows the current browser window width in CSS pixels, within the SVG renderer's 720-2400 pixel bounds. The current window width and height are shown beneath it. Cached diagrams belong only to that exact window resolution: any width or height change discards them, including changes within the mobile width clamp. Returning to an earlier size does not restore its old cache. Responses generated for a previous resolution are ignored. Resizing never starts generation or consumes another daily usage automatically; generate explicitly for the new size.

API clients opt in with `options.cache_track_densities = true` on `POST /v1/diagrams`. Event extraction, POS/reference highlighting, classification and TimeML run once. Only density-dependent track layouts, statistics and exports are recomputed; identical partitions share payloads. `density_cache.options` maps presets to `base` or an entry in `density_cache.views`. Alternate views contain track/segment placements and topology rather than eleven duplicate copies of the source events. JSON responses support compression and use `no-store`. Precomputing layouts increases response size and generation time for long inputs; it is not persistent or cross-chapter analysis caching.

The self-contained SVG embeds seven icons and a labeled legend: play for `NAR`, rewind for `REM`, question mark for `SUP`, repeat for `GEN`, sparkles for `FIC`, merge for `JOIN`, and split for `SPLIT`. Color supplements the shapes and text labels. `GEN`, like `REM`/`SUP`/`FIC`, uses the legend's dotted segment line. Icons require no external font or CDN; licensing notices are retained in the SVG metadata and [third-party notices](THIRD_PARTY_NOTICES.md).

SVG numeric coordinates use invariant formatting regardless of server locale. Invalid XML control characters from PDF text are normalized only in displayed labels/tooltips, preserving the original input and analysis offsets. See [rendering verification](docs/plans/rendering-verification.md) for tested widths, locales, and full-book counts.

### Local literary example

The user-supplied Romanian Tash Aw PDF and fifteen text files are available as sixteen complete inputs, with a separate local excerpt for each. The PDF's full input contains extraction from all 352 pages. Sources, extraction choices, work/translation/volume groups, and API request fixtures are documented in [the example guide](docs/plans/examples.md). Original books, extracted prose, and previews remain excluded from Git and ordinary publishing/Docker contexts. A separately prepared server-only library can be deployed explicitly as described below.

Chapter detection recognizes line-start `Capitolul`, `Capitol`, and `Chapter` headings with Arabic/Roman numerals, digit ordinals (`21st`, `al 2-lea`), and Romanian/English spelled-out cardinal or ordinal numbers (`UNU`, `DOI`, `TREI`, `PATRU`, `ONE`, `TWO`, `THREE`, `FOUR`, `al doilea`, `Twenty-First`). Matching ignores case and number-word diacritics while preserving original source spelling and offsets. Original/repeated numbering, front matter, whitespace, and the complete final chapter are preserved. The selector and previous/next controls submit the exact selected chapter.

**Chapter detection** has three modes. **Auto** also accepts standalone consecutive numeral/number-word sequences with at least three headings and at least 500 UTF-16 units between headings; bare numbering is disabled for text containing PDF form feeds to avoid treating page folios as chapters. **Prefixed headings only** disables those heuristics. **Include short numbered sections** explicitly permits two-or-more consecutive standalone headings in short inputs/excerpts, including PDF text; inspect the resulting ranges because page numbers/lists can then be mistaken for chapters. This is structural detection, not validated chapter annotation. Chapter mode works on whichever full text or excerpt is currently loaded and never silently substitutes the full book for an excerpt. Pasted text is supported too. Returned event/TimeML spans are chapter-local; add the separately displayed source start offset to map them to the complete input. Independent chapter analysis does not infer cross-chapter coreference or temporal links.

In a Development UI process accessed over loopback, an existing `private-examples/examples.json` adds a book selector with source attribution, full-text/excerpt controls, and the loaded character count. Complete text loads on demand through a local-only endpoint. Preview pictures are explicitly tied to excerpts. Production and remote requests do not receive the private catalog or full texts. `TYM_PRIVATE_EXAMPLES_FILE` can override the catalog path. The original English and Romanian demos remain available without private files.

### Hosted Book Library

**Book library** makes the existing sixteen complete inputs available for analysis without sign-in. The source files live only in the API's private container image, outside any static website directory. `GET /v1/books` returns metadata and chapter/section choices, not book contents. The UI submits a book ID and optional section ID to `POST /v1/books/{id}/diagrams` or `POST /v1/books/{id}/entities`; book language is fixed by the catalog. Entity recognition is explicitly requested with the refresh button rather than running for every selected chapter. Book generation shares the existing five-per-IP daily quota; entity lookup shares the separate thirty-per-minute quota.

Book mode has no source-text editor, copy command, JSON/XML export, or raw JSON/XML view. Diagram SVG and structured temporal analysis remain available. **This is not DRM or an access-control guarantee:** public analysis results, TimeML data, SVG labels/tooltips and network responses can contain source excerpts and can be copied. No source-text GET/download endpoint is provided. Only deploy books you are authorized to make available for public analysis.

Preparation verifies the original UTF-8 SHA-256 and UTF-16 length, then writes separate cleaned copies: LF line endings, no BOM or paragraph indentation, trailing whitespace removed, at most one blank separator line, and a final newline. Plain-text prose uses blank-line paragraph separation rather than code-style indentation. Words, diacritics, punctuation, hyphens and intentional single line breaks are unchanged. There is no spelling correction, dehyphenation or OCR. Originals are never overwritten. Chapter offsets apply to the **cleaned** edition, not the original file.

Every cleaned character belongs to a chapter/reading section. Inputs without detectable headings and chapters over 100,000 UTF-16 units are divided into clearly labeled reading sections at paragraph/word boundaries, without discarding text. The hosted library accepts up to 100,000 units per analysis to bound response/layout memory; larger full-book mode is disabled, not silently truncated. Chapter processing is independent, not whole-book coreference inference.

```powershell
node tools/prepare-book-library.cjs private-examples build_publish/my-library/library
$env:TYM_LIBRARY_PATH = "$PWD/build_publish/my-library/library"
dotnet run
```

Use a new output directory for each preparation. The generated catalog and all source files are ignored. For explicit Azure deployment, build the ordinary API image from its public-only context, then use [the library image layer](deploy/Dockerfile.library) in a separate ignored context containing only that Dockerfile and `library/`. Keep the registry private. Never upload `library/` to `$web` or commit it. An unconfigured API returns an empty book list.

The workbench includes four self-hosted, credited example images: Jakarta, a Jules Verne portrait, the 1871 *Twenty Thousand Leagues* frontispiece, and an 1843 *Christmas Carol* cover. Historic images are not presented as covers of the supplied Romanian editions. See [image sources and licenses](THIRD_PARTY_NOTICES.md#workbench-example-images).

### Improvement plans

The [two-prototype roadmap](docs/plans/README.md) separates the [diagram API plan](docs/plans/diagram-api.md) from the [ML.NET Corpus Explorer plan](docs/plans/corpus-explorer.md). The second plan references the separately reviewed corpus project; this checkout does not contain that application. Planned work, recorded software checks, and research accuracy are explicitly separated.

## Minimal React UI

Live UI:

[https://tymui20260707serban.z13.web.core.windows.net](https://tymui20260707serban.z13.web.core.windows.net)

Container App UI:

[https://tym-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io](https://tym-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io)

Live API:

[https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io](https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io)

The UI is in [ui/Tym.Ui](ui/Tym.Ui). It serves a minimal React page with English and Romanian examples, calls `POST /v1/diagrams`, displays extraction counts, renders the returned SVG, supports fit/zoom inspection, exposes Analysis/TimeML/JSON/XML result tabs, and can download SVG/JSON/XML outputs.

## Run

For the POS-filtered entity picker and entity highlighting, install Python 3.11 or 3.12 and configure the local models once (downloads packages/models; not user texts):

```powershell
.\tools\setup-pos.ps1 -Python python
```

Then start the API from the repository root:

```powershell
cd tym-api
dotnet run --urls http://127.0.0.1:8765
```

Then call:

```powershell
Invoke-RestMethod `
  -Uri http://127.0.0.1:8765/v1/diagrams `
  -Method Post `
  -ContentType 'application/json' `
  -Body (Get-Content .\sample_input.json -Raw)
```

Romanian sample:

```powershell
Invoke-RestMethod `
  -Uri http://127.0.0.1:8765/v1/diagrams `
  -Method Post `
  -ContentType 'application/json' `
  -Body (Get-Content .\sample_input_ro.json -Raw)
```

To get only SVG:

```powershell
Invoke-WebRequest `
  -Uri http://127.0.0.1:8765/v1/diagrams/svg `
  -Method Post `
  -ContentType 'application/json' `
  -Body (Get-Content .\sample_input.json -Raw) `
  -OutFile .\sample_output.svg
```

Run the UI locally against the Azure API:

```powershell
dotnet run --project .\ui\Tym.Ui\Tym.Ui.csproj --urls http://127.0.0.1:8780
```

Run the UI locally against a local API:

```powershell
$env:TYM_API_BASE_URL='http://127.0.0.1:8765'
dotnet run --project .\ui\Tym.Ui\Tym.Ui.csproj --urls http://127.0.0.1:8780
```

## Endpoints

- `GET /health`
- `GET /` returns service metadata and the available endpoints
- `POST /v1/entities` returns POS-filtered names and pronoun references
- `POST /v1/diagrams` returns normalized diagram JSON plus embedded SVG
- `POST /v1/diagrams/svg` returns `image/svg+xml`
- `POST /v1/diagrams/xml` returns paper-style XML notation

The three diagram-generation endpoints share a fixed-window rate limit of five requests per client IP every 24 hours. Health, service metadata, and OpenAPI requests are not limited. A rejected request returns HTTP `429`, a `Retry-After` header, and a JSON body containing `limit`, `window`, and `retry_after_seconds`.

When hosted behind Azure Container Apps, the API uses the rightmost valid address in `X-Forwarded-For`; otherwise it uses the direct connection address. The limiter is held in application memory, so its counters reset when the API process restarts and are independent per replica. Keep a single API replica for strict enforcement, or replace the policy store with a distributed counter when scaling out.

See [openapi.yaml](openapi.yaml) for the full contract.

The API also serves the contract at:

```text
http://127.0.0.1:8765/openapi.yaml
```

## Core Data Model

`NarrativeEvent` is the event-level unit suggested by the second paper:

- `id`: stable event id, such as `EV1`
- `actors`: person entities participating in the event, with carry-forward when absent
- `temporal_anchor`: detected temporal expression or inherited anchor
- `location`: lightweight location phrase
- `action`: detected verb/action cue
- `temporal_category`: `Past`, `Present`, or `Future`
- `span_start`, `span_end`: source character offsets

`TimeSegment` corresponds to `TS` in the paper:

- `id`: stable segment id, such as `TS1`
- `text`: source span
- `track_id`: owning time track
- `type`: `NAR`, `REM`, `SUP`, `GEN`, or `FIC`
- `perspective`: narrator, character, or unknown
- `text_order`: source order index
- `story_order`: layout order inside the track
- `actors`: stable character set for the segment
- `location_id`: associated `TL` location id
- `temporal_anchor`: associated temporal expression
- `temporal_category`: `Past`, `Present`, or `Future`
- `event_ids`: events grouped into this segment
- `confidence`: ML.NET classifier confidence for `type`
- `classifier`: `mlnet_seed_model` or `heuristic_fallback`

For Romanian (`language = "ro"`), event temporal category and segment type are currently rule-based (`romanian_rule_event_temporal`, `romanian_rule_segment_type`) because the bundled ML.NET seed examples are English.

`TimeTrack` corresponds to `TT`:

- `id`: stable track id, such as `TT1`
- `name`: mnemonic track name, usually inferred from character entities
- `left_endpoint`: `START` or split endpoint id
- `right_endpoint`: `STOP` or join endpoint id

`Endpoint` captures joins and splits:

- `type`: `JOIN` or `SPLIT`
- `track_ids`: the involved tracks
- `segment_id`: the segment where the event is inferred

`TimeRelation` captures shallow temporal constraints:

- `from_id`, `to_id`
- `rel`: `BEFORE`, `IMMEDIATELY_BEFORE`, `AFTER`, `IMMEDIATELY_AFTER`, or `SIMULTANEOUS`
- `cue`, `evidence`: the lexical cue and rule explanation, when available

`time_ml` is a TimeML-style annotation layer:

- `events`: event heads with TimeML event class, such as `OCCURRENCE`, `STATE`, `I_STATE`, or `PERCEPTION`
- `timex3`: temporal expressions with rough `DATE`, `TIME`, `DURATION`, or `SET` typing and normalized values such as `PAST_REF`
- `signals`: cue words such as `meanwhile`, `years earlier`, `then`, or `after`
- `make_instances`: event instances with tense, aspect, polarity, and POS
- `tlinks`: event-event and event-time temporal links using TimeML-style relation labels such as `IBEFORE`, `BEFORE`, `SIMULTANEOUS`, and `IS_INCLUDED`

## Production Notes

The API boundary is intentionally separate from extraction. The current pipeline is:

1. split text into event-like clauses using punctuation, conjunction, and verb cues
2. extract actor, temporal anchor, location, action, and offset features
3. classify each event as `Past`, `Present`, or `Future` with ML.NET for English, or Romanian temporal rules for Romanian
4. concatenate adjacent events with compatible temporal category and actor unity into `TS`, while forcing a new segment when explicit retrospective, forward, simultaneous, or changed-anchor cues indicate a temporal boundary
5. classify each `TS` as `NAR`, `REM`, `SUP`, `GEN`, or `FIC` with ML.NET for English, or Romanian narrative-mode rules for Romanian
6. infer TT membership, boundaries, endpoints, and relations
7. emit TimeML-style EVENT/TIMEX3/SIGNAL/MAKEINSTANCE/TLINK annotations
8. emit analysis statistics and quality issues
9. render JSON, SVG, and paper-style XML

The renderer consumes normalized TYM JSON, so extraction can be upgraded independently.

## Non-LLM Framework POCs

The comparison suite in [pocs/nlp-framework-comparison](pocs/nlp-framework-comparison) implements the article pipeline as non-LLM proof of concepts and reports statistics for:

- article-guided rules
- spaCy NER/POS/dependency preprocessing
- Stanza NER/POS/dependency preprocessing
- SUTime/CoreNLP availability
- HeidelTime availability
- current saved .NET API output

See [poc_report.md](pocs/nlp-framework-comparison/results/poc_report.md) for the generated statistics and [llm_considerations.md](pocs/nlp-framework-comparison/llm_considerations.md) for where an optional LLM layer would fit.
