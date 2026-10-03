# All-algorithm study — 3 October 2026

The user requested every family in the supplied algorithm chart, with measured time, space, performance and accuracy details. Work is isolated on `codex/tym-algorithm-comparison`.

## Implemented research routes

- Twelve classification families produce fourteen configurations: least-squares linear regression with class-indicator targets; SDCA/L-BFGS logistic regression; multiclass CART; FastForest; FastTree/LightGBM boosting; linear SVM; exact cosine KNN; feature-presence Naive Bayes; and compact C# MLP/CNN/LSTM/Transformer models.
- Five additional families explore the unlabeled books: KMeans, PCA followed by KMeans, average-linkage hierarchical clustering, DBSCAN and a neural numeric autoencoder.
- Native trainers and preprocessing use ML.NET 5.0.0. The custom CART/KNN are C# implementations. Neural training uses C# TorchSharp CPU 0.107.0 and LibTorch 2.10.0. No Python or downloaded pretrained weights are involved.
- The benchmark builds with zero warnings/errors. Original synthetic runtime checks fitted all four neural classifiers and the autoencoder with finite outputs.
- The fresh complete .NET run passed 144 checks with private integration enabled: 70 API, 31 baseline modeling and 43 research. This includes original-authored neural/book contracts and nonzero feature-budget regressions. Test counts are software checks, not semantic accuracy.

## Completed measurements

The classification run completed all 20,151 retained TimeBank task rows, three document-component folds over 50 components, seed 42, structured channels and five fixed neural epochs, in 3,893.0 seconds. The fourteen configurations share fold assignments. Native/numeric classifiers use 1,024 word unigrams/bigrams and character trigrams per order/channel (at most 3,072 features per active channel). Compact neural models use a separate training-only, role-prefixed vocabulary capped at 2,048 IDs and a 64-token window; results retain that representation limitation.

An uncapped runtime pilot was stopped before accuracy inspection after expensive sparse tree fitting. The shared cap is a resource decision; every classifier in the completed comparison must use the new prescribed budget. A character extractor option-format error was fixed and checked by the four dedicated regressions before restarting corpus fitting.

Run:

```powershell
dotnet run --project tools/Tym.Benchmark -c Release -- --data C:\private\timebank.jsonl --out C:\private\all-algorithms-classification-budget1024.json --algorithms all --features structured --ngram-limit 1024 --epochs 5 --folds 3 --seed 42
```

The completed report has all six tasks and fourteen configurations, producing 84 classifier task measurements. Romanian books use 4,037 training / 1,176 heldout passages from 14 supplied documents; English uses 356 / 90 passages from one work. Both book runs completed all five pipelines, producing ten additional measurements. Hierarchical/DBSCAN use at most 256 training-only PCA vectors; the English holdout is within-work. No semantic accuracy is available for unlabeled books or numeric reconstruction. These retrospective comparisons remain conditional diagnostics with unknown annotation adjudication, without an independent model-selection holdout.

The [Markdown report](results/algorithm-comparison.md), [CSV](results/algorithm-comparison.csv) and [UI JSON](results/algorithm-comparison.json) contain all seventeen families/nineteen variants, hashes of the input/report evidence and measurement limitations. The generator rejects incomplete evidence and nonfinite costs. PCA basis-fit time is preserved alongside downstream geometry. Space fields include actual input dimensions, neural parameter counts and float32 bytes, process memory snapshots/lifetime peaks and exported book artifact bytes. Shared-process RAM is not isolated model memory. Unexported classifier/autoencoder storage remains null. Private weights, raw books and input JSONL remain outside source/deployment.

The TypeScript comparison view includes language/task filters, same-origin downloads and accessible space/storage details, with five original-synthetic contract tests. TypeScript typecheck/build and the UI host build pass. The complete local browser run passed 21 checks including five private book samples on localhost. Three public smoke checks passed with original authored prose and aggregate result downloads. The [verification record](results/algorithm-study-tests.json) contains all 144 .NET / 24 distinct browser checks, zero failures/skips.

Azure revision `tym-corpus-ui-serban--0000002` is ready with 100% traffic. Docker ACR build `ca8` published `tym-corpus-ui:research-20261003-algorithms-v1`, digest `sha256:3893c0693941dafa40b0171af94b0b63fa7aaecae8de8171000d27d2c741c831`. All three comparison downloads and the retained 2 October PDF/PPTX matched local SHA256s and MIME types. The [artifact verification](results/algorithm-deployment-artifacts.json) records those bytes. The production API reports sixteen ready inference classifiers; research comparison fits do not promote alternative weights. Source checkpoints through `6118c3a` were pushed before corpus fitting completed; measured evidence and final release records are saved in the subsequent commit on the same separate branch.

The in-app browser refresh encountered `ERR_NAME_NOT_RESOLVED`; CLI HTTPS and the public Chrome smoke tests verified the live deployment. The updated comparison URL was queued in the Codex panel. A queued panel or failed browser refresh is not treated as verified visual navigation.
