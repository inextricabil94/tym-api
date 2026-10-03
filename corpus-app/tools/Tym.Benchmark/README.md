# ML.NET grouped diagnostic benchmark

This .NET 10 / ML.NET 5.0.0 tool compares conditional Romanian TimeBank label classifiers, and separately exports full-data structured inference artifacts. It accepts supplied annotations with unknown adjudication. It does not evaluate source span extraction, automatic endpoint discovery, complete temporal graphs or literary TYM generalization.

Run commands from the `tym-corpus-app` root. Corpus preparation is in [Tym.Corpus.Data](../Tym.Corpus.Data/README.md); architectural/evaluation decisions are in [architecture-review.md](../../docs/architecture-review.md).

## Input contract

Use the TimeBank JSONL produced by the C# importer. Every row requires a unique `id`, nonblank `text`/`label`, `document_id`, `source_type: provided_annotation` and `review_status: adjudication_unknown`. Preserve `source_group`, language and document identity. Supported targets are:

| Task | Conditional target | Structured channels |
| --- | --- | --- |
| `timebank_event_class` | Class of a supplied event | Target context, target mention |
| `timebank_event_tense` | Tense of a supplied event instance | Target context, target mention |
| `timebank_timex_type` | Type of a supplied temporal expression | Target context, target mention |
| `timebank_tlink` | Temporal label for supplied directed endpoints | Source/target contexts and mentions, signal |
| `timebank_slink` | Subordination label for supplied directed events | Source/target contexts and mentions, signal |
| `timebank_alink` | Aspectual label for supplied directed events | Source/target contexts and mentions, signal |

The shared `Tym.Corpus.Core.TaskInputParser` is used by structured training and API inference. Explicit field names are case-sensitive; repeated fields are rejected. These authored format examples illustrate input shape and are not evaluation observations:

```text
Context: Ana [TARGET]citește[/TARGET] o scrisoare.
Event: citește
```

For TIMEX, use `Time expression:` instead of `Event:`. For links:

```text
From: arrived
From context: Mara [TARGET]arrived[/TARGET] yesterday.
Signal: before
To: left
To context: Ana [TARGET]left[/TARGET] the garden.
```

FROM/TO are directed endpoint roles, not an assertion that the first endpoint is earlier. Parsing does not resolve or ground missing mentions. Target markers are lexical feature cues; a separate source-span validator is needed for end-to-end extraction.

## Diagnostic comparison

```powershell
dotnet run --project tools/Tym.Benchmark -- --data C:\private\tym\timebank.jsonl --out C:\private\tym\grouped-benchmark.json --folds 3 --seed 42
```

Optional single-task scoring uses the split derived from the complete input, preserving cross-task document folds:

```powershell
dotnet run --project tools/Tym.Benchmark -- --data C:\private\tym\timebank.jsonl --out C:\private\tym\tlink-diagnostic.json --folds 3 --seed 42 --task timebank_tlink
```

Use 2-10 folds and enough document components to populate them. Each task needs nonempty train/validation rows and at least two training labels in each fold. The single parallel TYM work cannot support this source-disjoint literary protocol; do not concatenate it into a purported multi-work evaluation.

The default comparison uses a training-fold majority label, whole-text SDCA maximum entropy, whole-text L-BFGS maximum entropy and structured-channel SDCA. These linear trainers use one thread and 100 maximum iterations; fold seed is the selected seed plus fold index. Feature transforms and label mapping fit only on the training fold. Entirely blank structured channels are omitted using training inputs, without inspecting validation labels.

Documents are keyed by source group/document ID. Identical Unicode/whitespace-normalized task/language/input strings join their documents into one component before deterministic, row-balanced fold assignment. The current run has 157 documents, 50 components and 107 cross-document duplicate rows, with 6,744 / 6,721 / 6,686 rows in three folds.

`independent_components` is the implementation's name for components of this duplicate-input graph. It is not proof of independent semantic works. The keys are normalized input hashes, not actual ML.NET feature vectors. Near duplicates, shared news events and normalization/representation collisions remain possible.

## Metrics and reports

The JSON report records input SHA-256, framework, seed, fold assignments/components, row counts, per-fold models' metrics, pooled out-of-fold metrics, class support and confusion matrices. Every input row is scored once out of fold. Macro F1 uses the fixed full task label inventory and gives unrecovered labels zero F1, including labels absent from some training folds. Accuracy must be read beside macro F1 and class counts.

Comparison mode exports no weights (`models_exported: false`). Its aggregate report omits corpus prose; raw inputs remain private. The completed comparison is retained in [the aggregate structured report](../../docs/results/grouped-mlnet-structured-benchmark.json).

The initial majority/SDCA/L-BFGS trial used fixed settings. Structured channels were added after inspecting the initial result and reused its three folds. Those gains are exploratory paired diagnostics, with no separate model-selection holdout or significance analysis. Supplied annotations have unknown adjudication; these are not independent human-gold scores. Classifiers are given mention boundaries/endpoints and cannot claim span, pair-candidate, end-to-end graph or book accuracy from these metrics.

## All seventeen algorithm families

The expanded comparison measures fourteen classifier variants from twelve families. Native ML.NET supplies SDCA/L-BFGS logistic regression, Naive Bayes, OVA linear SVM, OVA FastForest/FastTree, LightGBM and one class-indicator regression per label. Custom C# supplies bounded multiclass CART and exact cosine KNN. C# TorchSharp supplies compact scratch MLP, CNN, LSTM and Transformer classifiers; these are separate from native ML.NET trainers. No Python or pretrained model download is used.

```powershell
dotnet run --project tools/Tym.Benchmark -c Release -- --data C:\private\tym\timebank.jsonl --out C:\private\tym\all-classifiers.json --algorithms all --features structured --ngram-limit 1024 --epochs 5 --folds 3 --seed 42
```

`--ngram-limit 1024` fixes the native/numeric feature budget at 1,024 entries per n-gram order per active channel: word unigrams/bigrams and character trigrams, at most 3,072 features per channel. `0` retains historical library defaults. The vocabulary fits training rows only. CART additionally selects at most 128 training-variance features. Neural classifiers fit a distinct vocabulary of at most 2,048 IDs, retain the first 64 role-prefixed tokens, and train for five fixed epochs. The same rows and folds do not make these representations equivalent. Use the serialized configurations to interpret quality and cost.

Five additional pipelines cover the unlabeled families: KMeans, PCA+KMeans, average-linkage hierarchical clustering, DBSCAN and a numeric neural autoencoder. Run each language separately, after classifier timing finishes:

```powershell
dotnet run --project tools/Tym.Benchmark -c Release -- --mode books --data C:\private\tym\books-ro.jsonl --out C:\private\tym\books-ro-study.json --models C:\private\tym\book-models-ro-new --seed 42 --clusters 8 --rank 32
dotnet run --project tools/Tym.Benchmark -c Release -- --mode books --data C:\private\tym\books-en.jsonl --out C:\private\tym\books-en-study.json --models C:\private\tym\book-models-en-new --seed 42 --clusters 8 --rank 32
```

Book runs require new report/model destinations. Romanian validation holds out complete duplicate-linked document components. A single-work input uses a within-work normalized-passage holdout and cannot establish cross-book generalization. Fit text/PCA/centroids on training passages only. Hierarchical/DBSCAN operate on the same deterministic training sample of at most 256 vectors, with no heldout classifier. The autoencoder reconstructs normalized PCA vectors, with rank 32 compressed to 16 dimensions. Its MSE is compared with training-mean and zero-vector baselines; no prose decoder or neural weights are exported. Accuracy is unavailable for all unlabeled book pipelines.

Generate the complete measured summary only after all three report files finish:

```powershell
./tools/Tym.Benchmark/Write-AlgorithmSummary.ps1 -ClassificationReportPath C:\private\tym\all-classifiers.json -RomanianBookReportPath C:\private\tym\books-ro-study.json -EnglishBookReportPath C:\private\tym\books-en-study.json -OutputDirectory C:\private\tym\summary
```

The generator rejects partial runs, incomplete coverage, invalid feature budgets and nonfinite measurements. It produces Markdown, CSV and JSON for all seventeen families/nineteen variants: 84 classifier task measurements and ten book measurements. Classifier fit times sum three independent fold fits, and prediction throughput describes batched validation, not interactive latency. Book assessment timings have different workloads; shared prerequisite costs are repeated in the per-pipeline figures. One local CPU trial per fold supplies no timing confidence interval or universal performance ranking. See [the algorithm mapping](../../docs/algorithm-approaches.md) for all configurations and [the measured summary](../../docs/results/algorithm-comparison.md) for executed results.

## Full-data structured training

```powershell
dotnet run --project tools/Tym.Benchmark -- --mode train-structured --data C:\private\tym\timebank.jsonl --out C:\private\tym\structured-training.json --models C:\private\tym\models-new --seed 42
```

This mode trains all six TimeBank targets on all supplied rows, writing `<task>_ro_structured_sdca.zip` plus a matching `.manifest.json`. It refuses any existing target artifact/manifest name. Choose a new directory or one without those six artifacts. Baselines keep their original IDs and are not replaced. Assemble the sixteen intended classifiers in the deployment's trusted model directory.

Manifests record model ID, task/language, `input_format: structured_target_v1`, feature channels, trainer, seed, iterations, row/label counts, source provenance, input/weight hashes, framework and training timestamp. The API verifies present task/language identity and weight digest, uses the shared parser, and rejects malformed task input without evicting a healthy model. A hash verifies artifact bytes, not NLP validity.

The full-data training report has status `full_data_inference_artifacts`. It supplies inference weights; it does not report an accuracy estimate on those same rows. Use the separate grouped report for diagnostic evidence and a future independent adjudicated source-work test for scientific promotion.

## Replication plan

Freeze an independent development/test protocol before selecting representations. Compare all candidates on identical source-work partitions, grouping translations and derivatives. Report per-document results and document/component confidence intervals alongside pooled macro F1. Audit near duplicates and directional/target ablations. Evaluate strict spans and end-to-end labeled relations separately, and keep reviewed corrections outside human gold until adjudication.
