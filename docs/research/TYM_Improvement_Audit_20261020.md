# TYM Improvement Research: Current-Code Audit

20 October 2026. Audit API baseline: `e0b852e`; code line references below refer to that snapshot. Includes the separately tested bilingual UI increment. This report builds on [the 19 October research proposal](TYM_Research_Improvement_Proposal.md), including its interpretation of the two supplied papers. It adds fresh code inspection, a small reproducible diagnostic and current primary-source checks. It is not a new gold-corpus evaluation. A dated addendum distinguishes subsequent trained-NER implementation from the baseline findings and remaining proposals.

## Executive Recommendation

**Make the semantics defensible before making the diagrams more elaborate.** The most valuable research increment is a density-independent, evidence-linked TS/TT graph with explicit uncertainty and a work-held-out annotated benchmark. The most urgent production increment is moving from .NET 9 to .NET 10 LTS before support ends, then completing durable account deployment.

The existing non-LLM architecture is a useful baseline. Replacing it with a generative model is not necessary to make progress. First exploit the linguistic components already present in the installed models and measure the effect independently of rendering.

## Verified Findings

### 1. Density Is Not Yet Purely a Display Projection

`Program.cs:1111` rebuilds segments, tracks, relations, boundaries and endpoints inside `BuildDensityView`. Track assignments influence whether a cross-track BEFORE edge or RUPTURE is emitted (`Program.cs:1186-1241`). Story order is a per-track counter, with a fixed -0.35 adjustment for remembrance (`Program.cs:1144-1159`); this is not inferred elapsed time or a globally established chronology.

A fresh diagnostic submitted the existing authored English demo once, with all density views requested synchronously. Input: 299 UTF-16 units; output: seven events and six TS.

| Density | Tracks | All relations | TS-to-TS relations | RUPTURE boundaries | COMMUTE boundaries | Story orders differing from density 50 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 10 | 4 | 8 | 5 | 3 | 1 | 2 |
| 50 | 6 | 10 | 5 | 5 | 0 | 0 |
| 100 | 6 | 10 | 5 | 5 | 0 | 0 |

Source spans, segment types, perspective and event IDs were unchanged; the TimeML layer was also unchanged. Three endpoints were present at each density. These observations do **not** mean that differing track counts are themselves a bug: display granularity should change. They demonstrate that some exported semantic-looking topology and `story_order` values also depend on display choices.

**Improve:** infer one immutable semantic graph, then create view groups whose members reference canonical IDs. Keep narrative-order edges, temporal assertions and display connections as different types. Canonical join/split decisions must not be re-inferred because a slider merges or separates visual lanes. Rename display coordinates so callers cannot mistake them for measured story time.

**Acceptance:** all ten densities, both interface languages and every viewport preserve canonical event/TS IDs, offsets, temporal assertions and continuity operations. Only grouping, placement and display edges may differ. Test equality of the semantic graph, not just monotonic track counts.

Reproduce using `node tools/density-research-audit.mjs` with the API running, optionally setting `TYM_RESEARCH_API_URL`. It consumes one normal diagram permit. Counts are diagnostic outputs, not precision, recall or accuracy. The recorded SVG sizes were 23,796 / 25,312 / 25,312 UTF-8 bytes; these are serialized view sizes, not peak memory or performance measurements.

### 2. Missing Evidence Currently Becomes Strong Temporal Order

`Program.cs:2039` explicitly defaults an event with no temporal cue to `IMMEDIATELY_AFTER`. The TS relation code turns that into `IMMEDIATELY_BEFORE`; TimeML generation can turn it into `IBEFORE` (`Program.cs:1515-1538`, `1668-1672`). In the diagnostic, TS2-to-TS3 and TS5-to-TS6 received `IMMEDIATELY_BEFORE` with no cue and evidence identifying a text-order default.

**Improve:** retain text adjacency without converting it into immediate temporal precedence. Use internal `UNKNOWN`/unasserted candidates, provenance, supporting spans and an abstention policy. Measure whether errors arise from candidate selection, direction, label or unsupported inference. Research on faithful temporal extraction specifically supports distinguishing contextual evidence from guessed order and evaluating calibration/abstention; it does not establish a score for TYM. [Wang et al., EACL 2023](https://aclanthology.org/2023.eacl-main.39/)

**TimeML caveat:** `UNKNOWN` is an internal TYM state, not a valid `relType` in the TimeML 1.2.1 enumeration. A strict exporter should omit an unsupported TLINK or use a separately documented extension, rather than put `UNKNOWN` into a supposedly conforming file. Validate IDs, event instances and supported relation names. Add SLINK/ALINK only with an explicit treatment of factuality, subordination and aspect. [TimeML specification](https://timeml.github.io/site/publications/timeMLdocs/timeml_1.2.1.html)

**Acceptance:** adversarial cue-free, flashback, simultaneous, conditional and contradictory examples never acquire an unsupported immediate-order edge. Report directed labelled F1 and coverage on annotated data, plus contradiction rates on the asserted graph.

### 3. Baseline: Trained NER Was Disabled

`tools/tym_pos_worker.py:17` excludes `ner`, `parser`, `lemmatizer` and `senter`. The installed Romanian 3.8.0 metadata lists a pipeline containing `ner`, `parser` and `lemmatizer`; this metadata was inspected without running a new model evaluation. The Highlight entity endpoint instead uses regex name candidates, contiguous PROPN spans, contextual PERSON/LOCATION rules and conservative anaphora (`Program.cs:1023-1074`). This should not be described as full learned NER.

**First candidate: full spaCy small pipelines.** Enable trained entity spans, lemmas, dependency roles and morphology behind a provider adapter, keeping the current POS-only path as baseline. Reuse the current local worker; do not add a remote text service. Romanian models are documented as news-oriented, so literary quality must be measured rather than assumed. [spaCy Romanian models](https://spacy.io/models/ro)

**Second candidate: Stanza morphology/dependency analysis.** Compare task-equivalent processors and latency. The current official NER table does not list Romanian; therefore Stanza's Romanian linguistic support must not be advertised as an out-of-the-box Romanian NER replacement. A custom Romanian NER checkpoint would be a separate training task. [Stanza NER inventory](https://stanfordnlp.github.io/stanza/ner_models.html)

**Data choice:** distinguish RONEC versions. The maintained v2 repository reports 12,330 sentences and 80,283 entities across 15 classes, unlike the smaller corpus described by the original paper. Pin dataset revision, splits and label mapping. RONEC training/testing is useful but does not prove accuracy on the supplied novels, and a pretrained model may already have used related data. [RONEC v2](https://github.com/dumitrescustefan/ronec)

Romanian RRT supplies morphology/dependencies and includes a fiction subset within an imbalanced genre mix. Use relevant genre slices for diagnostics, but create a separate literary test set. [UD Romanian RRT](https://universaldependencies.org/treebanks/ro_rrt/index.html)

**Acceptance:** compare exact-span/type F1 on the same adjudicated literary scenes, with separate multiword-name, dialogue, OCR/diacritic and historical-name results. Do not discard a valid entity merely because its internal span contains a preposition or determiner. Keep standalone irrelevant POS items out of the highlight menu, while preserving complete named-entity spans.

### 4. Highlight Coreference and Event Actors Are Separate Paths

`AnaphoraResolution.cs` intentionally uses recent named mentions, conservative rules and unresolved references. Quoted references and several first/second-person forms remain unresolved. `Program.cs:1084-1091` uses these references to create highlight spans only when highlighting is requested; event actors are still extracted separately. Improving a dropdown's pronoun resolution therefore does not automatically improve the TS participants or TT graph.

**Improve:** create a shared entity/mention registry used by highlighting, event participants and segment continuity. Preserve original mention forms, canonical aliases, grammatical evidence, speaker/perspective and unresolved alternatives. Extend the worker DTO beyond POS/tag/possessive to include Gender, Number, Person, Case, Tense, Mood, VerbForm, lemmas and dependency heads. Evaluate cross-chapter continuity with persisted document state rather than resetting identity on each chapter request.

**Acceptance:** precision and coverage of resolved references are reported separately. An ambiguous pronoun must remain unresolved; improving coverage must not silently increase false identity merges. Add quoted dialogue, omitted Romanian subjects, clitics, aliases and scene transitions to the annotation pilot.

### 5. Existing Confidence and Pass Counts Are Not Accuracy Evidence

English ML.NET classifiers are trained from bundled seed examples (`Program.cs:779`, `912`). Romanian deliberately avoids those English classifiers. Displayed confidence is derived from model score maxima and heuristic fallbacks, without an audited held-out calibration procedure (`Program.cs:805-825`).

**Improve:** annotate TS boundaries according to time, participants and perspective; evaluate tense, TS mode and boundaries as different tasks. Split by work/translation family, not individual events. The earlier audit identifies 16 files but only 11 work groups and only one English work; adding independent licensed English material is necessary for cross-work English claims.

Compare current rules, trainable ML.NET features and a modest sequence baseline before considering larger encoders. Report macro-F1 with supports, boundary F1, candidate recall, directed relation F1, TIMEX span/type/value scores, and calibration/coverage. Keep engine-specific graph metrics separate from benchmark definitions. MATRES concerns event start-point relations and narrative axes, not all interval or TYM continuity semantics. [MATRES annotation paper](https://aclanthology.org/P18-1122/)

**Acceptance:** a versioned, adjudicated work-held-out manifest; training-only vocabulary/feature fitting; documented annotation agreement; confidence intervals resampled by independent work/document. Do not turn regression-test pass rates or entity counts into NLP accuracy percentages.

## Production and Interaction Priorities

### Move to .NET 10 LTS Before 10 November 2026

Both projects, helper projects and both Dockerfiles target .NET 9. Microsoft's current policy gives .NET 9 an end-of-support date of **10 November 2026** and .NET 10 LTS support through **14 November 2028**. As of this report, .NET 9 is supported but has only 21 days remaining. Align ASP.NET/EF/Data Protection packages and container images during migration, and rerun account, SQL-schema, offset, worker and browser regressions. This is a maintenance deadline, not evidence of a present exploit. [Official lifecycle](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

### Make Whole-Book Execution Cancellable and Byte-Bounded

`PosTagging.cs:41-50` serializes worker access under a lock and synchronously waits for a response with a fixed 60-second timeout. `DensitySessions.cs:25-30` serializes each session and checks cancellation around a synchronous layout, not during it. Browser aborts therefore do not establish prompt cancellation of expensive NLP/layout work.

The density session limit is weighted by source characters, not retained heap bytes. The server stores a prepared generator; the browser accumulates returned density views. A repeated density GET regenerates its view rather than serving a memoized layout. The synchronous-cache path can also retain rendered strings. These are distinct costs and need separate measurements. MemoryCache size units are application-defined and not a measured RAM ceiling. [Microsoft cache guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-10.0)

**Improve:** cancellable analysis jobs with bounded admission, progress, idempotency and explicit stage budgets; cooperative cancellation inside tokenization/layout loops; immutable semantic cache plus byte-bounded layout cache. Memoize repeated layouts and coalesce concurrent identical requests. Virtualize SVG tracks before transferring/rendering the complete book, not only after a giant SVG already reaches the browser. Preserve selected-density-first behavior. The async request/reply pattern provides an implementation starting point, not a requirement to purchase a particular queue service. [Azure architecture pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/asynchronous-request-reply)

**Acceptance:** matched cold/warm p50/p95, peak server and browser memory, bytes sent, queued work and cancellation latency under short/chapter/full-book workloads. Proposed cancellation target: stop queued work immediately and active work within a documented small bound; measure it before promising a specific latency. Keep semantic output equivalent between monolithic and stateful chapter processing.

### Finish Account Storage and Harden Quotas

The approved free-limit Azure SQL deployment is still blocked by regional provisioning restrictions; live accounts remain disabled. Keep it that way until region access, schema/runtime permissions, persistent token keys, real delivery/recovery and restart tests pass. Do not substitute container-local SQLite or a paid database without authorization. SQL key persistence is implemented but not an independently Key-Vault-wrapped production key ring.

Five-per-IP quotas use process-local fixed 24-hour windows, not durable calendar-day allowances. Restarts reset them; multiple replicas would get independent counters. The custom IP resolver reads forwarding headers directly, relying on the deployment's proxy behavior. Test direct/untrusted-header requests as well as Azure ingress and constrain trusted forwarding explicitly. [ASP.NET proxy guidance](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)

**Acceptance:** restart/replica/concurrency tests preserve a shared atomic quota; trusted client IP is verified; NAT/shared-network behavior is documented. Public text reading remains an explicit user policy, not something to silently reverse. Add per-book rights/access metadata and retention controls; absence of copy/download buttons is not a protection against copying displayed text.

### Add Evidence Inspection and Human Correction

The best next UI feature is a link from each TS/event/relation to its supporting source span, with method, uncertainty and editable corrections. Provide a compact textual graph view, keyboard navigation, semantic role/name/state on result tabs, and feedback that is distinguishable from source or model output. Capture annotation revisions separately from original text; do not use user corrections for training without a documented dataset policy.

Bilingual controls, chapter labels, account messages and SVG legends are now implemented and tested locally. Remaining language work includes localized server-sent emails/confirmation landing experiences, reviewed terminology and structured API diagnostic codes rather than English text keys. Locale must remain separate from analysis language and semantic cache keys.

Current 320-1920-pixel checks and visible legends are engineering evidence, not an accessibility certification. Add keyboard, screen-reader, 200% text zoom, focus and status-announcement checks in both languages. A two-dimensional diagram may need its own scrolling area while the surrounding controls reflow. [W3C reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html), [status messages](https://www.w3.org/WAI/WCAG22/Understanding/status-messages.html)

## Proposed Experiments and Decision Gates

| Experiment | Controlled comparison | Primary outcome | Decision gate |
| --- | --- | --- | --- |
| Semantic invariance | Current density-dependent topology vs canonical graph plus projection | Semantic equality for every density, viewport and locale | All canonical facts invariant; visual grouping may vary |
| Evidence-aware relations | Adjacency default vs abstention-aware rules | Directed relation F1, unsupported assertions, coverage | Better held-out quality without hiding reduced coverage |
| Linguistic adapter | Current POS/rule candidates vs full spaCy; Stanza for equivalent supported tasks | Literary NER span/type F1; participant errors; latency/RSS | Gain on the same held-out scenes within measured resource limits |
| TS definition | Tense/cue baseline vs time + participants + perspective | Boundary F1 and adjudicated disagreement categories | Improvement on held-out works, not repeated scenes |
| Long-text jobs | Synchronous book processing vs cancellable stateful jobs | Equivalent semantic graph, memory, p95 and cancellation | No truncation or cross-chapter identity loss; bounded work |

Start with a double-annotated pilot, not a claimed large benchmark: approximately 12 scenes from six work groups, including dialogue, recollection, hypothetical events and difficult Romanian forms. Use the pilot to revise guidelines and estimate the annotation effort before fixing a main-study sample size. Register comparisons, resource limits and practical improvement thresholds before examining the locked test set.

## Recommended Order

1. **Now:** schedule .NET 10 migration; freeze a canonical semantic schema; add no-cue/uncertainty and density-invariance fixtures. Retain the current working UI and selected-density-first behavior.
2. **Next research increment:** add an evidence inspector and shared entity registry, enable full spaCy behind an adapter, and create the double-annotated literary pilot.
3. **Next serving increment:** cancellable jobs, measured memory limits, page-level rendering, repeated-layout memoization and quota/proxy tests. Resolve Azure SQL access separately before activating accounts.
4. **After the baseline is measurable:** train TS/relations, evaluate calibration and optional sequence models, and investigate advanced coreference/temporal normalization. A generative LLM is not a prerequisite.

The audit itself did not replace research models, migrate .NET, activate accounts or purchase cloud resources. Further NLP quality claims require the controlled experiments above.

## Implementation Follow-Up: 20 October 2026

After this audit, trained NER was enabled in the existing local English/Romanian spaCy worker at the user's request. The highlight picker now consumes complete person, place and organization spans with nominal POS evidence. Internal function words are retained. Uncovered proper-name rules remain explicitly identified as fallback, and cannot create fragments inside a model span. The response includes active components, model/version, original labels and per-option provenance. This is pretrained inference, not new training on the supplied books. [Implementation details and limits](../trained-ner.md)

Eight worker tests, actual bilingual person/organization fixtures, deterministic span/provenance checks and the bilingual browser suite passed locally. A known Romanian model error labels Johan as a location in one authored sentence; the resulting ambiguous plural reference is left unresolved. These tests do not establish a literary NER accuracy score. Parser/lemmatizer enablement, a shared entity registry, canonical density-independent semantics, trained temporal extraction and the proposed gold benchmark remain future work. Event actors and TimeML still use their existing extraction paths. Live account activation remains blocked by persistent database provisioning.
