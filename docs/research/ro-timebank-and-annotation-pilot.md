# Romanian temporal corpora and supplied TYM annotation pilot

**Status:** a train-only data adapter and exploratory model fit. This is not a benchmark, model comparison, or state-of-the-art result.

## What was inspected

The provided `hln_varianta_romana.xml` and `motiw_varianta_engleza.xml` files use the project's custom `<TAGS>` schema. Each includes actor and location inventories, `TS` narrative segments with `TYPE`, `PER`, `ACTORS`, `LOCATION`, `NAME`, `TEXT`, and `SPANS`, and `TREL` segment-to-segment temporal relations with `REL` and `TRIGGER`.

They are parallel annotated versions of one story, not two independent documents. The two files contain 375 Romanian and 376 English segments, with 375 identical segment span intervals. Each has 382 temporal relation tags. The importer keeps all 382 English relations and 382 Romanian relations after dropping one Romanian relation whose endpoint references a missing segment. It normalizes one observed source typo, `SIMMULTANEOUS`, to `SIMULTANEOUS`. A source-level audit found that segment type labels agree for every shared span. No cross-language relation trigger strings are treated as equivalent features.

The current label crosswalk for `TS TYPE` is:

| XML value | Project label | Count, Romanian | Count, English |
|---|---:|---:|---:|
| Narration | NAR | 313 | 314 |
| Remembers | REM | 33 | 33 |
| Fiction | FIC | 25 | 25 |
| GeneralKnowledge | GEN | 4 | 4 |
| Supporting | SUP | 0 | 0 |

The relation crosswalk preserves `BEFORE`, `AFTER`, `IMMEDIATELY_BEFORE`, `IMMEDIATELY_AFTER`, and `SIMULTANEOUS`. These are relations between TYM segments in the supplied files. They are not automatically interchangeable with TimeML event-event `TLINK` labels.

## Provenance and limits

The XMLs were supplied as annotated project files, but their adjudication status and publication terms are unknown. The importer therefore marks every row `source_type=provided_annotation` and `review_status=adjudication_unknown`. The CLI accepts those rows for training only. Its evaluator still accepts only independent `human_gold/adjudicated` records. The files are not copied into the Git repository.

The two languages must always remain in the same document/work fold. A random segment-level split would put near-identical translation segments and relation pairs on both sides of the test boundary. With one story and highly imbalanced segment classes (only four GEN examples per language), there is no defensible held-out estimate of generalization or evidence for a state-of-the-art claim.

The local importer writes 1,515 JSONL examples to a private output location: 751 segment examples and 764 valid relation examples. The one invalid Romanian relation is reported and omitted. Generated rows contain text and labels only; actor IDs, location IDs, and annotator-written segment names are excluded from model features to reduce annotation leakage. The provided temporal cue is included for the relation task and must be available to a future inference pipeline in the same form.

## Ro-TimeBank and the thesis

Ro-TimeBank supplies a useful **complementary** Romanian temporal-information resource. The archive README lists 183 Romanian TimeML documents, 181 aligned English-Romanian pairs, and XCES token/lemma/POS plus word alignments. It is translated English news with annotation transfer, not original literary narratives or TYM segment annotations. Its `<EVENT>`, `<TIMEX3>`, `<SIGNAL>`, and `TLINK` / `SLINK` / `ALINK` layers should support distinct event-span, temporal-expression, and relation tasks after a reviewed schema crosswalk.

Forăscu and Tufiș (2012) report a 99.18% TimeML-annotation transfer rate after their transfer, correction, and validation workflow. This is a transfer/coverage statistic, **not model accuracy**. The thesis reports 183 Romanian documents, 4,715 aligned sentences, 65,375 Romanian lexical units, and a 99.18% final transfer measure. It also describes extending genres and combining temporal with discourse annotations as future directions. Cite: Forăscu, Corina (2011), *Contribuții la prelucrarea limbii române folosind metode de analiză a discursului*, PhD thesis, Romanian Academy; and [Forăscu & Tufiș (2012), Romanian TimeBank](https://aclanthology.org/L12-1451/).

The supplied Ro-TimeBank archive states that research-use terms apply, commercial use requires a separate license, and underlying TimeBank/source-text rights are owned separately. Thus neither the raw archive, extracted rows, nor corpus-trained model weights are added to GitHub. Keep them in the user's local private bundle unless the applicable owners authorize wider distribution.

## Training plan and implementation

The adapter is [`tools/Tym.Modeling/import-tym-xml.py`](../../tools/Tym.Modeling/import-tym-xml.py). It only reads the supplied custom TYM XML; it is not a TimeML importer. It validates the XML root and unique IDs, rejects DTD/entity declarations, maps the known labels, skips relations with unresolved segment endpoints, writes provenance-marked UTF-8 JSONL, and produces aggregate counts.

Example, with paths to the private user-provided XMLs:

```powershell
python tools/Tym.Modeling/import-tym-xml.py `
  --romanian C:\private\hln_varianta_romana.xml `
  --english C:\private\motiw_varianta_engleza.xml `
  --out C:\private\tym-attached-annotations.jsonl `
  --report C:\private\tym-import-report.json
```

The modeling CLI trains the existing ML.NET `FeaturizeText` plus `SdcaMaximumEntropy` classifiers, available for English and Romanian as transparent lexical baselines. These support the pilot's segment and relation labels but are not neural models or research-quality models.

Example train-only English segment fit:

```powershell
dotnet run --project tools/Tym.Modeling -- train `
  --data C:\private\tym-attached-annotations.jsonl `
  --task segment_type `
  --language en `
  --model-out C:\private\models\segment_type_en_sdca.zip
```

Four ML.NET SDCA artifacts were trained locally and are stored in the private research bundle: `segment_type_en_sdca.zip` (376 rows), `segment_type_ro_sdca.zip` (375), `temporal_relation_en_sdca.zip` (382), and `temporal_relation_ro_sdca.zip` (382). They are one-story pilot classifiers. SDCA models trained from these annotations stay out of the public repo because their source rights are not established. No evaluation score was computed from the parallel pair, and their training resubstitution performance is not reported as evidence.

An ML.NET TorchSharp NAS-BERT training attempt was made after retrieving the official checkpoint, but fitting stopped with `Mismatched state_dict sizes: expected 60, but found 142 entries`. The same failure class is tracked in the [ML.NET repository's open NAS-BERT trainer issue](https://github.com/dotnet/machinelearning/issues/7350). I removed the non-working optional package path rather than leave a broken trainer in the CLI. Revisit it only after an upstream package/checkpoint pair is demonstrated to work end-to-end.

## A research-grade multilingual model stack

The next meaningful comparison should be document-grouped and should keep each source work, its translations, and any synthetic descendants together:

1. Train the transparent ML.NET SDCA models, per language, with strict training provenance. This is the inspectable baseline, not the target state of the art.
2. Fine-tune a multilingual encoder such as XLM-R in a working modern transformer training stack on the segment/frame labels and a separate temporal-relation classification head. Use native Romanian and English held-out works, plus cross-language transfer tests. Export compatible models to ONNX for ML.NET inference if the graph and tokenizer operations export cleanly. The 2012 Ro-TimeBank data may pretrain or supervise its own TimeML subtasks; do not convert its news labels directly into fiction/narrative labels.
3. For long-range temporal links, build an event/segment-pair encoder with document context and a graph-level component. Compare local pairwise decisions with graph-aware reasoning and evaluate transitivity/Allen-style consistency separately. Recent graph-transformer and global temporal-graph work offers architecture hypotheses; clinical-domain gains should not be assumed to transfer to narrative text.
4. Use synthetic data only after grouped splits. Generate minimal contrasts from training-only human/adjudicated parents, label lineage and transformations, then audit them. Preserve separate conditions for transfer-projected, synthetic, pseudo-labelled, and human-adjudicated data.
5. Report macro-F1/per-class scores for classification; exact and overlap span metrics for events/TIMEX/segments; relation F1 under a stated candidate-pair policy; graph consistency; calibration; uncertainty; inter-annotator agreement; and multilingual/genre slices. Run at least five seeds and bootstrap over source works when enough works exist.

The current sample is suitable for checking XML ingestion, API schema mapping, and an initial model-training path. Before a conference claim, expand to many independently sourced works with explicit rights, independent annotations, adjudication, and untouched held-out works/authors.

## Related work and technical references

- Forăscu & Tufiș (2012), [Romanian TimeBank: An Annotated Parallel Corpus for Temporal Information](https://aclanthology.org/L12-1451/).
- Forăscu (2011), *Contribuții la prelucrarea limbii române folosind metode de analiză a discursului*, user-provided PhD thesis.
- Su, Howard & Bethard (2025), [Transformer-Based Temporal Information Extraction and Application: A Review](https://aclanthology.org/2025.emnlp-main.1467/). Reviews 32 datasets across 15 languages; highlights news-domain skew and annotation-schema differences.
- Mathur et al. (2021), [TIMERS: Document-level Temporal Relation Extraction](https://aclanthology.org/2021.acl-short.67/). Uses contextual/discourse/time-aware graphs for document-level links.
- Chaturvedi et al. (2025), [Temporal Relation Extraction in Clinical Texts: A Span-based Graph Transformer Approach](https://aclanthology.org/2025.acl-long.1251/). Span plus heterogeneous graph modelling; clinical results are not a narrative benchmark.
- Eirew, Bar & Dagan (2025), [Beyond Pairwise: Global Zero-shot Temporal Graph Generation](https://aclanthology.org/2025.emnlp-main.1601/). Motivates whole-document graph output and consistency constraints.
- Conneau et al. (2020), [Unsupervised Cross-lingual Representation Learning at Scale](https://aclanthology.org/2020.acl-main.747/). XLM-R as a multilingual transfer model candidate.
- Microsoft, [ML.NET deep-learning overview](https://learn.microsoft.com/en-us/dotnet/machine-learning/deep-learning-overview) and [TextClassificationTrainer API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.torchsharp.torchsharpcatalog.textclassification?view=ml-dotnet-preview). ML.NET supports NAS-BERT text-classification fine-tuning; the trainer is not ONNX-exportable.
- Mathur et al. (2021), TIMERS; Tan et al. (2023), [TIMELINE](https://aclanthology.org/2023.emnlp-main.1016/); Ning et al. (2018), [MATRES](https://aclanthology.org/N18-1287/); Reimers et al. (2016), [CaTeRS](https://aclanthology.org/W16-1007/); Minard et al. (2016), [MEANTIME](https://aclanthology.org/L16-1699/); and [NarrativeTime](https://aclanthology.org/2024.lrec-main.1054/) provide complementary task datasets and analyses.
