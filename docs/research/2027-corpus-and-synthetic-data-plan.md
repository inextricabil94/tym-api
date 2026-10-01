# TYM corpus and synthetic-data plan for the 2027 NLP venues

**Status:** research proposal, not implemented or evaluated. This plan was reviewed against the official 2027 conference calls and corpus papers on 1 October 2026.

## Research question

Can unlabeled, genre-matched narratives help researchers find under-covered temporal-semantic phenomena, and can human-audited synthetic descendants improve TYM models without reducing the coverage of human language?

The contribution is a corpus and controlled training-data study. ML.NET is the reproducible baseline and the tool for the current lexical clustering and classifiers; its use is not itself the research claim. The checked-in 67 seed rows are synthetic demos, not corpus data or gold labels. The repository currently has no adjudicated TYM corpus, so it cannot support an accuracy or generalization claim.

## Corpus architecture

Keep three resource roles separate. Do not relabel every available temporal corpus as TYM.

### 1. Target-domain TYM gold

Build or obtain a rights-cleared collection of **human-authored original narratives** in explicitly selected genres and languages. A useful starting matrix is genre × language × source/work, with samples from each cell rather than many excerpts from a few works. Record source, author/work, language, genre, publication period, license, and collection method.

Annotate the TYM target distinctions from the project guideline: event spans, segment or narrative-frame labels, temporal anchors, and temporal relations. Use independent annotations on an agreed subset, retain pre-adjudication labels and uncertainty, adjudicate disagreements, and version the guideline. A second-language extension requires competent annotators and a language-specific guideline; translated English labels do not by themselves establish native-language gold.

### 2. Public comparator corpora

Use public corpora to evaluate compatible tasks or stress-test transfer. Before use or redistribution, verify both the annotation license and the underlying source-text terms.

| Resource | Useful signal | Limit for TYM |
|---|---|---|
| [NarrativeTime / TimeBankNT](https://aclanthology.org/2024.lrec-main.1054/) | Dense timeline annotations, two independent expert annotations, and a conversion path to TimeML. | Re-annotated TimeBank-Dense news; it does not provide TYM narrative-frame labels or a general fiction corpus. |
| [TIMELINE](https://aclanthology.org/2023.emnlp-main.1016/) | Explicit long-distance relations and nominalized events, with documented annotation criteria. | News domain and a temporal-relation task; map only labels that have a defensible correspondence. |
| [CaTeRS / ROCStories](https://aclanthology.org/W16-1007/) | 320 five-sentence stories annotated for temporal and causal event relations; a short-story diagnostic. | Small and schema-specific; it is not an annotated TYM training set. |
| [MEANTIME](https://aclanthology.org/L16-1699/) | English, Spanish, Italian, and Dutch translations of Wikinews, with event/time annotations. | 120 English articles and translations; some target-language annotations were projected from English. Treat those as transfer candidates until audited, not as independent target-language gold. |

Create a schema crosswalk for each comparator: compatible labels, labels absent from the source, scope differences, and conversion uncertainty. Keep crosswalked results separate from native TYM gold scores.

### 3. Unlabeled discovery pool

Collect a separate rights-cleared pool that resembles the target genres and languages. Preserve document/work boundaries and metadata. The current ML.NET `FeaturizeText` + K-Means pipeline can expose lexical strata, exemplars, and outliers; cluster IDs are **sampling hints**, never semantic labels. Compare random annotation sampling with cluster-stratified sampling under the same labeling budget. Track whether each policy discovers additional human-confirmed phenomena and how much annotation effort it takes.

If n-gram clusters do not capture the desired semantics, evaluate an embedding representation as a distinct ablation (for example, a frozen encoder exported to ONNX feeding a downstream ML.NET clustering step). Do not describe the existing K-Means output as contextual-semantic clustering.

## Training-data construction

1. **Split first.** Partition human gold by complete source work/document, and where possible by author or collection source. Make train, development, and test manifests before generating any descendants. Keep test and development human-authored and adjudicated.
2. **Discover gaps.** Fit the unsupervised sampler on the training-side unlabeled pool. Have annotators inspect representative, rare, and outlier items. Compare cluster-guided selections against random selections at the same annotation budget.
3. **Generate from train parents only.** Create minimal pairs and controlled transformations for temporal frame, tense/aspect, explicit and implicit anchors, nominalized events, long-distance relations, genre, and ambiguous cues. Change one phenomenon at a time where possible, then vary syntax, lexical choice, cue position, event names, and context independently.
4. **Audit and preserve lineage.** Record parent/work ID, phenomenon, transformation, generator and prompt version, language, intended label, review status, and reviewer. Deduplicate descendants against all splits. Human-audit a stratified sample from every generator × phenomenon slice before admitting that slice to training; exclude disputed cases or represent their uncertainty explicitly.
5. **Keep pseudo-labeling separate.** Unlabeled cluster assignments or model predictions are not gold labels. If pseudo-labeling is later studied, compare it as its own noisy-label condition and report confidence filtering and human-audit error.

### User-supplied TYM annotations and Ro-TimeBank

The supplied Romanian and English `<TAGS>` files are near-parallel annotations of one story (375/376 segments, 375 shared spans), with temporal relations between TYM segments. They are useful for validating the importer and for train-only pilot models, but their shared narrative forbids a random train/test split and they do not provide an independent held-out work. The adapter marks these records `provided_annotation/adjudication_unknown`; the evaluation command continues to accept only independently adjudicated `human_gold` records. Details, mappings, and the Ro-TimeBank separation are in [the annotation pilot note](ro-timebank-and-annotation-pilot.md).

Ro-TimeBank adds separate ISO-TimeML tasks over translated English news: event, time-expression, signal, and temporal-link annotations. Its 183 documents and 181 aligned pairs are valuable for Romanian temporal IE transfer experiments after a schema audit. They do not supply TYM narrative-frame labels. The reported 99.18% transfer figure is annotation transfer/coverage after corrections, not model accuracy. The archive terms restrict use and distribution, so corpus text and models trained from it stay outside public commits unless rights are cleared.

ML.NET's TorchSharp integration offers a pretrained NAS-BERT/RoBERTa text-classification trainer, but the local attempt failed during checkpoint loading (`Mismatched state_dict sizes: expected 60, but found 142 entries`), matching an open ML.NET NAS-BERT trainer issue. That optional dependency was removed rather than leaving a broken path. The implemented SDCA text baseline is trainable; a multilingual encoder such as XLM-R plus a document-level graph model is the research SOTA direction, and span extraction/graph evaluation still need implementation. No current model should be called state of the art from one annotated story.

### Controlled comparisons

Use the same human-gold evaluation set and split manifest for every condition:

- human-only;
- human plus deterministic/template contrast examples;
- human plus diversified model-generated examples that passed audit;
- cluster-guided human annotation versus random human annotation at equal budgets;
- where data volume allows, cross source/genre/language and leave-one-generator-family-out evaluation.

Test synthetic-to-human ratios such as 0, 0.25, 0.5, and 1.0, but include a human-only quantity control (for example, resampling or a matched additional human annotation budget). This distinguishes synthetic-data value from a simple increase in row count. Repeat runs with fixed seeds, report variance, and publish neutral or negative results.

The current ML.NET CLI can report grouped classification metrics and clustering diagnostics. A paper-quality TYM benchmark also needs span-boundary precision/recall/F1, temporal-link scores, graph consistency, annotator agreement, calibration, and per-slice errors. Do not claim those metrics until implemented and measured.

## Conference-specific data questions

These are alternative scholarly framings of one research program. A venue change should correspond to a different corpus slice, training comparison, or analysis—not just a renamed paper.

| Venue and published lens | Data/training emphasis | Evidence needed |
|---|---|---|
| **EACL — “The Human in Language”** ([CFP](https://2027.eacl.org/calls/papers/)) | Linguistically motivated TYM guidelines, double-annotated narrative gold, disagreement/uncertainty, and human-reviewed minimal pairs. | Agreement before adjudication, disagreement analysis, human-grounded error slices, and evidence that the annotation decisions reflect temporal/discourse distinctions. The main ARR deadline (3 August 2026) has passed; a new main-track submission is not available in this cycle. |
| **COLING — “NLP for Linguistics”** ([CFP](https://2027.coling-iccl.org/calls/main_conference_papers/)) | A genuinely multilingual and linguistically interpretable corpus extension: original texts across chosen languages, comparable genres, language-specific cue inventories, and audited cross-lingual diagnostics. | Analyze variation and label portability rather than assuming translation equivalence. Choose a second language only when rights, language expertise, and annotation capacity exist. MEANTIME is a transfer diagnostic, not independent target-language narrative gold. |
| **NAACL — broad track; agentic communication is a separate theme** ([CFP](https://2027.naacl.org/calls/main_conference_papers/)) | Genre/source transfer, low-resource evaluation, or a fixed-budget active annotation study using the unlabeled pool. | Group-held-out human gold, per-phenomenon performance, and fair random-versus-cluster-guided annotation comparisons. The agentic theme would require a real communication/common-ground corpus task; using multiple agents alone is explicitly insufficient. |
| **ACL — “Homogenization and knowledge collapse in LLMs”** ([CFP](https://2027.aclweb.org/calls/main/)) | Matched human-authored narratives and outputs from multiple generator families; annotate TYM phenomena and rare-case coverage; compare synthetic training mixes. | Directly measure semantic as well as lexical diversity, phenomenon coverage/entropy, and generator-held-out performance. Claim theme relevance only if homogenization is an actual research question and result. |
| **EMNLP — 2027 CFP/theme not yet published** | A controlled empirical matrix across corpus source/domain, generation strategy, generator family, and synthetic ratio. | Repeated runs, source/genre/generator holdouts, slice-level errors, reproducible lineage, and negative results. Re-check the CFP before selecting a track. |

## Practical submission gates (checked 1 October 2026)

- **EACL:** its main ARR submission date, 3 August 2026, has passed; only already reviewed work can still be committed through the current cycle.
- **COLING and NAACL:** share an ARR submission deadline of 12 October 2026. A new collection and annotation program cannot credibly be rushed into this deadline; use the cycle only if suitable data and pilot evidence already exist. The reviewed article can be committed to only one primary venue.
- **ACL:** ARR deadline is 4 January 2027, leaving a more realistic window for a bounded corpus pilot and controlled analysis.
- **EMNLP:** 2027 dates and its paper CFP/theme are not yet published.

Conference dates identify audiences and submission windows; they are not training examples, synthetic data, or corpus dimensions. Recheck official CFPs before a submission decision.

## Selected sources

- NarrativeTime, [paper and annotation resources](https://aclanthology.org/2024.lrec-main.1054/)
- TIMELINE, [paper](https://aclanthology.org/2023.emnlp-main.1016/)
- CaTeRS, [paper](https://aclanthology.org/W16-1007/)
- MEANTIME, [paper](https://aclanthology.org/L16-1699/)
- EACL 2027 [main CFP](https://2027.eacl.org/calls/papers/)
- COLING 2027 [main CFP](https://2027.coling-iccl.org/calls/main_conference_papers/)
- NAACL 2027 [main CFP](https://2027.naacl.org/calls/main_conference_papers/)
- ACL 2027 [main CFP](https://2027.aclweb.org/calls/main/)
