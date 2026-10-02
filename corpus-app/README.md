# TYM Corpus Explorer

A separate .NET 10 application for English/Romanian narrative annotation review and Romanian TimeBank classification experiments. Training, corpus preparation and inference use C# and ML.NET 5.0.0. The browser interface is strict TypeScript compiled to local assets, with no external script/CDN dependency. The original `tym-api` diagram application remains a legacy project.

The catalog contains sixteen classifiers: ten whole-text baselines and six structured TimeBank variants preserving target mentions and directed endpoint channels. Two private eight-cluster K-Means models explore unlabeled books; their cluster IDs are not TYM labels and those weights are excluded from deployment.

Read [the architecture review](docs/architecture-review.md) for source papers, licenses, implementation scope and evaluation decisions, and [the session record](docs/research-session.md) for the run history. [Aggregate results](docs/results/) contain counts, metrics and model metadata without book prose.

## Requirements and build

Use the .NET 10 SDK, Node.js/npm for the frontend, and Docker or Azure CLI for deployment. The committed package lock pins TypeScript 5.9.3 and the browser-test dependencies.

From this project's directory:

```powershell
dotnet build Tym.Corpus.sln
Push-Location src/Tym.Corpus.Ui
npm ci
npm run typecheck
npm run build
Pop-Location
```

TypeScript builds `client/app.ts` and `client/workbench.ts` into `wwwroot/app.js` with strict checking and `noEmitOnError`. Build the frontend before launching the UI or preparing deployment contexts.

## Run locally

Start the API in one terminal:

```powershell
$env:TYM_CORPUS_MODEL_DIR = 'C:\private\tym\models'
$env:TYM_CORS_ORIGINS = 'http://127.0.0.1:8791'
dotnet run --project src/Tym.Corpus.Api --urls http://127.0.0.1:8871
```

Start the UI in another:

```powershell
$env:TYM_API_BASE_URL = 'http://127.0.0.1:8871'
dotnet run --project src/Tym.Corpus.Ui --urls http://127.0.0.1:8791
```

Open the local UI at [127.0.0.1:8791](http://127.0.0.1:8791). The API model directory contains all sixteen `<model_id>_sdca.zip` files and matching `.manifest.json` files. Configure `TYM_CORS_ORIGINS` as a comma-separated allowed-origin list for a restricted deployment; its current empty default permits any browser origin.

## API and document review

| Endpoint | Behavior |
| --- | --- |
| `GET /health` | Cheap process liveness |
| `GET /ready` | Load/scoring readiness for every expected classifier; HTTP 200 when ready, otherwise 503 |
| `GET /v1/models` | Task guidance, metadata, installed/loaded/scorable state and artifact identity/integrity |
| `POST /v1/predictions` | `model_id` and `text`; task-specific explicit formats for structured variants |
| `POST /v1/documents/analyze` | `text`, `language` (en/ro), optional explicit `document_date` in yyyy-MM-dd |
| `POST /v1/documents/validate` | Edited document; HTTP 200 with `valid`, diagnostics and supported partial-order results |

The workbench preserves submitted text, SHA-256 and half-open UTF-16 offsets. It drafts sentence boundaries, lexical TIME/ACTOR/LOCATION/EVENT candidates and segment-mode classifier labels. Review edits retain field provenance. Source adjacency creates no story-order relation. Only reviewed supplied precedence/equivalence edges appear in story order; unknown order remains unknown.

Supported absolute dates can normalize directly. Relative day terms require an explicit document date, which remains a lexical hypothesis in fiction. The validator checks structure, exact source slices, typed endpoints, strict-precedence cycles and supported simultaneous/identity contradictions. It does not perform full Allen reasoning, coreference, perspective/actor identity resolution or causal inference. Reviewed annotations are not automatically gold.

The analysis limits are 50,000 UTF-16 code units, 500 sentence drafts and 2,000 mention candidates; candidate truncation preserves complete source with warnings. Validation caps the annotation total at 10,000. Models are trusted immutable deployment inputs: present digest/identity mismatches refuse inference, missing metadata remains explicit and unverified, successful bundles are cached, and failed bundles retry after a bounded delay. A nonempty scoring probe does not establish NLP accuracy.

Original sample request:

```powershell
$analysisBody = @{
    text = 'Ieri Mara a ajuns în Brașov. Mâine va pleca.'
    language = 'ro'
    document_date = '2026-10-02'
} | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri 'http://127.0.0.1:8871/v1/documents/analyze' -ContentType 'application/json' -Body $analysisBody
```

## Private corpus preparation

Keep raw books, converted JSONL, private weights and build contexts outside Git. These commands read attachments as data and do not execute attached code:

```powershell
dotnet run --project tools/Tym.Corpus.Data -- tym-xml --romanian C:\private\tym\hln_ro.xml --english C:\private\tym\motiw_en.xml --out C:\private\tym\tym.jsonl --report C:\private\tym\tym-import.json
dotnet run --project tools/Tym.Corpus.Data -- timebank --zip C:\private\tym\Ro-TimeBank.zip --exclusions tools/Tym.Modeling/ro-timebank-exclusions.json --out C:\private\tym\timebank.jsonl --report C:\private\tym\timebank-import.json
dotnet run --project tools/Tym.Corpus.Data -- books --zip C:\private\tym\Books.zip --romanian C:\private\tym\Harta.txt --english C:\private\tym\Map.txt --out C:\private\tym\books-ro.jsonl --en-out C:\private\tym\books-en.jsonl --report C:\private\tym\books-import.json
```

See [the data tool](tools/Tym.Corpus.Data/README.md) for archive/XML protections, encoding, grouping and private book samples. Feature preparation normalizes Unicode/whitespace; it is separate from the workbench's unchanged-source coordinate layer. Book chunking and browser sample limits preserve complete UTF-16 surrogate pairs at passage boundaries.

The supplied TYM pair produces 1,515 task rows and represents one parallel source work. The TimeBank run retains 157 of 183 documents and 20,151 conditional task rows. Raw books yield 5,213 Romanian passages from 26 files/14 document groups and 446 English passages from one file/group. Raw books have no supervised labels, and separately supplied editions require offset reconciliation.

## Training and diagnostic comparison

The baseline CLI accepts labeled JSONL while retaining provenance:

```powershell
dotnet run --project tools/Tym.Modeling -- train --data C:\private\tym\tym.jsonl --task segment_type --language en --model-out C:\private\tym\models\segment_type_en_sdca.zip
dotnet run --project tools/Tym.Modeling -- predict --model C:\private\tym\models\segment_type_en_sdca.zip --data C:\private\tym\original-free-text.jsonl --report C:\private\tym\predictions.json
dotnet run --project tools/Tym.Modeling -- cluster --data C:\private\tym\books-ro.jsonl --language ro --clusters 8 --model-out C:\private\tym\books_ro_clusters_k8.zip --assignments-out C:\private\tym\book-clusters.jsonl --report C:\private\tym\book-clusters-report.json
```

Repeat baseline training for English/Romanian `segment_type` and `temporal_relation`, plus Romanian `timebank_event_class`, `timebank_event_tense`, `timebank_timex_type`, `timebank_tlink`, `timebank_slink` and `timebank_alink`. Artifact names are the catalog's model ID plus `_sdca.zip`.

The separate diagnostic tool compares majority, whole-text SDCA/L-BFGS and structured SDCA:

```powershell
dotnet run --project tools/Tym.Benchmark -- --data C:\private\tym\timebank.jsonl --out C:\private\tym\grouped-benchmark.json --folds 3 --seed 42
dotnet run --project tools/Tym.Benchmark -- --mode train-structured --data C:\private\tym\timebank.jsonl --out C:\private\tym\structured-training.json --models C:\private\tym\models --seed 42
```

Structured training refuses existing target artifact names. Use a directory without the six structured model files, or choose a new directory. See [the benchmark README](tools/Tym.Benchmark/README.md) for the split/metric protocol and input format.

Annotations retain `provided_annotation/adjudication_unknown`. Grouped diagnostics join duplicate normalized inputs before splitting documents. Structured feature gains are exploratory paired comparisons on the same folds, conditional on supplied mentions/endpoints. They do not measure end-to-end spans/graphs or independent literary accuracy. The paper's historical CNN scores remain separate. The `Tym.Modeling evaluate` command only accepts `human_gold/adjudicated` data with grouped partitions; provided annotations cannot be silently promoted.

## Tests

```powershell
dotnet test Tym.Corpus.sln
```

Self-contained tests use original authored fixtures. To enable private corpus prediction snapshots and book integration:

```powershell
$env:TYM_PRIVATE_MODEL_DIR = 'C:\private\tym\models'
$env:TYM_PRIVATE_BOOKS_ZIP = 'C:\private\tym\Books.zip'
dotnet test Tym.Corpus.sln
```

At the current .NET verification checkpoint, 70 API, 31 baseline modeling and 22 research checks passed (123 total, including enabled private integration). Counts are software checks, not accuracy scores. Missing private variables skip their gated tests. Raw archive excerpts stay in private temporary test inputs.

Frontend commands:

```powershell
Push-Location src/Tym.Corpus.Ui
npm run typecheck
npm run build
npm run test:ui
Pop-Location
```

Browser tests are a separate verification step. Private book browser checks require the harness's private sample configuration and must not publish excerpts, screenshots or traces containing book prose.

## Docker and Azure CLI

Stage a new empty context directory using explicit allowlists and verify all sixteen classifier digests:

```powershell
./deploy/Prepare-Context.ps1 -ModelsPath C:\private\tym\models -OutPath C:\private\tym\docker-context-new -PdfPath C:\private\tym\report.pdf -PptxPath C:\private\tym\slides.pptx
```

The optional PDF/PPTX inputs become UI `/results/` downloads. Corpus text/JSONL, private clustering weights, tests, Git metadata and browser captures are excluded. Inspect the generated context inventory before remote building.

The deployment script uses Azure CLI's ACR remote Docker builder and separate API/UI Container Apps:

```powershell
./deploy/Deploy-Azure.ps1 -SubscriptionId 'YOUR-SUBSCRIPTION-ID' -ResourceGroup 'YOUR-RESOURCE-GROUP' -EnvironmentName 'YOUR-CONTAINER-APP-ENVIRONMENT' -RegistryName 'YOUR-ACR-NAME' -ContextPath C:\private\tym\docker-context-new -ImageTag 'research-20261002' -ReportPath C:\private\tym\deployment.json
```

API containers use `TYM_CORPUS_MODEL_DIR=/app/models`; UI containers receive the API's HTTPS URL in `TYM_API_BASE_URL`. Deployment reporting distinguishes provisioning from subsequent external readiness/prediction/browser verification. No new public URL is claimed by this README before that verification.

