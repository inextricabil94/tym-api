# Ten book experiment integration workflows

These tests exercise the actual supplied prepared book corpora through C# ML.NET and TorchSharp. They train fresh pipelines, verify an independently reproduced split, reload exported ML.NET models, and inspect the private bounded clustering artifacts. They do not infer semantic accuracy from unannotated prose.

## Coverage

| Case | Language | Workflow | What the integration test establishes |
|---|---|---|---|
| `book_ro_mlnet_kmeans` | Romanian | Lexical KMeans | A fresh training-only text vocabulary and eight centroids survive model save/load and score an original authored query with finite distances. |
| `book_en_mlnet_kmeans` | English | Lexical KMeans | The same pipeline works on the English source work and preserves its within-work passage holdout. |
| `book_ro_mlnet_pca_kmeans` | Romanian | PCA plus KMeans | The saved pipeline transforms an authored query into 32 finite dimensions before returning one of eight arbitrary clusters. |
| `book_en_mlnet_pca_kmeans` | English | PCA plus KMeans | The English training-only vocabulary, 32-dimensional projection and centroid scorer round-trip together. |
| `book_ro_csharp_average_linkage_hierarchical` | Romanian | Average-linkage hierarchy | Exactly 256 deterministically selected training vectors are finite, their sample hash matches, and eight cluster assignments reproduce aggregate counts. |
| `book_en_csharp_average_linkage_hierarchical` | English | Average-linkage hierarchy | The same bounded, training-only sample contract holds for the English source work. |
| `book_ro_csharp_dbscan` | Romanian | DBSCAN | Epsilon 0.45 and minPoints 5 produce internally consistent finite-vector cluster/noise counts without using heldout passages. |
| `book_en_csharp_dbscan` | English | DBSCAN | English sample assignments agree with their private artifact and aggregate noise metrics. |
| `book_ro_csharp_torchsharp_autoencoder` | Romanian | Numeric autoencoder | A scratch C# network fits five training-only epochs, maps 32 dimensions to 16, and records finite heldout reconstruction MSE alongside zero and training-mean baselines. |
| `book_en_csharp_torchsharp_autoencoder` | English | Numeric autoencoder | The same reconstruction pipeline works on English PCA vectors without pretrained weights or validation-based stopping. |

## Data and splitting

The exact supplied prepared input files are pinned by SHA256, so a changed corpus needs an explicit fixture update rather than silently changing this experiment.

| Language | Passages | Supplied document groups | Training / heldout | Interpretation |
|---|---:|---:|---:|---|
| Romanian | 5,213 | 14 | 4,037 / 1,176 | Twelve training and two heldout document components; normalized duplicate passages cannot cross the split. |
| English | 446 | 1 | 356 / 90 | Normalized passage groups held out inside the single supplied Tash Aw work; this does not measure cross-book generalization. |

All five algorithms share the same seed-42 split per language. The fixture fits the five real pipelines once per language and also fits an independent text featurizer on the reproduced training rows. Reloaded KMeans and PCA plus KMeans query vectors must match that independently fitted vocabulary.

## How to run

Set the private inputs and choose a new evidence directory, then run the ten cases:

```powershell
$env:TYM_PRIVATE_BOOK_RO_JSONL = 'C:\private-corpora\books-ro-unlabeled.jsonl'
$env:TYM_PRIVATE_BOOK_EN_JSONL = 'C:\private-corpora\books-en-unlabeled.jsonl'
$env:TYM_WORKFLOW_EVIDENCE_DIR = 'C:\private-results\workflow-run-unique'
dotnet test tests/Tym.Research.Tests/Tym.Research.Tests.csproj -c Release --filter FullyQualifiedName~PrivateBookWorkflowIntegrationTests
```

Missing environment variables cause an explicit skip. Real input files and model weights stay outside Git. Temporary weights are confined to a fixture-owned directory and deleted after the collection finishes. Inputs are read without modification; their checksums are rechecked after fitting.

After every assertion in a case succeeds, an aggregate JSON record is created in `TYM_WORKFLOW_EVIDENCE_DIR/books/`. The evidence contains counts, corpus/split/report/artifact hashes, dimensions, finite metrics, measured integration-run costs, memory scope, and explanatory assertions. It contains no book passages, document identifiers or absolute private paths. Existing evidence files are never overwritten.

## Reading the evidence

- **KMeans and PCA plus KMeans:** heldout centroid distance and Davies–Bouldin index describe the chosen feature geometry. Cluster IDs are arbitrary and have no mapping to narrative, temporal or semantic labels. An undefined Davies–Bouldin value is allowed only when the report explicitly records degenerate clusters.
- **Hierarchy and DBSCAN:** sample silhouette and noise describe at most 256 training-only projected vectors. These methods have no inductive heldout classifier in this experiment. DBSCAN may legitimately return noise or few clusters.
- **Autoencoder:** heldout MSE describes reconstruction of normalized numeric PCA vectors. It is not prose reconstruction, NLP extraction accuracy or semantic classification accuracy. The model has 6,320 parameters, uses a 16-dimensional bottleneck, and exports neither experimental weights nor heldout encodings.
- **Time and space:** fresh integration costs include one shared language run; they do not replace the algorithm comparison benchmark. Working-set and lifetime-peak memory belong to the shared process. Artifact size measures the compressed ML.NET pipeline or private sample JSON; float32 parameter bytes exclude gradients, optimizer state and activations.

The illustrated workflow report is generated from successful aggregate evidence by the release orchestration. A green integration result establishes that these connected software steps worked on the supplied data; semantic accuracy remains unavailable until suitable gold annotations exist.
