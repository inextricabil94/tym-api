# All-algorithm study — 3 October 2026

The user requested every family in the supplied algorithm chart, with measured time, performance and accuracy summaries. Work is isolated on `codex/tym-algorithm-comparison`.

## Implemented research routes

- Twelve classification families produce fourteen configurations: least-squares linear regression with class-indicator targets; SDCA/L-BFGS logistic regression; multiclass CART; FastForest; FastTree/LightGBM boosting; linear SVM; exact cosine KNN; feature-presence Naive Bayes; and compact C# MLP/CNN/LSTM/Transformer models.
- Five additional families explore the unlabeled books: KMeans, PCA followed by KMeans, average-linkage hierarchical clustering, DBSCAN and a neural numeric autoencoder.
- Native trainers and preprocessing use ML.NET 5.0.0. The custom CART/KNN are C# implementations. Neural training uses C# TorchSharp CPU 0.107.0 and LibTorch 2.10.0. No Python or downloaded pretrained weights are involved.
- The benchmark builds with zero warnings/errors. Original synthetic runtime checks fitted all four neural classifiers and the autoencoder with finite outputs.
- The corrected research run passed all 33 checks with the current Books.zip location. Four additional nonzero-budget regressions passed, covering SDCA, FastTree and LightGBM. Six more original-authored neural/book contract cases have been added and await the final full-suite run after timed corpus fitting.

## Evaluation in progress

The new classification run uses all 20,151 retained TimeBank task rows, the original three document-component folds, seed 42, structured channels and five fixed neural epochs. The fourteen configurations share fold assignments. Native/numeric classifiers use 1,024 word unigrams/bigrams and character trigrams per order/channel (at most 3,072 features per active channel). Compact neural models use a separate training-only, role-prefixed vocabulary capped at 2,048 IDs and a 64-token window; results must retain that representation limitation.

An uncapped runtime pilot was stopped before accuracy inspection after expensive sparse tree fitting. The shared cap is a resource decision; every classifier in the completed comparison must use the new prescribed budget. A character extractor option-format error was fixed and checked by the four dedicated regressions before restarting corpus fitting.

Run:

```powershell
dotnet run --project tools/Tym.Benchmark -c Release -- --data C:\private\timebank.jsonl --out C:\private\all-algorithms-classification-budget1024.json --algorithms all --features structured --ngram-limit 1024 --epochs 5 --folds 3 --seed 42
```

Completed task groups are checkpointed to the private JSON report. Final reporting requires the completed status, all six tasks and all fourteen configurations. No accuracy is invented for unlabeled books or numeric reconstruction. These retrospective comparisons remain conditional diagnostics with unknown annotation adjudication, without an independent model-selection holdout.

The TypeScript comparison view and its three original-synthetic UI tests are saved. The summary generator validates completed evidence for all six tasks, fourteen classifier variants, five book pipelines in each language, finite measurements and the recorded feature budgets. PCA basis-fit time is preserved alongside downstream geometry. The source checkpoints have been pushed to the user's separate branch; latest pushed checkpoint is `ee5a491`.

Next: finish the active classification run, then run Romanian and English book exploration sequentially, generate the real Markdown/CSV/JSON, run the full .NET and UI suites with private integration enabled, publish the comparison view, and push final evidence. The prior approved deployment and its sixteen inference classifiers remain the recorded production release while this study runs. New comparison classifiers are research fits; no alternative weights are promoted by this run.
