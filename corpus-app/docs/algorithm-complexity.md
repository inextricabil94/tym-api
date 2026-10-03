# Computational complexity of the 17 implemented algorithm families

Reviewed against the repository implementation on 3 October 2026.

These are explanatory operation and storage bounds for this experiment. They are not measured seconds, accuracy predictions, or guarantees from a library vendor. The comparison reports contain the actual timings and scores. Constants, convergence, native allocation, input length, and hardware can dominate at this experiment's sizes.

The bounds below are derived from the local C# code, with primary sources used to identify the native algorithms and operators. ML.NET packages are pinned to 5.0.0; TorchSharp uses CPU LibTorch 2.10.0. Online documentation and source listings can describe another revision, so they do not establish exact allocation or instruction counts for the pinned binaries.

## Scope and notation

Training means one fitted training partition. The classification report sums training and prediction time over three folds; it does not time one final production fit. Prediction bounds concern one already represented input unless a row explicitly includes representation construction. Scoring `q` inputs generally multiplies that work by `q`, plus output storage and batching overhead.

Space is measured in scalar or index slots, not bytes. **Model** means retained numeric parameters or stored examples. **Workspace** means additional fitting structures; add model storage, the shared inputs, dictionaries, caches, runtime, and batch output buffers to estimate process memory. Prediction workspace is stated separately where it is material. Private artifact sizes and sampled process working set are empirical measurements with different meanings.

| Symbol | Meaning |
| --- | --- |
| `n`, `q`, `c` | Training rows, assessed rows, and training classes. |
| `d`, `Z`, `s` | Numeric feature width, total stored training feature slots, and stored slots in one query. Dense inputs have `Z = nd` and `s = d`; sparse buffers can still explicitly store zeros. |
| `U` | Observed feature coordinates considered by the custom CART variance selector; `U <= d`. |
| `I`, `g`, `h_b` | Actual optimizer passes/iterations, L-BFGS objective/gradient evaluations, and L-BFGS history length. An iteration need not be one data pass. |
| `k`, `r`, `u`, `p`, `t_p` | Cluster count, PCA rank, oversampling, sketch width `p = r + u`, and randomized matrix passes. Books request `k = 8`, `r = 32`, `u <= 8`; effective rank is bounded by feature width and `n - 1`. |
| `m` | Deterministic training sample size for hierarchy and DBSCAN, `m <= min(n, 256)`. |
| `f`, `H`, `J`, `tau` | CART selected coordinates, depth, nodes, and candidate thresholds per feature/node: `f <= 128`, `H <= 8`, `tau <= 8`. |
| `T`, `ell`, `B`, `H_t` | Trees per class/round, leaves per tree, histogram bins per feature, and tree depth. Here `ell <= 16`; a safe unbalanced depth bound is `H_t <= ell - 1`, not `log2(ell)`. |
| `E`, `b`, `L`, `V` | Neural epochs, batch size, sequence length, and vocabulary: `E = 5`, `b = 32`, `L = 64`, `V <= 2048` including padding and unknown tokens. |
| `e`, `h`, `a`, `F`, `A`, `w`, `z` | Embedding width, sequence hidden width, fully connected hidden width, Transformer feedforward width, heads, convolution width, and autoencoder bottleneck. Local values: `e = h = 32`, `a = F = 64`, `A = 4`, `w = 3`, `z = max(1, min(32, floor(r/2)))`. |
| `P`, `C_f`, `C_a` | Neural parameter count, forward work per row, and activation storage per batch, defined below. |

All bounds assume fixed-width arithmetic, ordinary hash-table behavior, and successful finite inputs. Text character processing, hashing, string comparison, and dictionary string storage must also be counted when lengths vary. They cannot be inferred from `n` alone.

## Shared representation costs

Classification fits its word/character n-gram dictionaries on each training fold only. At most 1,024 features are retained for each word unigram, word bigram, and character trigram channel. Thus `d <= 3 * 1024 * active_channels`, rather than 1,024 total features. An absent training channel is omitted. The raw-book ML.NET featurizer uses its own default limits; the classification cap should not be applied to the book models' complexity.

Feature extraction scans text, generates fixed-order n-grams, and constructs dictionaries before model fitting. A numeric cache or materialized sparse matrix costs `O(Z + n)` slots; a dense matrix costs `O(nd)`. Final sparse examples can coexist with dense model weights, PCA projections, or native histograms. A feature cap limits retained coordinates, not necessarily all temporary candidates encountered during dictionary fitting.

The custom classifiers consume the same fold-fitted ML.NET vectors as the native classifiers. Scratch text networks instead fit a token vocabulary and encode the first 64 tokens. Their LINQ `GroupBy` vocabulary builder first groups **all** training tokens and sorts distinct token counts; if there are `N_tok` token occurrences and `U_tok` distinct tokens, expected work is `O(N_tok + U_tok log U_tok)` with `O(N_tok + U_tok)` temporary grouping storage, before keeping at most 2,046 ordinary vocabulary entries. Encoded training/assessment arrays add `O((n + q)L)` slots. The final vocabulary cap does not bound that initial grouping memory.

## Eight classical classification families

The following bounds exclude shared text/vector fitting and caching. Linear SDCA/L-BFGS trainers have a 100-iteration cap; linear SVM requests 100 passes. Native trainers are configured with one thread. When an implementation detail affects sparsity or passes, the bound keeps an explicit parameter rather than treating the configured cap as a precise runtime count.

| Family and implemented variant | Fitting work | One-query prediction work | Model slots | Additional fitting workspace |
| --- | --- | --- | --- | --- |
| Linear regression: `linear_regression_ovr`, one ML.NET squared-loss SDCA regressor per class | Typical sparse pass bound `O(c I (Z + n + d))`; dense substitution gives `O(c I (nd + n + d))`. | `O(c(s + 1))` scores, plus the wrapper's class ranking; a conservative sorting bound is `O(c log c)`. | `O(cd + c)` | Active binary optimizer `O(d + n)`; previously fitted class models remain retained. Assessment buffers all `qc` class scores. |
| Logistic regression: multiclass SDCA and L-BFGS maximum entropy | SDCA: `O(I(cZ + nc + cd))`. L-BFGS: `O(g(cZ + nc + cd) + I h_b cd)`, including full evaluations and history recursion. | `O(c(s + 1))`, including class normalization/selection. | `O(cd + c)` | SDCA dual variables and parameter copies: `O(nc + cd)`. L-BFGS history/work vectors: `O(h_b cd + cd)`. |
| Decision tree: custom multiclass CART | `O(Z + U log(U + 1) + nf + n(H + 1) + f H n log(n + 1) + (f tau + 1)cJ)`, including variance selection, projection, node counts, and repeated sorting. | Sparse projection and traversal: `O(s + f + H + 1)`; dense projection: `O(f + H + 1)`. | `O(J + f + c)` | `O(U + nf + (n + c)(H + 1))`: moments, projected rows, retained recursive row lists, sorting buffers, and class counts. |
| Random forest: `fastforest_ova`, 64 trees per class | Conservative dense bound `O(c T C_tree) + C_cal`, with `T = 64` and the tree/calibration bounds below. | `O(c T H_t a_lookup + c)`. | `O(c T ell + c)` | Conservative native working representation/histograms `O(nd + ell dB + nc)`. |
| Gradient boosting: `fasttree_ova` or multiclass `lightgbm` | Conservative dense bound `O(c T C_tree + Tnc) + C_cal`; `T = 100`. `C_cal` applies to OVA FastTree when calibration is needed. | `O(c T H_t a_lookup + c)`. | `O(c T ell + c)` | Conservative native data/histogram/gradient workspace `O(nd + ell dB + nc)`. |
| SVM: `linear_svm_ova`, linear PEGASOS | `O(c[I(Z + n) + j_proj d])` if `j_proj` full-vector projection/materialization operations occur. `j_proj <= In` gives the conservative dense upper bound `O(c I nd)`. | `O(c(s + 1))`; raw margins are ranked without probability calibration. | `O(cd + c)` | Active binary parameter/update vectors `O(d)`; add row/shuffle state `O(n)` when retained. |
| KNN: custom exact cosine, `k_nn = min(5,n)` | Copying and L2 normalization: `O(Z + n)`; label indexing adds `O(c log c + n)`. No search index is built. | All sparse rows: `O(Z + ns + n k_nn + c)`; dense worst case: `O(nd + n k_nn + c)`. | `O(Z + n + c)` stored normalized examples/labels. Dense worst case `O(nd + n + c)`. | `O(n + c)` row/label administration besides the stored copies. Per query: `O(s + k_nn + c)`. |
| Naive Bayes: ML.NET feature-presence classifier | `O(Z + n + cd)` counting and dense absent-feature/class precomputation. | `O(c(s + 1))`. | `O(cd + c)` class/feature counts or probabilities. | `O(cd + c)` count/precomputation structures; these can overlap the final model representation. |

### Why these differ from a generic algorithm chart

The linear-regression classifier fits binary indicator targets, one regressor per class. It is iterative SDCA, so an ordinary least-squares matrix-inversion formula such as `O(nd^2 + d^3)` does not describe its fit. Multiclass SDCA maintains row/class dual state and performs class score/update work; L-BFGS stores a limited history and can evaluate the objective multiple times during line search. Their operation counts follow these methods' updates and state. [ML.NET SDCA source](https://source.dot.net/Microsoft.ML.StandardTrainers/Standard/SdcaMulticlass.cs.html), [L-BFGS maximum-entropy API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.standardtrainerscatalog.lbfgsmaximumentropy?view=ml-dotnet-preview).

The SVM is a linear weighted hyperplane using PEGASOS. A quadratic kernel matrix is absent. Sparse updates can be cheap, but full-vector projection or scaling must be counted when performed. The table therefore exposes `j_proj` instead of asserting that every native operation is sparse. [Microsoft LinearSvmTrainer documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.trainers.linearsvmtrainer?view=ml-dotnet-preview).

Naive Bayes treats `value > 0` as feature presence; it does not use TF-IDF magnitude as multinomial token counts. Its class histograms are dense even when the input buffers are sparse. [ML.NET Naive Bayes source](https://source.dot.net/Microsoft.ML.StandardTrainers/Standard/MulticlassClassification/MulticlassNaiveBayesTrainer.cs.html).

The CART implementation selects at most 128 features by training variance. At each node it sorts the node's rows again for every selected feature, and tests at most eight boundaries. Limiting boundaries does not eliminate sorting. Summing rows at each of at most eight depths bounds sorting by `O(f H n log n)`. `J <= 2^(H+1) - 1 <= 511`; where a split occurs, each child must have at least five rows. The KNN implementation compares every training vector: two sparse buffers are merged in `O(s_i + s)` time, so its query bound includes the query being scanned repeatedly. Neither algorithm has an approximate neighbor index. These are direct deductions from [CustomClassifiers.cs](../tools/Tym.Benchmark/CustomClassifiers.cs).

### Native tree bounds and assumptions

For the forest/boosting rows, use the deliberately conservative dense per-tree bound

`C_tree = O(nd log(n + 1) + ell nd + ell dB)`.

This permits feature sorting/bin construction and repeated row/feature scans across leaves. It is an upper workload model under ordinary comparison sorting and histogram-based splitting, **not** a claim that either library repeats all these operations for every tree. Binning can be shared, histogram subtraction can reduce work, bagging/features use fixed fractions, and sparse construction can depend on nonzero values. In particular, LightGBM documents histogram construction from nonzero inputs and split scanning over bins; scanning `dB` bins remains possible after sparse construction. An alternative sparse histogram estimate replaces appropriate `nd` construction terms with `Z`, while retaining bin-scan terms. [Official LightGBM feature documentation](https://github.com/lightgbm-org/LightGBM/blob/main/docs/Features.rst).

FastForest is a bagged ensemble; FastTree builds sequential boosted trees. OVA fits one binary ensemble per class, and multiclass LightGBM can build class-specific trees in each round. Thus a lone `T` factor can omit multiclass work. Exact scheduling and native buffers require the pinned runtime's source or profiling. [FastForest documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.trainers.fasttree.fastforestbinarytrainer?view=ml-dotnet-preview), [FastTree documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.trainers.fasttree.fasttreebinarytrainer?view=ml-dotnet-preview), [ML.NET OVA source](https://source.dot.net/Microsoft.ML.StandardTrainers/Standard/MulticlassClassification/OneVersusAllTrainer.cs.html).

`a_lookup` is feature-access cost during traversal. It is constant for dense random access, can be `O(log(s + 1))` with sparse index lookup, or can require an upfront `O(d)` densification. Native representation controls the actual case. Probability calibration, when needed, receives at most 20,000 training rows per binary model. With `n_cal` calibration rows and `I_cal` scalar optimizer iterations, account separately for scoring the binary ensemble and fitting its calibrator: a conservative total is `C_cal = O(c n_cal T H_t a_lookup + c I_cal n_cal)`. Calibration does not consume held-out labels.

## Five families for unlabeled books

These inputs have no gold semantic labels. Distance, silhouette, noise rate, and reconstruction loss do not become accuracy through a complexity analysis. Shared text fitting and PCA are charged separately in the reports; adding them is necessary for an end-to-end cost.

| Family and actual operation | Fitting work after representation | One-query operation | Model/artifact slots | Additional fitting workspace |
| --- | --- | --- | --- | --- |
| K-means: ML.NET, raw vectors or PCA vectors | Conservative Lloyd-style sparse distance/update bound `O(I[kZ + n + kd + k^2 d]) + C_init`; dense version `O(I[nkd + k^2 d]) + C_init`. Actual acceleration can skip distances. | All centroid distances: `O(k(s + 1))`, with dense centroids and cached norms; dense `O(kd)`. | `O(kd + k)` centroids/norms. PCA variant uses `r` instead of `d`, plus its upstream projection. | Conservative allowance `O(nd + nk + kd + k^2)` for possible dense initialization candidates, assignments/bounds, and cluster work, plus any shared cache. Streaming/bounded initialization can use less. |
| Hierarchical clustering: custom average linkage on `m` PCA vectors | Initial Euclidean matrix `O(m^2 r)`; naive minimum-pair scan over merges `O(m^3)`; total `O(m^2 r + m^3)`. | No out-of-sample predictor is implemented. Sample silhouette assessment separately costs `O(m^2 r)`. | Private sampled vectors/assignments `O(mr + m)`. A full retained merge tree would add `O(m)`; it is not exported here. | `O(m^2 + mr)` for distance matrix, sampled vectors, active state, and potentially retained membership copies. |
| PCA: native randomized rank-`r` projection, followed by K-means | Conditional randomized sketch bound `O(t_p p Z + (n + d)p^2 + p^3)`. Dense substitution: `O(t_p ndp + (n + d)p^2 + p^3)`. Add K-means fitting on dense `n x r` outputs. | Projection `O(rs + r)` if centering is handled with a precomputed mean offset; conservative dense `O(dr)`. Add L2 normalization `O(r)` and K-means `O(kr)`. | `O(dr + d)` basis/mean, plus `O(kr)` centroids. | Conservative generic sketch bound `O((n + d)p + p^2)`; materializing projected rows additionally costs `O(nr)`. Native streaming can use less. |
| Autoencoder: scratch dense `r -> 64 -> z -> 64 -> r` | `O(E n C_f + E ceil(n/b) P)`, where `C_f = O(ra + az)` and `P = O(ra + az)`. Add training-only text/PCA fit and matrix transformation. | Numeric encode/reconstruct and loss: `O(ra + az)`; include upstream text/PCA projection for a new text. | In-memory network `O(P)`; weights are not exported/deployed by this experiment. | Dense Adam/gradient state `O(P)`, batch activations `O(b(r + a + z))`, and supplied matrices `O((n + q)r)`. Returned latent assessment vectors additionally use `O(qz)` and are omitted from public reports. |
| DBSCAN: custom Euclidean scans, epsilon `.45`, minPoints `5` including self | `O(m^2 r)` because every visited point can scan all sample vectors; queue operations are at most quadratic and hash membership is expected constant time. | No out-of-sample predictor is implemented. Sample silhouette assessment separately costs `O(m^2 r)`. | Private sampled vectors/assignments `O(mr + m)`. | `O(mr + m)` for vectors, visited/label arrays, one queue/hash set, and transient neighborhoods; no full distance/adjacency matrix is retained. |

ML.NET K-means has initialization and acceleration behavior beyond textbook Lloyd iteration, so `C_init` and skipped distances remain explicit. The configured 100 iterations do not include every initialization pass. The table is a conservative distance/update workload, not a guarantee that every iteration computes `nk` distances. [ML.NET K-means source](https://source.dot.net/Microsoft.ML.KMeansClustering/KMeansPlusPlusTrainer.cs.html).

ML.NET identifies its PCA as a randomized low-rank method. The PCA row gives a generic matrix-sketch bound under sparse matrix multiplication, implicit centering, and sketch width `p`; it does **not** assert a verified native pass count, power-iteration count, or exact temporary layout. If a library materializes centered dense data, use `nd` instead of `Z` and add its storage. This distinguishes truncated randomized projection from building a dense `d x d` covariance matrix and performing full eigendecomposition. [ML.NET PCA documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.ml.transforms.principalcomponentanalyzer?view=ml-dotnet-preview), [Halko, Martinsson and Tropp's primary randomized-decomposition paper](https://arxiv.org/abs/0909.4061).

Average linkage repeatedly scans all active pairs, using a precomputed matrix and size-weighted distance updates. This implementation has no nearest-neighbor-chain optimization. DBSCAN's `Neighbors` enumerates all sample points, with no spatial index. The original DBSCAN paper's favorable indexed average complexity therefore does not describe this implementation. Sample selection is deterministic over training rows only; producing ranks adds hashing and `O(n log n)` ordering outside the clustering core. [BookExploration.cs](../tools/Tym.Benchmark/BookExploration.cs), [original DBSCAN paper](https://cdn.aaai.org/KDD/1996/KDD96-037.pdf).

The autoencoder reconstructs L2-normalized PCA coordinates, not book prose. Its bottleneck is half the input width, capped at 32; the usual 32-coordinate input therefore compresses to 16. Mean and zero reconstruction baselines add `O((n + q)r)` work, and neither uses held-out data for fitting.

## Four scratch neural text-classification families

The following are compact C# TorchSharp models trained from random initialization. They are not pretrained BERT/GPT models or reproductions of the historical paper's CNN. Bounds retain dimensions as variables even though this experiment fixes them. For each row below:

`training = O(E n C_f + E ceil(n/b) P)`

The first term covers forward/backward operations; the second covers dense Adam parameter/state updates each batch. Add vocabulary/encoding work above. Prediction has `O(C_f)` work per row plus tokenization, encoding and batch/output handling. Fitting workspace is `O(P + C_a + nL)` after vocabulary construction; assessment also retains `O(qL)` encoded inputs. Parameter gradients and Adam's two moment arrays cost `O(P)`, even when an embedding looks up few vocabulary entries. [Official Adam documentation](https://docs.pytorch.org/docs/2.10/generated/torch.optim.Adam.html).

| Family and implemented architecture | Forward/prediction work per represented row `C_f` | Model slots `P` | Batch activations `C_a` |
| --- | --- | --- | --- |
| MLP: dense token-count bag of words, `V -> 64 -> c` | `O(Va + ac + V + L)`, including dense input construction/normalization. Sparse token counts do not make the dense linear layer sparse. | `O(Va + ac)` | `O(b(V + a + c))` |
| CNN: embedding, one width-3 Conv1d, masked global max, output | `O(Lweh + Lh + hc)` | `O(Ve + weh + hc)` | `O(bL(e + h) + bc)` |
| RNN: embedding, one unidirectional LSTM, last nonpadding hidden output | `O(L(eh + h^2) + hc)`; four gates are a constant factor. | `O(Ve + eh + h^2 + hc)` | `O(bL(e + h) + bc)` |
| Transformer: token/learned-position embeddings, one four-head encoder, masked mean, output | `O(Lh^2 + L^2h + LhF + hc)` | `O(Vh + Lh + h^2 + hF + hc)` | Conservative dense-attention bound `O(bAL^2 + bL(h + F) + bc)` |

CNN cost follows the actual kernel and channel products, rather than an image-convolution complexity for arbitrary image height/width. LSTM processes the full padded 64-position sequence; selecting the last nonpadding output does not skip those recurrent steps. [Conv1d operator documentation](https://docs.pytorch.org/docs/2.10/generated/torch.nn.Conv1d.html), [LSTM operator and gate equations](https://docs.pytorch.org/docs/2.10/generated/torch.nn.LSTM.html).

The Transformer includes attention projections, quadratic sequence attention, and feedforward layers. Quoting only `O(L^2h)` omits the other components. Its local encoder uses the sequence-first layout; no optimized attention-memory path is assumed. `O(bAL^2)` is a conservative conventional activation bound, not a measurement of the pinned LibTorch allocator. [Primary Transformer paper](https://arxiv.org/abs/1706.03762), [TransformerEncoderLayer documentation](https://docs.pytorch.org/docs/2.10/generated/torch.nn.TransformerEncoderLayer.html).

## How to interpret performance claims

- These tables cover all 17 families: eight classical classifiers, five book-exploration families, and four neural text classifiers. The 14 classification configurations and five book configurations total 19 variants; the majority-class reference is an additional baseline.
- Iteration caps, early convergence, objective evaluations, initialization, class count, native preprocessing, and calibration affect work. Neither `O(nd)` nor a fixed five epochs is a measured speed ranking.
- Dense worst-case bounds and sparse arithmetic should be shown together. Sparse input does not imply a sparse model, sparse optimizer, sparse histogram, or index-assisted neighbor search.
- Hierarchy and DBSCAN measurements describe a maximum 256-row training sample. Their assessment costs are within-sample statistics; their API has no unseen-document prediction step.
- Book shared transforms are fitted once and separately timed. Their cost must not be counted as zero when discussing an end-to-end pipeline, or repeatedly mistaken for independent refits in the combined tables.
- Classification includes task-specific given mentions. Its label accuracy does not measure finding spans in arbitrary prose. Unlabeled books have no semantic accuracy. Complexity supplies no evidence about either model's semantic quality.
- The recorded wall-clock results are one sequential hardware run. Process working-set samples include caches, loaded native libraries and earlier models; parameter slots, artifact bytes, and isolated model memory are different quantities.
- Timing boundaries are defined in code: ML.NET scoring warm-up is excluded; scratch text-network training includes vocabulary/model/optimization and prediction includes unseen-text encoding. Scratch network native runtime initialization and one training-input evaluation warm-up are excluded. Metadata token-retention scans and report/artifact serialization can add run-level work outside per-model timers.

## Implementation locations

The experiment's settings and operation structure are available in [AlgorithmCatalog.cs](../tools/Tym.Benchmark/AlgorithmCatalog.cs), [ClassificationTrial.cs](../tools/Tym.Benchmark/ClassificationTrial.cs), [LinearRegressionClassifier.cs](../tools/Tym.Benchmark/LinearRegressionClassifier.cs), [CustomClassifiers.cs](../tools/Tym.Benchmark/CustomClassifiers.cs), [BookExploration.cs](../tools/Tym.Benchmark/BookExploration.cs), [NeuralAlgorithms.cs](../tools/Tym.NeuralBenchmark/NeuralAlgorithms.cs), and [AutoencoderAlgorithms.cs](../tools/Tym.NeuralBenchmark/AutoencoderAlgorithms.cs). These local files determine the experiment being discussed; the cited primary sources explain the algorithms/operators and the limits of common generic claims.
