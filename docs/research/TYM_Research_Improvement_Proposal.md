# TYM: Research and Improvement Proposal

Evidence audit, non-LLM NLP architecture and evaluation programme

19 October 2026 | Research planning document | Version 1.0

## 1. Research Direction

**Recommendation.** Develop TYM as an evidence-linked narrative-time graph, with diagrams as projections of that graph. Prioritise defensible annotations and uncertainty over larger models or more diagram elements. A working renderer demonstrates software capability, not the correctness of its linguistic interpretation.

The starting points are Cristea and Macovei's time-yard model [1] and the supplied manuscript, *Deep Learning for Spatial and Temporal Semantics in Natural Language Processing*, by Boghiu Serban Paul [2]. The implementation audit refers to repository revision `1564056`; the account/reader increment is identified separately. This is a proposal, not a new peer-reviewed experiment or an accuracy certification.

### What Already Works

The .NET API generates events, temporal segments, tracks, TimeML-style annotations and SVG diagrams using rules, ML.NET seed classifiers and model-backed POS filtering. English and Romanian are supported. The React workbench supports chapter/section selection, entity highlighting, grouped/paged tracks and viewport-aware density choices.

The selected density is returned first. The remaining presets, 10 through 100 in steps of 10, are requested in the background from prepared analysis; they do not repeat NLP or consume additional daily diagram permits. Complete books can be analysed without the former 100,000-character-per-section restriction. These are implemented features, not future research results.

| Evidence | Meaning and limitation |
| --- | --- |
| 16 source files; 468 reading sections | Cleaned library contains 9,460,174 UTF-16 code units. Files are not independent works. |
| 15 Romanian files; 1 English file | The English material represents one work; cross-work English generalisation is untested. |
| Existing automated checks | Validate offsets, layout/cache behaviour, library integrity and authored linguistic fixtures; not gold-corpus accuracy. |
| New account/reader increment | Separates guest analysis from authenticated source reading; real Google and SMTP configuration remains an operational prerequisite. |

<!-- pagebreak -->

## 2. Aligning the Implementation With the Papers

In the time-yard account [1], tracks are not simply character lanes. Narrative continuity, interruption, split, join and restoration matter; a character's trajectory may cross tracks. Text order and story-time organisation are distinct. A diagram should not imply measured elapsed duration when the evidence only establishes order.

The supplied manuscript [2] defines TS through unity of time, characters and perspective, then builds TT using segments, selected characters and story ordering. Past/present/future classification is therefore a useful feature or auxiliary task, not a complete operational definition of a temporal segment.

| Research concept | Proposed implementation contract |
| --- | --- |
| Temporal segment | Evidence-backed span satisfying documented time, participant and perspective criteria; allow an uncertain boundary. |
| Time track | Canonical continuity relation among segments, with explicit join/split/interrupt/restore operations. |
| Diagram density | Display aggregation only; preserve canonical events, TS IDs, source offsets and temporal relations. |
| Unreported interval | A gap in narrated continuity, distinct from recollection, supposition, generality or fiction labels. |

The current dotted-line convention for narrative modes should not silently replace the paper's discontinuity convention. Give gaps and modes different visual channels and legends. Store the semantic reason behind each drawn line.

### Recomputing the Manuscript's Reported Matrix

Table 4 of the supplied manuscript gives the following event-tense counts. Rows are actual classes; columns are predicted classes. The matrix totals **2,000 events**, not 2,000 independent novels or TS boundaries.

| Actual / Predicted | Past | Present | Future |
| --- | --- | --- | --- |
| Past | 476 | 287 | 98 |
| Present | 143 | 439 | 113 |
| Future | 67 | 114 | 263 |

Recomputed accuracy is **58.90%** (1,178/2,000); the majority-class baseline is **43.05%** (861/2,000). Macro-F1 is **58.68%**. These numbers describe that reported event-tense matrix only. They are not current API accuracy, TS-boundary F1, TT correctness or temporal-relation performance.

The manuscript also reports 376 TS and 382 TS relations. Its event-level 80/20 split needs a published grouping manifest: events from the same segment, novel or translation can otherwise cross splits. This is a leakage risk to audit, not evidence that leakage necessarily occurred. Clarify supervised versus unsupervised experiments, feature availability at inference time, and what the reported 93 extracted segments were compared against.

<!-- pagebreak -->

## 3. A Reproducible Non-LLM NLP Pipeline

**Proposed processing sequence:** immutable source and offset map; linguistic analysis; canonical entities and roles; events and time expressions; TS boundaries; perspective and continuity; directed temporal graph; display projection. Each stage should be independently replaceable and evaluated.

### Linguistic Analysis and Entity Identity

The current spaCy worker excludes NER, dependency parsing and lemmatisation. Its trained contribution is POS filtering; enabling a full NER/parser pipeline would be a new change. Evaluate a complete spaCy adapter [9] and a Stanza adapter [5] using identical inputs and task coverage. Verify actual language/model availability before calling either a Romanian literary NER solution.

RONEC [3] provides Romanian NER resources and UD Romanian RRT [4] provides morphology/dependency data. Their domains differ from the novels here. Use them for training or adaptation with licensing checks, then evaluate literary dialogue, historical names and OCR artefacts separately. Do not treat benchmark news accuracy as literary-domain accuracy.

Keep a canonical entity registry with mention spans, aliases, type, grammatical evidence and provenance. Pronouns are mentions of an entity only when resolution is sufficiently supported. Keep unresolved or ambiguous references separate rather than assigning every pronoun to the closest proper noun. Speaker and quotation context are essential for first-person and conversational references.

Romanian evaluation should include clitics, omitted subjects, gender/number agreement, compound verb forms, diacritics and spelling variation. POS selection must reject irrelevant standalone verbs, adverbs, conjunctions and interjections without deleting internal function words from a valid multiword name. Distinguish entity extraction from the narrower set offered in the Highlight entity menu.

### Events, Roles and Segment Boundaries

Represent event mentions separately from event instances. Record predicate, participants/roles, tense/aspect, polarity, modality, narrator/perspective and time anchors. A tense change is neither necessary nor sufficient for a TS boundary. Introduce a trainable boundary classifier only after the annotation rules and span policy are stable.

Use a transparent ML.NET baseline with lexical, POS, morphology, participant-change, temporal-cue and perspective features. Compare it with a sequence model such as CRF or BiLSTM only if labelled data supports that complexity. Fit TF-IDF, vocabulary, feature selection and calibration on training data only. Any similarity threshold, including the manuscript's 0.7 suggestion, must be validated rather than treated as universal.

No generative LLM is required for this programme. A pretrained transformer encoder is an optional, separately reported model family, not automatically a non-language-model method. Do not introduce one merely to avoid evaluating the simpler baseline.

<!-- pagebreak -->

## 4. Temporal Semantics and Canonical Graphs

TimeML [6] offers a useful separation of event mentions, instances, temporal expressions, signals and links. Adopt an explicit schema and validate it; producing XML with familiar element names is not sufficient to claim conformance.

| Layer | Required improvement |
| --- | --- |
| EVENT / MAKEINSTANCE | Distinguish lexical mention from tense/aspect/polarity-bearing occurrence; validate instance references. |
| TIMEX3 / SIGNAL | Separate DATE, TIME, DURATION and SET; preserve unresolved values and supporting cues. |
| TLINK | Store ordered source/target IDs, relation, evidence span, method/version and uncertainty. |
| SLINK / ALINK | Model subordination/factuality and aspectual relations separately from temporal order. |
| TYM structure | Record perspective, continuity gaps and typed track operations without overloading TimeML links. |

A fictional narrative's “now” should derive from its narrative reference context. Never normalise “tomorrow” using the Azure deployment date or wall-clock request date unless the document explicitly supplies that reference. Keep normalised values unresolved where the anchor is missing.

SUTime [7] is a candidate English normalisation baseline. HeidelTime [8] is another candidate framework; available Romanian resources and literary performance must be verified before implementation claims. Evaluate span detection, expression type and normalised value separately. A recognised date string is not proof that its normalisation is correct.

### Replace Unsupported Temporal Certainty

The audited code includes adjacency defaults to BEFORE, IMMEDIATELY_BEFORE or IBEFORE. Successive sentences may describe overlapping, remembered, hypothetical or unordered events. Prefer `UNKNOWN` or an explicitly typed narrative-adjacency edge unless evidence supports temporal order. Do not export a display connection as a strong temporal assertion.

Maintain a partial-order graph, not a compulsory total chronology. Check the known BEFORE subset for cycles, reject dangling endpoints and retain contradictory evidence for review. Full interval reasoning requires a documented relation-composition algebra; a DAG check alone does not establish interval consistency. MATRES [10] can inform event start-point annotation but is not interchangeable with full interval or TYM continuity annotation.

For the authored example “Ana arrived. Earlier, Stefan had left. They might meet tomorrow,” distinguish textual order from the earlier departure, uncertain future meeting and unresolved narrative date. Keep this example as an adversarial fixture, not a training/test performance estimate.

Finally, vary density and viewport only after inference. A display merge should reference its underlying TS IDs. Canonical event spans, identities, boundary decisions and graph links must remain invariant across every 10-100 density preset.

<!-- pagebreak -->

## 5. Corpus, Annotation and Evaluation Design

The 16 files represent **11 work groups**, not 16 independent literary works. Group both Shogun volumes together, both Tinutul volumes together, both Morometii volumes together, and the English/translated/PDF Tash Aw variants together. Keep these groups intact across training, development and test. Near-duplicate passages and editions need the same treatment.

Because only one English work is currently available, an English/Romanian comparison on that material is within-work translation transfer. Acquire additional independently licensed English works before making cross-work English generalisation claims. Keep raw copyrighted prose out of public benchmark repositories; publish annotation formats, code and lawful redistribution arrangements instead.

### Proposed Annotation Programme

Start with 12 scenes from six work groups, independently annotated twice, then adjudicate disagreements. Use the pilot to refine TS boundary, participant, perspective and temporal-link definitions. A possible main study is 120 scenes, approximately 200-400 words each, distributed across those groups. These are proposed sample sizes, not completed annotations.

Freeze the work-held-out protocol before feature engineering. Report train/development/test group IDs, language and genre coverage, scene-selection rules and duplicate detection. If data is small, use grouped cross-validation and clearly distinguish exploratory tuning from a final locked test. Any augmentation or vocabulary fitting must use training data only.

| Task | Primary reporting requirements |
| --- | --- |
| NER and event spans | Exact-span precision/recall/F1; relaxed overlap results separately; per-language/type errors. |
| TS boundaries and modes | Boundary F1 with an explicit matching policy; segmentation error analysis; mode macro-F1 and class support. |
| Temporal relations | Directed labelled F1; candidate-pair recall; conditional gold-endpoint and end-to-end predicted-endpoint results separately. |
| TIMEX and coreference | TIMEX span/type/value scores separately; coreference cluster metrics only where gold clusters exist. |
| TT graph | Join/split/continuity-operation F1, endpoint validity, cycle/contradiction rates and abstention coverage. |

Measure inter-annotator agreement before adjudication and publish disagreement categories. An adjudicated set is not itself evidence that the original labels were reproducible. Evaluate perspective and uncertain links explicitly; otherwise apparent boundary disagreement may actually be a definition problem.

Report confidence intervals by resampling independent documents or work groups rather than treating all events as independent. With few work groups, uncertainty will remain large. Classifier scores should not be presented as probabilities until calibration is evaluated on held-out material. Report coverage alongside selective accuracy whenever the system abstains.

<!-- pagebreak -->

## 6. Experiments and Honest Statistics

### Preregistered Comparisons

| Experiment | Comparison and decision question |
| --- | --- |
| E1: TS boundaries | Tense-only baseline versus time cues + participants + perspective. Do richer features improve held-out boundary F1? |
| E2: Romanian entities | POS-only candidates versus full NER, morphology and conservative coreference. Which errors improve without increasing false identity merges? |
| E3: Relations | Forced adjacency versus UNKNOWN-aware local links versus constrained graph inference. Does directed link quality improve at acceptable coverage? |
| E4: Whole books | Monolithic processing versus stateful chunking and paged layouts. Are semantic outputs equivalent while peak memory and cancellation improve? |

Run ablations using identical work splits and candidate sets. Record model/data hashes, package versions, random seeds and hardware. Treat a practical improvement threshold, for example three absolute F1 points, as a decision criterion chosen in advance, not as an achieved or promised result. Publish negative results and error distributions.

### Existing Engineering Measurements

A recorded local full-book run on cleaned Morometii volume 1 processed **786,740 UTF-16 code units**, producing **7,712 events** and **6,057 segments**. Selected density 40 returned one initial layout plus nine pending presets; a density-100 background layout contained 6,057 tracks. Request plus JSON decoding took **7.03 seconds in one local run**. This is an engineering observation, not extraction accuracy or a latency percentile.

An earlier synchronous-cache run took **57.81 seconds** and accumulated about **114.83 million SVG characters** and **156.28 million XML characters** across its cached views. Settings and export behaviour differed, so these observations are not a controlled speedup experiment. They motivate a matched benchmark, not a causal performance claim.

For performance evaluation, separate cold start, model load, analysis, layout, transfer, parsing and first meaningful display. Report p50/p95 over repeated runs, peak process RSS, output bytes, concurrency and cancellation latency. Use the same input, language, density, hardware and warm/cold policy for each comparison. Output counts and counts-per-second cannot substitute for accuracy.

Existing repository checks include hundreds of density fixtures and authored POS/anaphora cases. Their role is regression prevention. A large pass count cannot be converted into a percentage of correct literary interpretations. Likewise, framework proof-of-concept extraction counts do not establish a research-grade ranking without matched task definitions and a common gold set.

<!-- pagebreak -->

## 7. Serving, Reading and Diagram Improvements

### Density and Whole-Book Execution

Keep the implemented selected-density-first behaviour. Next, make layouts paged and byte-bounded, share immutable descriptors and avoid retaining ten enormous SVG/XML strings. The current session cache uses source-character weights with a four-million-unit limit; ASP.NET MemoryCache units are application-defined [11], so that limit is not a four-megabyte heap guarantee.

Add cancellable jobs with stable IDs, idempotency keys, progress milestones and bounded admission. Checkpoint chapter processing without breaking cross-chapter entity identity or narrative anchors. Include source hash, cleaning version, model versions, options and schema version in semantic-cache keys; add viewport dimensions only to layout-cache keys. A cancelled or expired job must not overwrite a newer selection.

In-memory density sessions and daily quotas currently assume a single API replica. Before horizontal scaling, use atomic shared quota state and shared job/session storage, or explicitly document affinity and its failure cases. CPU, model memory and whole-book output bytes need joint load tests, not independent optimistic limits.

### Guest and Signed-In Access

The requested account increment uses ASP.NET Core Identity email registration and direct Google Identity Services, not Firebase. Guests should see metadata and permitted analysis, but not source text. Confirmed signed-in users may read the chosen chapter/section or full selection. Keep authorisation on the API endpoint, not merely on a hidden frontend panel.

Require email confirmation, recovery, failed-login protection, verified Google ID tokens, short access lifetimes and revocation checks on source reads. Google registration needs the real OAuth client ID and approved origins; email confirmation needs an actual mail sender. Persistent private account storage and Data Protection keys are production prerequisites. Local mail-pickup tests are not evidence of real inbox delivery or a live Google login [12,13].

Do not add source download/copy buttons. Nevertheless, visible browser text can be copied: authentication is access control, not DRM. Public analysis can expose excerpts, and library metadata is not private merely because raw source endpoints require login. Review redistribution permissions, retention, account deletion and content-use policy before wider release.

### Readable Diagrams and Labels

Display chapter names as Arabic-number labels such as “Capitolul 1,” preserving source headings and offsets internally. Provide uncertainty/provenance inspection, legible legends and keyboard-accessible track navigation. Use different symbols for continuity gaps, hypothetical modes and temporal links. Collision tests must cover long labels, dense graphs, high zoom and narrow screens. Resolution changes should reproject layout, not change the inferred story.

<!-- pagebreak -->

## 8. Roadmap and Acceptance Gates

The following is an indicative 12-week sequence, not a delivery commitment. Work on the diagram API and the separately documented corpus-explorer prototype should share schemas, annotation formats and model/version contracts. The corpus explorer's current source was not independently audited for this document; do not treat its older plans as verified implementation status.

| Phase | Deliverable and acceptance gate |
| --- | --- |
| Weeks 1-2: Definitions | TS/TT annotation guide, typed graph schema, reversible offset map and double-annotated pilot. Every stored span slices the declared source exactly. |
| Weeks 3-5: Baselines | Work-group split manifest, ML.NET boundary baseline and full linguistic adapters. Reproduce per-language task metrics without test-set fitting. |
| Weeks 6-8: Semantics | UNKNOWN-aware directed links, continuity operations and review workflow. No orphan IDs; known BEFORE subgraph acyclic; conflicts surfaced. |
| Weeks 9-12: Scale and report | Paged jobs/layouts, cancellation/load tests and locked evaluation. Report quality, memory and latency with uncertainty and reproducible configuration. |

### Release Gates

- Original and cleaned sources have explicit hashes and a reversible offset mapping. Normalisation never silently changes evidence coordinates.
- Every event, TS and link has method/model provenance, supporting spans and a reviewable uncertainty state.
- Canonical TS IDs, event spans and graph links are invariant across density presets and supported window sizes.
- Guest/forged/expired/revoked sessions receive no raw source. Sign-out and source changes clear the reader. Real Google and mail-provider tests precede production activation.
- Retry, cancellation, stale-response, quota and cache-expiry cases are automated. A cancelled generation cannot consume unbounded background CPU.
- Public artifacts contain code, lawful fixtures, schemas and evaluation manifests, not restricted novels, credentials or private account data.

### Manuscript Revision Checklist

Clarify which experiments are supervised, which are exploratory and which labels are targets. Report the Table 4 values as event-tense performance with class supports and an explicit baseline. Publish train/test grouping and inference-time feature availability. Replace broad “automatic TS extraction” conclusions with task-specific boundary, mode, relation and graph evaluations.

Separate tense, narrative mode, factuality, perspective and track continuity throughout the terminology. Explain whether an edge means textual adjacency, temporal order or visual routing. Add uncertainty and negative examples, including dialogue, flashbacks and non-chronological narration.

<!-- pagebreak -->

## 9. References and Audit Trail

[1] Dan Cristea and Andreea Macovei. *Why Time Resembles Rail Yards? A Way to Look at Time in Text Books.* Supplied author PDF; bibliographic metadata needs publisher reconciliation. [Author copy](https://profs.info.uaic.ro/dan.cristea/papers/CristeaMacovei-ROMJIST2017.pdf).

Bibliographic audit: the filename says ROMJIST2017, but the PDF header gives volume 19, numbers 1-2, 2016, pages 1-13. Verify the final publisher record before publication; do not invent year/pagination.

[2] Boghiu Serban Paul. *Deep Learning for Spatial and Temporal Semantics in Natural Language Processing.* Supplied 14-page manuscript, file suffix “(3).pdf”. Publication status/year not established. Pages 2-3: TS/TT proposal; pages 11-12: features and reported experiment; Table 4: matrix recomputed here. No full article or novel text is reproduced.

[3] Stefan Dumitrescu and Andrei Avram. 2020. *Introducing RONEC - the Romanian Named Entity Corpus.* [LREC primary paper](https://aclanthology.org/2020.lrec-1.546/).

[4] Universal Dependencies. *Romanian RRT treebank.* [Official dataset documentation](https://universaldependencies.org/treebanks/ro_rrt/index.html).

[5] Peng Qi, Yuhao Zhang, Yuhui Zhang, Jason Bolton and Christopher D. Manning. 2020. *Stanza: A Python Natural Language Processing Toolkit for Many Human Languages.* [ACL system paper](https://aclanthology.org/2020.acl-demos.14/).

[6] TimeML. *TimeML Annotation Guidelines, version 1.2.1.* [Specification](https://timeml.github.io/site/publications/timeMLdocs/timeml_1.2.1.html).

[7] Angel X. Chang and Christopher D. Manning. 2012. *SUTime: A Library for Recognizing and Normalizing Time Expressions.* [LREC paper](https://aclanthology.org/L12-1122/).

[8] Jannik Strotgen and Michael Gertz. 2010. *HeidelTime: High Quality Rule-Based Extraction and Normalization of Temporal Expressions.* [SemEval paper](https://aclanthology.org/S10-1071/).

[9] Explosion. *spaCy Models and Trained Pipelines.* [Official model documentation](https://spacy.io/models).

[10] Qiang Ning, Hao Wu and Dan Roth. 2018. *A Multi-Axis Annotation Scheme for Event Temporal Relations.* [ACL paper](https://aclanthology.org/P18-1122/).

[11] Microsoft. *Cache in-memory in ASP.NET Core.* [Official guidance](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/memory?view=aspnetcore-9.0).

[12] Microsoft. *Use Identity to secure a Web API backend for SPAs.* [Official guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-9.0).

[13] Google. *Verify the Google ID token on your server side.* [Official guidance](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token).

### Local Audit Artifacts

Implementation: `Program.cs`, `BookLibrary.cs`, `DensitySessions.cs`, `TrackGeneration.cs`, `AnaphoraResolution.cs`, `PosTagging.cs`, `tools/tym_pos_worker.py`, frontend density/viewport modules and the new `Accounts.cs` increment. Planning context: `docs/plans/diagram-api.md` and `docs/plans/corpus-explorer.md`. Measurement records: `build_verify/full-book-morometii-1.json` and recorded progressive full-book smoke run. Full texts and account-test artifacts remain outside public source assets.

External references were consulted for this proposal; current model/language support and service configuration should be rechecked at implementation time. All new experiments, sample sizes and roadmap dates above are proposals unless explicitly labelled as recorded observations.
