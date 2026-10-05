# Real prediction integration workflows

The opt-in Playwright suite contains **ten integration cases**, one for each prediction workflow. It drives the TypeScript UI and the real HTTP API connected to the sixteen private ML.NET model bundles. The six TimeBank cases each exercise the lexical baseline and the structured variant within the same test, covering all sixteen model identities.

## Coverage

| Case ID | Workflow | Models exercised | What the input supplies |
| --- | --- | --- | --- |
| `prediction-segment-en` | English narrative segment | `segment_type_en` | One English narrative segment |
| `prediction-segment-ro` | Romanian narrative segment | `segment_type_ro` | One Romanian narrative segment |
| `prediction-relation-en` | English segment relation | `temporal_relation_en` | Ordered segments and relation cue |
| `prediction-relation-ro` | Romanian segment relation | `temporal_relation_ro` | Ordered segments and relation cue |
| `prediction-event-class` | Romanian event class | `timebank_event_class_ro` and `_structured` | Marked event and context |
| `prediction-event-tense` | Romanian event tense | `timebank_event_tense_ro` and `_structured` | Marked event and context |
| `prediction-timex-type` | Romanian temporal expression type | `timebank_timex_type_ro` and `_structured` | Marked temporal expression and context |
| `prediction-tlink` | Romanian temporal link | `timebank_tlink_ro` and `_structured` | Ordered source/target mentions, contexts and signal |
| `prediction-slink` | Romanian subordination link | `timebank_slink_ro` and `_structured` | Ordered source/target event mentions, contexts and signal |
| `prediction-alink` | Romanian aspectual link | `timebank_alink_ro` and `_structured` | Ordered source/target event mentions, contexts and signal |

## Assertions

Every test requires a localhost UI and API before sending any input. Its browser request firewall permits only the configured local UI/API origins and continues real requests; it does not fulfill or mock catalog, prediction or document responses.

Each case checks live `/ready` and `/v1/models` responses, all sixteen ready bundles, installed and scorable model state, nonempty unique label inventory, positive training metadata, and the selected task/language identity. Integrity, identity and readiness must be `verified`, `verified` and `ready_verified`; the catalog must report a SHA-256 model digest. The TimeBank structured model must report `structured_target_v1`.

The test then scores the original authored input directly through the HTTP API and through the browser form. It checks the exact browser request payload, inventory membership of the predicted label, verified response metadata, agreement between both HTTP responses, the displayed label and the rendered response JSON. The independent-accuracy limitation must remain visible.

The current prediction API returns labels and metadata. It does **not** return class scores or probabilities. The evidence therefore records `scores_exposed: false` and `score_count: null`, and does not invent probability or finite-score assertions.

## Run locally

Start the real API with `TYM_CORPUS_MODEL_DIR` pointing to the existing private directory containing the sixteen classifier weights and manifests. The tests do not retrain or replace models. Build the UI first; the existing Playwright web-server configuration can start its release DLL on port 8891.

From `src/Tym.Corpus.Ui` in PowerShell:

```powershell
$env:TYM_RUN_WORKFLOW_INTEGRATION = '1'
$env:TYM_UI_TEST_API_URL = 'http://127.0.0.1:8871'
# Optional output directory for per-case pictures and machine-readable evidence.
$env:TYM_WORKFLOW_EVIDENCE_DIR = '<local evidence directory>'
npm run test:integration:predictions
```

An explicit `TYM_PUBLIC_UI_URL` pointing to Azure causes these tests to fail closed before navigation. Without `TYM_RUN_WORKFLOW_INTEGRATION=1`, the ten cases are skipped rather than silently substituting demo models.

## Pictures and result summaries

With `TYM_WORKFLOW_EVIDENCE_DIR` configured, each successful case writes `predictions/<case-id>.png` and `predictions/<case-id>.json`. The picture captures the selected model, authored input, prediction and explanatory limitation in the real prediction workbench. For a TimeBank case, the picture shows the structured variant and the JSON records both variants.

The JSON includes the observed labels, inventories, model digests, verification states, assertion descriptions, exact authored input and explanation. It excludes private filesystem paths and training corpus prose. Single local API/browser round trips are recorded as diagnostic timings, not benchmark averages or isolated inference latency.

These tests assess integration and repeatable program behavior. Their examples are original authored prose, not passages from `Books.zip`; label membership and UI/API agreement do not establish semantic accuracy. TimeBank inputs provide the intended mentions and endpoints, so the checks do not evaluate span detection, independent book accuracy, graph completion or complete document extraction.
