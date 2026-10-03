# All-algorithm study — 3 October 2026

The user requested every family in the supplied algorithm chart, with measured time, performance and accuracy summaries. Work is isolated on `codex/tym-algorithm-comparison`.

## Implemented research routes

- Twelve classification families produce fourteen configurations: least-squares linear regression with class-indicator targets; SDCA/L-BFGS logistic regression; multiclass CART; FastForest; FastTree/LightGBM boosting; linear SVM; exact cosine KNN; feature-presence Naive Bayes; and compact C# MLP/CNN/LSTM/Transformer models.
- Five additional families explore the unlabeled books: KMeans, PCA followed by KMeans, average-linkage hierarchical clustering, DBSCAN and a neural numeric autoencoder.
- Native trainers and preprocessing use ML.NET 5.0.0. The custom CART/KNN are C# implementations. Neural training uses C# TorchSharp CPU 0.107.0 and LibTorch 2.10.0. No Python or downloaded pretrained weights are involved.
- The benchmark builds with zero warnings/errors. Original synthetic runtime checks fitted all four neural classifiers and the autoencoder with finite outputs.
- The initial unit run passed 30 checks, including all eleven new classifier checks. Three private book checks failed solely because the attachment moved from Desktop to Desktop/TYM; its current path has been located for rerunning them.

## Evaluation in progress

The new classification run uses all 20,151 retained TimeBank task rows, the original three document-component folds, seed 42, structured channels and five fixed neural epochs. The fourteen configurations share fold assignments. Compact neural models use a separate training-only, role-prefixed vocabulary and bounded sequence window; results must retain that representation limitation.

Run:

```powershell
dotnet run --project tools/Tym.Benchmark -c Release -- --data C:\private\timebank.jsonl --out C:\private\all-algorithms-classification.json --algorithms all --features structured --epochs 5 --folds 3 --seed 42
```

Completed task groups are checkpointed to the private JSON report. Final reporting requires the completed status, all six tasks and all fourteen configurations. No accuracy is invented for unlabeled books or numeric reconstruction. These retrospective comparisons remain conditional diagnostics with unknown annotation adjudication, without an independent model-selection holdout.

Next: complete measured runs, generate the summary tables/CSV/JSON, rerun private integration using the current attachment path, publish the comparison view, and push final evidence. The prior approved deployment and its sixteen inference classifiers remain the recorded production release while this study runs.
