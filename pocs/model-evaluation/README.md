# ML.NET model training, evaluation, and data discovery

The production-facing training code lives in [`tools/Tym.Modeling`](../../tools/Tym.Modeling). It provides three commands:

- `train`: fit and save a supervised multiclass ML.NET text classifier.
- `evaluate`: train on a document- or chapter-grouped training partition and report held-out metrics on adjudicated human gold.
- `cluster`: fit an unsupervised ML.NET K-Means model on unlabeled narrative text and write cluster assignments for analyst review.

The repo includes 43 synthetic segment-type seeds and 24 synthetic event-temporal seeds. They are demo inputs only. There is no annotated TYM corpus in the repo, and no accuracy or generalization result can be computed from the checked-in seeds.

## Input schemas

Use one UTF-8 JSON object per line. For gold annotations, include a unique `id`, `task`, `text`, `label` (or `gold_label`), `language`, `source_type`, `review_status`, `document_id`, and `chapter_id`:

```json
{"id":"work-a-ch03-e01","task":"event_temporal","text":"By dawn, Mara had returned.","label":"Past","language":"en","source_type":"human_gold","review_status":"adjudicated","document_id":"work-a","chapter_id":"ch03"}
```

Human rows must use `source_type: "human_gold"` and `review_status: "adjudicated"` for evaluation. Training also accepts rows marked `source_type: "synthetic_seed"` and `review_status: "unreviewed"`. Add `parent_id` when a synthetic row descends from a gold example. Optional `generator_version`, `phenomenon`, and `transformation` metadata is accepted and summarized in the model manifest, but never enters the model features.

For unlabeled clustering, rows need only `id` and `text`; an optional `language` restricts the selection:

```json
{"id":"story-12-sentence-4","text":"Mara remembered the summer house.","language":"en"}
```

The modeling CLI does not read labels in cluster inputs or map clusters to TYM classes.

## Train and load an artifact

```powershell
dotnet run --project .\tools\Tym.Modeling -- train `
  --data .\data\seed-examples.jsonl `
  --task segment_type `
  --language en `
  --model-out .\artifacts\segment_type_en.zip
```

Repeat with `--task event_temporal` and output `event_temporal_en.zip`. Configure the API's `TYM_MODEL_DIR` to that directory. If the setting is explicit but an expected model is missing, the API fails clearly instead of silently using the seed model. Without `TYM_MODEL_DIR`, the API retains its seed-trained fallback. Romanian remains rule-based.

The 43/24-row training smoke runs on the checked-in seeds only show that serialization and the ML.NET pipeline run. They must not be reported as classifier evaluation results.

## Evaluate with group-safe splits

```powershell
dotnet run --project .\tools\Tym.Modeling -- evaluate `
  --data C:\data\tym-adjudicated.jsonl `
  --language en `
  --group-by chapter `
  --splits-out C:\data\tym-splits.json `
  --report C:\data\tym-evaluation.json
```

The split manifest stores hashes of document/chapter group identifiers rather than raw group names. At least three independent groups are required. Use `--group-by document` when multiple works are available; for a single work, hold out whole chapters. Reuse the same manifest for all ablations. Validate that each partition contains the classes needed for interpretation; group-level splitting can produce class imbalance or a class absent from a partition.

The report includes accuracy, macro-F1, per-class precision/recall/F1/support, a confusion matrix, and ML.NET micro/macro accuracy plus log-loss measures. This CLI scores event and segment **classification** rows. It does not currently score span-boundary F1, temporal relation extraction, or whole-graph consistency.

Each trained `.zip` model is accompanied by a `.zip.manifest.json` sidecar containing the task, language, trainer/featurizer names, random seed, row count, labels/source types, synthetic-parent coverage, counts by phenomenon/transformation/generator version, and model SHA-256. The manifest excludes text and raw document/chapter identifiers.

To measure the incremental value of synthetic examples, evaluate once with only the gold data and again with `--train-seeds C:\data\synthetic-train.jsonl`. Use the exact same held-out manifest both times. Synthetic rows enter the training fold only. If a seed has `parent_id`, its parent must map to the training partition. A model trained with a saved split may use `--split train` (default) or `--split dev`; the test partition is reserved for final scoring.

## Explore unlabeled text

```powershell
dotnet run --project .\tools\Tym.Modeling -- cluster `
  --data C:\data\unlabeled-narratives.jsonl `
  --language en `
  --clusters 8 `
  --model-out .\artifacts\narrative-clusters.zip `
  --assignments-out C:\data\cluster-assignments.jsonl `
  --report C:\data\cluster-summary.json
```

This fits ML.NET `FeaturizeText` n-gram features followed by centroid-based K-Means. Its average distance and Davies-Bouldin index describe geometric cohesion/separation in that feature space; they are not semantic accuracy. Inspect representative examples and outliers, repeat with different seeds and feature choices, and ask annotators to validate the discovered groups. The ML.NET clustering implementation is not a contextual language-model pretrainer.

## Recommended synthetic data design

1. Write an annotation manual and split existing human annotations by document/chapter **before** generating any descendants. Keep the final test set human-authored, adjudicated, and locked.
2. Create contrast sets that isolate TYM phenomena: temporal anchors and tense/aspect conflicts; retrospective/prospective shifts; remembered and imagined events; habitual/general statements; fiction and reporting frames; nominalized events; long-distance links; and ambiguous or absent cues.
3. Vary names, predicates, syntax, lengths, and discourse context independently of class. Avoid making a generator template, cue word, or character name a shortcut for the label.
4. Start with deterministic, auditable templates and transformations whose label-preservation assumptions are explicit. LLM paraphrases and model-proposed labels are candidates, not adjudicated truth; check for semantic drift and template artifacts before they enter train.
5. Record parent, generator version, phenomenon, transformation, and review status. Generate descendants only from training parents; cap synthetic/gold ratios and compare a no-synthetic baseline against several controlled mixtures.
6. Use unlabeled clustering to find recurring modes, coverage gaps, and hard examples to annotate. Sample from clusters and outliers, double-annotate a subset, adjudicate disagreements, and never treat a cluster ID as a gold TYM label.
7. Report paired results on the same document/chapter-held-out human gold: accuracy and macro-F1, class-wise results, span boundaries, relation quality, calibration, and graph consistency. Include generator/phenomenon ablations and error analysis.

The current repo implements train/evaluate and unlabeled K-Means workflows. A controlled synthetic corpus generator, calibrated sample filtering, contextual embeddings, boundary/link models, and graph-level evaluators are next research steps—not implemented results.
