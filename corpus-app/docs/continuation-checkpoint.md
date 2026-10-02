# Continuation checkpoint — 2026-10-02

Saved for the user's request to preserve work on a separate GitHub branch before usage expires.

## Completed evidence

- Separate .NET 10 / ML.NET 5.0.0 solution and strict TypeScript UI source are under this project.
- Six structured TimeBank classifiers were trained, retaining ten baseline classifiers. Two book clustering models remain private.
- Latest recorded .NET results: 70 API tests, 31 modeling tests and 22 corpus/research tests passed (123 total).
- Public API deployment was verified: all 16 models loaded, scorable and checksum verified; all 32 original authored prediction snapshots matched; source spans and document validation passed. See `docs/results/corpus-api-public-*.json`.
- Public API: https://tym-corpus-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io
- Initial public UI: https://tym-corpus-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io
- Initial public UI browser checks confirmed the 16-model catalog, English REM, Romanian structured FUTURE and document analysis/validation.
- Local UI checks previously passed eight synthetic flows and six live integration flows, including five private Books.zip samples. Later added tests need their final result captured.
- Manually added annotation provenance was repaired in TypeScript after independent contract review. This repair still needs the final deployment and verification recorded.

## Remaining work

1. Capture final results for the current 18 UI tests, including manual annotation creation and two public smoke tests. Private book tests must stay on localhost.
2. Finalize the updated PDF and PowerPoint from the already prepared, visually inspected private drafts. Save the PowerPoint locally, including Downloads if permitted.
3. Deploy the final UI source and report/presentation assets; verify downloads and the repaired manual annotation flow.
4. Record final deployment/artifact evidence, commit and push the final update.

The work is exploratory. Provided annotations have unknown adjudication status; unannotated books and prediction regressions are not independent literary accuracy evidence. Private corpora, imported prose, model weights, test traces and credentials are excluded from GitHub.
