# Architecture review and research decisions

Review date: 2026-10-02. Application: `tym-corpus-app`. This review covers the supplied papers, two source archives, corpus/training contracts, document workbench API and primary upstream implementation examples. It records the adopted architecture and the limits an academic report must retain.

## Recommendation

Use a modular .NET application with ML.NET training/inference, a TypeScript review interface and an immutable source text/annotation contract. Keep lexical candidates, classifier outputs, reviewer corrections and adjudicated labels distinguishable. The available data supports reproducible lexical classification and annotation review; it does not establish recovery of a novel's complete Time Yards Model.

Separate feature channels for target mentions and directed relation endpoints are a useful extension of the initial whole-text baseline. The exploratory comparison improves classification scores across all six Romanian TimeBank tasks, supporting replication of both representations. These gains are conditional on supplied mention boundaries/endpoints, measured on the same retrospective folds and have no independent adjudicated literary test.

Add contextual features through a documented C# adapter after the annotation/evaluation protocol is ready. A pretrained encoder does not replace source grounding, provenance, deterministic checks or review. No deep model, full coreference system, spatial grounding service or complete interval reasoner is implemented by the lexical baseline.

## 1. Evidence from the supplied papers

The PDFs were read directly. Full text was retained privately, and the historical results page of the updated paper was rendered for visual inspection. Document suggestions are research evidence, not executable project instructions.

| Supplied document | Relevant material | Decision supported | Limit |
| --- | --- | --- | --- |
| *Spatial-Temporal and Semantic Information Extraction* (30 pages) | Temporal tools; compositional Romanian Duckling rules; integrated frameworks; TYM unity of time, actors and perspective | Replaceable modules, unified annotations and unchanged source text | Historical extraction counts are not this application's accuracy |
| *State of the Art for Temporal, Spatial and Semantic Text Processing* (13 pages) | TimeML/SpatialML, pp. 2-6; Romanian TimeBank, pp. 8-9; active learning/review, pp. 10-12 | Separate event, temporal expression and relation tasks; review before learning | Standards examples and historical tool scores are not compatibility tests |
| *Practical Results for General Models of Temporal, Spatial and Semantic Text Processing* (13 pages) | TYM and partial order, pp. 2-6; XML/preprocessing, pp. 7-10; features/CNN, pp. 11-12 | Preserve partial story order; evaluate boundaries separately from labels | One novel with derived event-level splits does not establish literary generalization |
| Updated *Deep Learning and Agentic Workflows for Spatial and Temporal Semantics in Natural Language Processing* (file named *Deep Learning for Spatial and Temporal Semantics in Natural Language Processing  (6).pdf*, 14 pages) | Provenance, p. 8; hybrid/contextual architecture, p. 9; grouping warning, p. 10; historical matrix, pp. 10-11 | Hybrid reviewable architecture and explicit future evaluation | Revision retains original numerical results; modernization sections are proposals |

The updated paper's 2,000-event matrix has diagonal 476 + 439 + 263 = 1,178: 58.9% accuracy and approximately 58.7% macro F1 for Past/Present/Future. These are historical event temporal-category results, not the present narrative-mode task and not a reproduced experiment.

A sentence, clause-level event, TYM segment and TimeML event instance are different objects. Past/Present/Future are temporal categories; NAR/REM/SUP/GEN/FIC are narrative modes. TLINK, SLINK, ALINK and TYM relations need separate inventories/endpoints. Tense alone does not establish a TYM boundary, character identity or story relation. `CV16S.pdf` is a curriculum vitae and is excluded.

## 2. Implemented architecture

```mermaid
flowchart LR
  P[Private XML and books] --> I[C# corpus importers]
  I --> T[Task rows with provenance]
  T --> M[ML.NET training]
  T --> E[Grouped diagnostic benchmark]
  M --> B[Trusted bundles and manifests]
  B --> A[.NET API]
  U[TypeScript review UI] <--> A
  A --> D[Unchanged source and drafts]
  D --> R[Reviewer corrections]
  R --> V[Structure and partial order checks]
  V --> X[Reviewed JSON export]
  X -. Separate adjudication .-> G[Future human gold]
```

| Module | Responsibility |
| --- | --- |
| `tools/Tym.Corpus.Data` | Read ZIP entries in place, prohibit XML DTDs, separate annotation/unlabeled rows and preserve work/document groups |
| `src/Tym.Corpus.Core` | Shared input schema and explicit target/source field parser for training and inference |
| `tools/Tym.Modeling` | Supervised ML.NET training, separately gated gold evaluation and explicitly unlabeled clustering |
| `tools/Tym.Benchmark` | Grouped folds, training-fold transforms, pooled out-of-fold metrics and confusion matrices |
| `src/Tym.Corpus.Api` | Catalog/readiness, inference, source-grounded drafts and edited-document validation |
| `src/Tym.Corpus.Ui` | Task guidance, source/annotation review, correction provenance and export |
| `tests/Tym.Corpus.Api.Tests` | Original synthetic artifacts and source/graph contract tests; no NLP accuracy claims |

The catalog declares sixteen classifier IDs: ten original whole-text models and six additional structured Romanian TimeBank variants. Four TYM models cover English/Romanian narrative modes and segment relations. Six TimeBank targets are event class, event tense, TIMEX type, TLINK, SLINK and ALINK. Structured variants retain those targets and separate their feature channels.

## 3. Decision records

### ADR-01: C# and ML.NET own application training and inference

Use .NET 10 / Microsoft.ML 5.0.0 with distinct data, modeling, benchmark, shared input and API modules. The requested application stack remains .NET and TypeScript. The literary annotation pair is one source work; linear models provide an inexpensive reproducible baseline before a richer sequence architecture. Shared input parsing reduces training/runtime drift. Text featurization does not itself provide POS, dependencies, NER, coreference, roles or narrative perspective.

### ADR-02: Preserve immutable source and explicit offsets

Use half-open UTF-16 `[start, end)` spans in unchanged source text, source SHA-256 and `offset_encoding: utf-16`. An annotation's text must equal the source substring, and boundaries cannot split surrogate pairs. JavaScript/.NET coordinate agreement supports emoji, Romanian letters and CRLF. Feature normalization must never change source coordinates. XML offsets require reconciliation with the exact edition. An external tokenizer or Java/Python/Haskell adapter must convert its own offset convention and retain the mapping.

### ADR-03: Draft candidates and reviewed corrections retain separate evidence

The workbench drafts punctuation/newline sentence spans, lexical TIME/ACTOR/LOCATION/EVENT candidates and a segment-mode label when a model is usable. It infers no relation from adjacency. Fields retain provenance and draft/reviewed status; review never automatically means gold. These rules omit many inflections, nominal events and implicit references. Capitalization and prepositions can create false candidates. Scoring a sentence with a model trained on TYM segments introduces a unit mismatch. Review assists annotation; it does not verify TYM unity constraints.

### ADR-04: Use explicit temporal anchors and preserve unresolved values

Normalize supported valid ISO dates/years from source text. Supported deictic day terms require a supplied `document_date`; unresolved or unsupported values remain null. Server/ingestion time describes processing, not narrative chronology. Even explicitly anchored “yesterday” is a lexical hypothesis in a novel where narrator/character reference time may differ. The implementation does not resolve discourse reference time, weeks, durations, recurrent sets, uncertain intervals or event-to-date links.

### ADR-05: Preserve partial order and bound consistency claims

Check structure, source/hash/spans, endpoint kinds, inventories, unique IDs and supplied strict precedence cycles. Orient BEFORE/AFTER and immediate variants, merging supplied SIMULTANEOUS/IDENTITY for the supported contradiction check. Story output contains reviewed supplied edges/groups, with no rank for unrelated nodes.

The checker is not full Allen algebra, TimeML closure, interval satisfiability, causal inference or verification against narrative truth. It does not prove immediacy. Inclusion, overlap, boundary relations, SLINK and ALINK are retained/checked structurally without full temporal inference. The custom JSON's simplified EVENT/TIME mention layer is not a complete TimeML exporter with MAKEINSTANCE semantics.

### ADR-06: Separate directed feature channels

Structured inputs separately featurize target context/mention and, for relations, source context/mention, target context/mention and signal. Fit transforms on training folds only, omitting a wholly blank channel from training inputs. Whole-text bags can lose endpoint direction and target identity. Explicit parsing does not infer endpoints or chronology; target markers are feature cues rather than a span grounding validator. This hypothesis was added after the initial comparison and needs independent replication.

### ADR-07: Cache immutable trusted bundles and distinguish readiness

Capture weight bytes once, verify any declared SHA against those same bytes, reject incompatible declared task/language or unsupported input format, load the transformer and run a nonempty scoring probe. File presence alone is insufficient availability.

Catalog `available` now means loaded/scorable; `installed`, `loaded`, `scorable`, `integrity_status`, `identity_status` and `readiness_status` distinguish properties. Missing/malformed training metadata may permit explicitly unverified inference with absent counts/source lists. Invalid/mismatched declared digests or identity refuse inference. Successful snapshots stay cached until process replacement; failed snapshots retry after 30 seconds. No public model upload or arbitrary path is accepted. A checksum confirms bytes, not accuracy, adjudication, rights or truth of scientific metadata.

### ADR-08: Preserve legacy operational code as separate context

Keep the existing `tym-api` and its diagram as a legacy example. The archive `tym-project-updated (2).zip` contains a HybridEventExtractor switching from structured LLM output to sentence heuristics, and a YardService that sorts `TimestampStart ?? CreatedAt`, links adjacent records, infers supersession from normalized subjects and emits causal links from keywords plus a 45-day window. These are operational inbox heuristics, not evidence of literary TYM relations.

The new workbench therefore preserves unknown chronology and does not inherit ingestion-time ordering, automatic adjacent links, keyword causation or subject-based event identity. Structured generative JSON would still need exact grounding, abstention and independent evaluation.

## 4. Alternatives considered

| Alternative | Strength | Cost/limit | Recommendation |
| --- | --- | --- | --- |
| Modular .NET rules + ML.NET + TypeScript review | Shared contracts, CPU baseline, auditable evidence | Leaves discourse semantics unresolved | Adopt now and preserve as baseline |
| .NET with Python NLP sidecars | Access to specialized research components | Extra runtime/service, latency, offset conversion, licensing and version drift | Consider only for a measured missing capability; not required here |
| Contextual encoder with C# ONNX inference | Contextual features can complement explicit endpoints | Tokenizer alignment, licensed weights, windows and suitable labels still required | Next controlled representation experiment |
| Deep segmentation/relation/coreference pipeline | Contextual and sequence modeling | Richer labels, independent works, compute and propagated errors | Research roadmap with separate task gates |
| Schema-constrained LLM proposals | Flexible structured assistance | JSON validity does not prove source truth, calibrated uncertainty or stable behavior | Evaluate separately with grounding/abstention |

This is an architectural inference from the available data and scope, not an accuracy experiment comparing .NET, Python, ONNX and LLM systems.

## 5. Primary source, version and license mapping

Sources were inspected on 2026-10-02. Optional repositories are references, not vendored dependencies. Inspected branch pages are mutable; adopting an adapter requires an exact commit, model/tokenizer hashes and redistribution notices. No GPL implementation code was copied.

| Source | Version/status | License evidence | Local use |
| --- | --- | --- | --- |
| [ML.NET](https://github.com/dotnet/machinelearning) | Installed Microsoft.ML 5.0.0 | [MIT](https://github.com/dotnet/machinelearning/blob/main/LICENSE) | Training/inference dependency |
| [Official samples](https://github.com/dotnet/machinelearning-samples) | Inspected main; many examples use older APIs | [MIT](https://github.com/dotnet/machinelearning-samples/blob/main/LICENSE) | Classification, clustering and serving patterns; no TYM score claims |
| [ML.NET cookbook](https://github.com/dotnet/machinelearning/blob/main/docs/code/MlNetCookBook.md) | Inspected main | Repository MIT | Mutable engine ownership and batch considerations |
| [Microsoft.Extensions.ML serving guide](https://learn.microsoft.com/en-us/dotnet/machine-learning/how-to-guides/serve-model-web-api-ml-net) | Official guide; library not added | Framework MIT; documentation terms are separate | Pooling as a measured future option |
| [TimeML specification](https://timeml.github.io/site/publications/timeMLdocs/timeml_1.2.1.html) | 1.2.1 | Specification reference; no implementation copied | Distinct event/time/relation semantics; no complete conformance claim |
| [Tarsqi Toolkit](https://github.com/tarsqi/ttk) | [VERSION 3.0.1](https://github.com/tarsqi/ttk/blob/master/VERSION), master | Current [Apache-2.0 LICENSE](https://github.com/tarsqi/ttk/blob/master/LICENSE) | Newswire temporal component/consistency reference; not installed |
| [Legacy TTK site](https://timeml.github.io/site/tarsqi/toolkit/download.html) | Historical content, distinct artifact | Advertises CC BY-NC-SA 3.0 US | Do not transfer its license to the current repository or vice versa |
| [Duckling](https://github.com/facebook/duckling) | [cabal 0.2.0.1](https://raw.githubusercontent.com/facebook/duckling/main/duckling.cabal) | [BSD-3-Clause](https://raw.githubusercontent.com/facebook/duckling/main/LICENSE) | Optional temporal normalizer; not installed |
| [Duckling RO time rules](https://raw.githubusercontent.com/facebook/duckling/main/Duckling/Time/RO/Rules.hs) | Inspected main, explicit RO module | Duckling BSD-3-Clause | Evidence of compositional Romanian rules, no corpus accuracy guarantee |
| [ONNX Runtime C#](https://onnxruntime.ai/docs/get-started/with-csharp.html) | Official guide; no ONNX model/package added | [Runtime MIT](https://github.com/microsoft/onnxruntime/blob/main/LICENSE); weights/tokenizers separate | Future contextual feature adapter |
| [BERT](https://aclanthology.org/N19-1423/), [Longformer](https://arxiv.org/abs/2004.05150), [XLM-R](https://aclanthology.org/2020.acl-main.747/) | Original research publications | Publication and model distribution terms are separate | Contextual, long-document and multilingual research directions |

The cookbook states that PredictionEngine is not reentrant; its historical relative-cost illustration is not a benchmark on this hardware. The API shares immutable transformers and owns/disposes one engine per request batch, plus a separate readiness probe. It never registers a mutable engine as a singleton. Pooling can be measured later.

TTK documents extraction and temporal consistency for newswire, not Romanian narrative accuracy. Duckling's Romanian/reference-time support motivates an adapter with explicit date, locale, offsets and negative examples. ONNX Runtime documents C# inference and native-resource disposal; a contextual adapter additionally needs a verified tokenizer and source alignment.

## 6. Current evaluation and interpretation

The TYM pair contributes 376 English and 375 Romanian segment rows, with 382 imported relations in each language. One Romanian relation has a missing endpoint and is skipped explicitly. Both translations have one source-work group. Labels include NAR/REM/GEN/FIC, with no observed SUP examples. Doubling languages does not double independent novels. No source-disjoint TYM score is reported.

The TimeBank import yields 20,151 conditional task rows from 157 included documents of 183 archive documents. Exclusions retain reasons. The split joins documents sharing identical normalized task/language/input text into 50 components, then assigns three folds, seed 42, with 6,744 / 6,721 / 6,686 rows. The 107 cross-document duplicate rows motivate grouping.

The JSON name `independent_components` identifies components of this duplicate-input graph; it is not proof of independent semantic works. Keys hash Unicode/whitespace-normalized input text, not actual ML.NET vectors. Near duplicates, shared news events, case/diacritic/punctuation transformations and representation collisions remain threats to independence.

Each fold fits its feature transforms and classifiers on training inputs. Metrics pool out-of-fold predictions over a fixed full label inventory, retaining support and confusion matrices and giving unrecovered labels zero F1. Rare labels can be absent from training folds; macro F1 is essential beside accuracy.

| Task | Rows | Whole-text SDCA accuracy / macro F1 | Structured SDCA accuracy / macro F1 |
| --- | ---: | ---: | ---: |
| Event class | 5,947 | 67.18% / 40.83% | 72.51% / 57.24% |
| Event tense | 5,920 | 73.18% / 56.17% | 77.62% / 63.06% |
| TIMEX type | 1,144 | 86.01% / 44.01% | 91.87% / 57.23% |
| TLINK | 4,808 | 34.21% / 14.98% | 40.39% / 22.77% |
| SLINK | 2,133 | 74.50% / 34.17% | 83.22% / 63.75% |
| ALINK | 199 | 52.26% / 40.97% | 62.81% / 56.95% |

Original evidence is retained privately; sanitized aggregate copies of [the initial comparison](results/grouped-mlnet-benchmark.json) and [the structured comparison](results/grouped-mlnet-structured-benchmark.json) omit corpus prose and machine paths. The initial majority/SDCA/L-BFGS comparison fixed algorithms and iteration settings. The structured hypothesis was added after that comparison and reused its folds. These are paired retrospective diagnostics with unknown adjudication, no independent model-selection holdout and no significance analysis.

The scores assume supplied mention boundaries and relation endpoints. They do not measure span detection, TIMEX value normalization, candidate recall, end-to-end graph recovery, TYM boundaries, coreference, character timelines or full interval reasoning. TLINK remains difficult despite the directional feature gain. Raw books have no supervised labels: clustering and coverage are unlabeled exploration, and cluster IDs are not TYM labels. Prediction snapshots check behavior, not corpus accuracy. Deployed full-data weights differ from fold models; scoring their own training rows is not evaluation.

## 7. Independent backend review and fixes

The original catalog counted ZIP presence as availability and displayed unchecked manifest hashes. The service now checks captured bytes/identity, loads weights and probes nonempty scoring. It exposes distinct status fields and readiness. Missing metadata produces absent counts/source lists, not fabricated provenance. Failed snapshots retry after a bounded delay; successful snapshots remain immutable.

At the final .NET checkpoint reported by the coordinating agent, all 70 API component tests pass on .NET 10, alongside 31 modeling and 22 research checks (123 total). This reviewer directly ran the 57-test checkpoint, including thirteen new synthetic artifact tests for identity/digest refusal, corrupt installed weights, partial/full readiness, immutable caching, delayed repair/retry and concurrent document batches. Later API checks cover structured inference and readiness integration. Existing checks include invalid requests, missing/malformed metadata, source slices, explicit DCT, unrelated nodes, cycles and simultaneous contradictions. These establish fixture-specific software behavior.

Remaining interpretation limits:

- A scoring probe verifies a usable pipeline/nonempty label, not task accuracy or truth of training metadata. Hash/identity verification cannot adjudicate labels.
- Operational readiness may be true with unverified metadata; `all_integrity_verified` is a separate indicator. Successful snapshots retain the artifact state observed at loading until process replacement.
- Legacy TYM feature prefixes `Earlier segment` / `Later segment` refer to XML FROM / TO, not proved chronology. Preserve compatibility with these old weights, describe source/target endpoints clearly and rename/retrain the format later.
- One DCT does not model all narrative reference times or perspectives.
- Structural graph acceptance is not narrative truth or complete interval satisfiability. The validator has an explicit supported subset.
- Lexical mentions do not resolve actors, event instances, roles or perspective. The custom JSON export needs separate instance modeling/schema round trips for genuine TimeML interoperability.

## 8. Performance and scalability

Budgets are explicit: 50,000 UTF-16 source code units, 500 sentence candidates and 2,000 lexical mention candidates per analysis. Candidate truncation retains the full source with warnings. Validation accepts at most 10,000 annotations. Regex timeouts are per-operation safeguards, not a measured end-to-end latency guarantee.

One analysis request owns one prediction engine for up to 500 segment texts, amortizing construction over the document. Engines score sequentially within each request. Cached transformers, metadata and readiness snapshots avoid ZIP/manifest/hash work on every request. Replacement of an observed failed snapshot is atomic.

The sentence scan scales with source length; the topological cycle pass scales with graph vertices/edges. Equivalence handling uses iterative path compression, and Kahn's cycle algorithm avoids recursion on submitted graph depth. Actor overlap checks scan prior mentions and can grow quadratically with candidates; hard caps bound work but do not replace profiling.

The API performs synchronous CPU work. A document cap does not bound total concurrent CPU/memory demand. Measure cold loading, warm single inference, document batches, memory retained by sixteen transformers and P50/P95/P99 latency under realistic concurrency. Unit-test duration and training time do not establish serving throughput.

For higher load, compare request-owned engines with Microsoft.Extensions.ML pooling and IDataView batch transforms, measuring throughput/allocation before choosing. Preserve mutable-engine ownership and model-version consistency. Add deployment-appropriate ingress body/concurrency/rate budgets, and keep private prose out of request logs. Whole novels should use cancellable background jobs, document/version checkpoints and a bounded queue.

Future relation generation should avoid unrestricted quadratic pairing. Window/discourse candidate selection and cross-window links need explicit recall evaluation; dropping true pairs is not a free speed improvement. Coreference/perspective caches need document-scoped identity and revision tracking.

`/health` is process liveness. `/ready` checks the expected catalog's operational state and returns 200 when every expected model is ready, otherwise 503. The first readiness/catalog call performs cached loading/probes. Readiness and artifact integrity remain separate from scientific validity.

## 9. Evaluation roadmap and acceptance criteria

1. **Audit/adjudicate data.** Reconcile XML spans against the exact edition; retain language exclusions and ambiguous/missing endpoint records; publish label definitions, including rare narrative modes. Double-annotate a stratified sample, measure task-specific agreement, resolve disagreements and preserve original/adjudicated records. Reviewed exports stay outside human gold until this process is complete.

2. **Freeze source-work partitions.** Keep translations, editions, chapters, derived events, paraphrases and synthetic descendants with their source work. Add independently annotated works for a final literary test, with a separate development partition for choices/thresholds. Audit exact/near duplicates and shared news events. Current folds remain a diagnostic record.

3. **Replicate representation comparisons.** Compare majority, whole-text SDCA, structured SDCA and contextual features on identical frozen partitions. Report pooled/per-document accuracy and macro F1, class counts and document/component bootstrap intervals. Use independent works/documents for paired analysis, not correlated events as independent observations. Inspect swapped endpoint direction and target-context ablations.

4. **Measure extraction.** For EVENT/TIME/ACTOR/LOCATION report strict labeled span precision/recall/F1, a separately defined overlap metric, TIMEX type/value accuracy and unresolved-anchor rates. Cover both languages, diacritics, nominal events, quotation, coordination and long references. Evaluate candidates instead of supplying correct endpoints to every model.

5. **Measure literary structure.** Evaluate TYM boundaries independently from narrative modes, using exact boundary F1 and an agreed segmentation metric. Measure coreference, actor attribution, perspective, explicit anchoring and direction. Distinguish oracle-endpoint classification from end-to-end relation F1/candidate recall. Graph metrics need a documented equivalence/closure policy.

6. **Evaluate reasoning separately.** Add an interval representation and independent solver only when required. Define endpoint uncertainty, equality, immediacy and supported algebra; test inclusion/boundary contradictions beyond precedence cycles. Do not describe the current checker as full Allen reasoning.

7. **Prototype contextual C# inference.** Select licensed English/Romanian or multilingual weights with documented limits. Pin model/tokenizer, verify UTF-16 alignment, test windows/long-context aggregation and combine embeddings with explicit symbolic channels. Pretraining does not supply missing gold spans, event identity or temporal labels.

8. **Measure annotation assistance.** Compare correction burden, time/document and independently adjudicated quality for assisted versus manual annotation. If uncertainty sampling is added, calibrate scores and sample diverse/rare cases. Actively queried development cases cannot become an untouched final test.

The scientific promotion gate is reproducible improvement on frozen independent adjudicated works, with source grounding and task-specific errors. Operational readiness, passing component tests and a publishable container each have their own evidence and do not substitute for that gate.
