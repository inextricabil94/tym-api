# Continuation checkpoint — 2026-10-02

Saved for the user's request to preserve work on a separate GitHub branch before usage expires.

## Completed evidence

- Separate .NET 10 / ML.NET 5.0.0 solution and strict TypeScript UI source are under this project.
- Six structured TimeBank classifiers were trained, retaining ten baseline classifiers. Two book clustering models remain private.
- Latest recorded .NET results: 70 API tests, 31 modeling tests and 22 corpus/research tests passed (123 total).
- Public API deployment was verified: all 16 models loaded, scorable and checksum verified; all 32 original authored prediction snapshots matched; source spans and document validation passed. See `docs/results/corpus-api-public-*.json`.
- Public API: https://tym-corpus-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io
- Final public UI: https://tym-corpus-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io
- All 18 distinct UI checks passed: 16 local checks (nine synthetic flows and seven live integration flows, including five private Books.zip passages) and two public smoke checks. Private passage requests were restricted to localhost; public checks used original authored prose. See `docs/results/ui-test-summary.json` and `ui-local-test-summary.json`.
- Final public smoke checks confirmed the 16-model catalog, English REM, Romanian structured FUTURE, manual segment/mention/relation creation with complete provenance, validation and unchanged-source export on revision `tym-corpus-ui-serban--0000001`.
- Manually added segment, mention and relation provenance was repaired after independent contract review. Local real-API regression checks passed for manual creation and validation; edits record an explicit return to draft.
- The final 17-page PDF and 17-slide editable PowerPoint completed visual QA. They preserve the distinction between conditional diagnostics, prediction snapshots, historical CNN results and independent adjudicated evaluation.
- The local UI has been restored at `http://127.0.0.1:8791`, connected to the local API at `http://127.0.0.1:8871`.
- GitHub checkpoint commit `53b04a9` was successfully pushed on `codex/tym-corpus-research`. This release update contains the final deployment, browser and artifact evidence.
- The published PDF and PowerPoint returned their expected content types and exact SHA-256 digests. See `docs/results/published-artifacts.json`.
- The PowerPoint was downloaded from the public app and saved in the user's Downloads directory with the same verified digest.

## Release evidence

The final UI Docker image is `tym-corpus-ui:research-20261002-v2`, built remotely by ACR run `ca7`; the API remains `tym-corpus-api:research-20261002-v1`. `docs/results/final-release.json` records image identity, verification counts, URLs and artifact delivery. Private raw corpora, trained weights and detailed test outputs remain in the local private research directory; rerunning private integration requires the original attachments and model paths.

The work is exploratory. Provided annotations have unknown adjudication status; unannotated books and prediction regressions are not independent literary accuracy evidence. Private corpora, imported prose, model weights, test traces and credentials are excluded from GitHub.
