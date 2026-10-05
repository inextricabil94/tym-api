# Integration workflow results — 5 October 2026

**All 20 requested integration cases passed:** ten prediction workflows and ten book experiments. The [illustrated report](./integration-workflows-report/index.html) gives each case a picture, its observed output, explanatory text and passing assertions. The [aggregate JSON evidence](./integration-workflows-report/workflow-evidence.json) contains the measured results and provenance hashes without private book passages, document identifiers or filesystem paths.

Prediction pictures are screenshots from the actual tested localhost interface using original authored inputs. Book pictures are explanatory pipeline diagrams generated from successful aggregate evidence; they are not screenshots of a book prediction interface.

## Execution summary

| Execution | Result | Elapsed suite time |
| --- | --- | ---: |
| Real TypeScript UI → HTTP API → existing ML.NET models | 10 passed; all 16 model identities exercised | 16.4 s |
| Fresh C# book pipeline integrations | 10 passed; five algorithms in each language | 2 min 22 s |
| .NET solution regression run | 108 passed; 7 private opt-in cases skipped | Not reported here |
| Local UI regression run | 14 passed | 29.9 s |

The regression counts are separate from the 20 new integration cases. Skipped private cases were not counted as passing tests. Suite times include orchestration and assertions; they are not individual model training or inference benchmarks.

## Ten prediction workflows

Each case uses live HTTP requests with the real private ML.NET bundles. Checks cover localhost boundaries, readiness and integrity, task/language identity, manifest label inventory, the exact submitted input, and agreement between the direct API response, browser HTTP response, displayed label and response JSON. The six TimeBank cases exercise both the lexical baseline and structured variant within one case each, so ten cases cover all sixteen deployed model identities.

| Workflow | Lexical observed label | Structured observed label | Input supplied to the model |
| --- | --- | --- | --- |
| [English narrative segment](./integration-workflows-report/index.html#prediction-segment-en) | `REM` | N/A | One narrative segment |
| [Romanian narrative segment](./integration-workflows-report/index.html#prediction-segment-ro) | `NAR` | N/A | One narrative segment |
| [English segment relation](./integration-workflows-report/index.html#prediction-relation-en) | `AFTER` | N/A | Two ordered segments and relation cue |
| [Romanian segment relation](./integration-workflows-report/index.html#prediction-relation-ro) | `SIMULTANEOUS` | N/A | Two ordered segments and relation cue |
| [Romanian event class](./integration-workflows-report/index.html#prediction-event-class) | `OCCURRENCE` | `OCCURRENCE` | Marked event and context |
| [Romanian event tense](./integration-workflows-report/index.html#prediction-event-tense) | `FUTURE` | `FUTURE` | Marked event and context |
| [Romanian time-expression type](./integration-workflows-report/index.html#prediction-timex-type) | `DATE` | `DATE` | Marked time expression and context |
| [Romanian temporal link](./integration-workflows-report/index.html#prediction-tlink) | `AFTER` | `BEFORE` | Ordered mentions, contexts and signal |
| [Romanian subordination link](./integration-workflows-report/index.html#prediction-slink) | `MODAL` | `MODAL` | Ordered event mentions, contexts and signal |
| [Romanian aspectual link](./integration-workflows-report/index.html#prediction-alink) | `INITIATES` | `INITIATES` | Ordered event mentions, contexts and signal |

These labels record the software's observed behavior on original authored examples. The temporal-link models disagree on the same input; English and Romanian segment outputs also differ. Neither agreement nor disagreement establishes correctness without adjudicated gold labels. Passing requires each variant's UI and API output to agree with that same variant, not with another model.

The API exposes labels and provenance metadata, without class scores or probabilities. Score checks and semantic accuracy are therefore unavailable. TimeBank checks are conditional on supplied mentions and endpoints; they do not evaluate span detection, complete document extraction or timeline reconstruction.

## Ten book experiments

Romanian inputs contain 5,213 passages from 14 supplied document groups: 4,037 training passages across 12 groups and 1,176 heldout passages across two groups. English inputs contain 446 passages from one source work: 356 training and 90 heldout passages. Seed-42 split assignments are independently reproduced, and normalized duplicate passages cannot cross either holdout. The English split measures within-work passage behavior, not generalization to a different book.

| Workflow | Training / heldout passages | Feature width | Observed metric and scope | Fit seconds | Timing scope |
| --- | ---: | ---: | --- | ---: | --- |
| [Romanian KMeans](./integration-workflows-report/index.html#book_ro_mlnet_kmeans) | 4,037 / 1,176 | 706,013 | Heldout mean squared centroid distance: **0.417928** | 44.844 | K |
| [English KMeans](./integration-workflows-report/index.html#book_en_mlnet_kmeans) | 356 / 90 | 80,599 | Heldout mean squared centroid distance: **0.391954** | 3.816 | K |
| [Romanian PCA + KMeans](./integration-workflows-report/index.html#book_ro_mlnet_pca_kmeans) | 4,037 / 1,176 | 32 | Heldout mean squared centroid distance: **0.854288** | 49.702 | K |
| [English PCA + KMeans](./integration-workflows-report/index.html#book_en_mlnet_pca_kmeans) | 356 / 90 | 32 | Heldout mean squared centroid distance: **0.681986** | 6.711 | K |
| [Romanian average-linkage hierarchy](./integration-workflows-report/index.html#book_ro_csharp_average_linkage_hierarchical) | 4,037 / 1,176 | 32 | 256 training-sample points; 8 clusters; silhouette: **0.066369** | 0.012 | S |
| [English average-linkage hierarchy](./integration-workflows-report/index.html#book_en_csharp_average_linkage_hierarchical) | 356 / 90 | 32 | 256 training-sample points; 8 clusters; silhouette: **0.061457** | 0.037 | S |
| [Romanian DBSCAN](./integration-workflows-report/index.html#book_ro_csharp_dbscan) | 4,037 / 1,176 | 32 | 256 training-sample points; **100% noise**; silhouette unavailable | 0.007 | S |
| [English DBSCAN](./integration-workflows-report/index.html#book_en_csharp_dbscan) | 356 / 90 | 32 | 256 training-sample points; **100% noise**; silhouette unavailable | 0.023 | S |
| [Romanian numeric autoencoder](./integration-workflows-report/index.html#book_ro_csharp_torchsharp_autoencoder) | 4,037 / 1,176 | 32 → 16 → 32 | Heldout reconstruction MSE: **0.008020** | 6.139 | A |
| [English numeric autoencoder](./integration-workflows-report/index.html#book_en_csharp_torchsharp_autoencoder) | 356 / 90 | 32 → 16 → 32 | Heldout reconstruction MSE: **0.025232** | 0.910 | A |

Fit times are rounded observations from this integration execution:

- **K:** `fit_seconds_including_shared_featurizer`, including the shared text featurizer and that pipeline's representation/centroid fitting. PCA + KMeans also includes its PCA fit. Assessment, export and assertions are excluded. The shared text cost appears in both pipeline figures and must not be added twice when estimating a language run.
- **S:** `fit_seconds_algorithm_only` on the deterministic 256-point training sample. Text/PCA fitting, sample transformation, geometry assessment and artifact writing are excluded. Hierarchy and DBSCAN do not use the heldout passages or provide an inductive heldout classifier here.
- **A:** `fit_seconds_algorithm_only` for scratch TorchSharp model creation and five epochs of optimization. Shared text/PCA fitting, feature transformation and heldout reconstruction are excluded; the trainer excludes native initialization and one training-input warmup.

All five algorithms are freshly fitted once per language in a shared collection fixture. The complete language runs, including independent split/vocabulary verification, took **121.644 s for Romanian** and **15.972 s for English**. Ten passing cases therefore represent two shared language executions, not ten isolated training runs. These measurements do not replace the published algorithm comparison benchmark.

KMeans tests reload each saved private pipeline twice, verify its manifest and checksum, and score an original authored query with finite cluster distances. Its text-feature vector must match an independently fitted training-only vocabulary. Hierarchy and DBSCAN checks verify finite sample vectors, selection hashes, assignment counts and algorithm settings. Autoencoder checks verify five finite epoch losses, finite reconstruction and baseline metrics, dimensions and fitting metadata.

Cluster IDs are arbitrary lexical groups. Centroid distances belong to different feature spaces, so the raw and PCA distances cannot be treated as an accuracy ranking. With DBSCAN epsilon 0.45 and minPoints 5, every sampled point was classified as noise in this run; that is a valid recorded outcome, not evidence of semantic success. Autoencoder heldout MSE describes numeric reconstruction, without a prose decoder. Romanian zero/training-mean baseline MSEs were **0.031250 / 0.031065**; English baselines were **0.031250 / 0.031259**. Lower reconstruction error on these inputs does not establish NLP classification accuracy.

## Reproduce the integration evidence

Use the [prediction workflow setup](./prediction-integration-workflows.md) to start the real local API with its sixteen existing private weights and build the TypeScript UI. From `src/Tym.Corpus.Ui`, enable the opt-in suite and choose a new evidence directory:

```powershell
$env:TYM_RUN_WORKFLOW_INTEGRATION = '1'
$env:TYM_UI_TEST_API_URL = 'http://127.0.0.1:8871'
$env:TYM_WORKFLOW_EVIDENCE_DIR = '<new local evidence directory>'
npm run test:integration:predictions
```

Follow the [book workflow setup](./book-integration-workflows.md) for the two supplied prepared JSONL inputs and their pinned hashes. From the project root, use the same new evidence directory:

```powershell
$env:TYM_PRIVATE_BOOK_RO_JSONL = '<supplied Romanian prepared JSONL>'
$env:TYM_PRIVATE_BOOK_EN_JSONL = '<supplied English prepared JSONL>'
dotnet test tests/Tym.Research.Tests/Tym.Research.Tests.csproj -c Release --filter FullyQualifiedName~PrivateBookWorkflowIntegrationTests
```

The [report builder](../tools/Tym.WorkflowReport/build-workflow-report.mjs) assembles pictures and text from twenty successful records. It uses Node only for document generation; model fitting remains in C# with ML.NET/TorchSharp. Supply a document dependency runtime containing `pdf-lib` and `sharp`:

```powershell
node tools/Tym.WorkflowReport/build-workflow-report.mjs --evidence '<new local evidence directory>' --out '<illustrated report directory>' --pdf '<PDF output file>' --runtime '<document dependency runtime>'
```

## Interpretation limits

- The twenty results establish connected software behavior and traceability. Prediction examples are original authored prose, not semantic gold. Book passages are unlabeled, so semantic accuracy and F1 remain unavailable.
- Split assignments and the training-only text vocabulary are independently checked. The PCA fitting boundary and autoencoder no-validation-fit contract are reviewed in the source path and metadata; these tests do not independently recompute every PCA basis or prove every learned transform's nonleakage.
- Memory figures describe the shared process, including prior pipelines/native state, rather than isolated algorithm memory. Private artifact size measures a compressed ML.NET pipeline or transductive sample JSON. Autoencoder parameter bytes exclude gradients, optimizer state and activations.
- No experimental book model is promoted by these tests. Temporary private weights are cleaned up after the collection; public evidence contains aggregate measurements, hashes, original authored examples and pictures without private corpus prose.
