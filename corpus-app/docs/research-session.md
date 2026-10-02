# Research session

Requested duration: three hours, starting 2026-10-02 04:03:47 UTC and targeting completion at 07:03:47 UTC.

## Scope

Review the four supplied research papers, the existing TYM implementation and primary GitHub examples. Choose an architecture appropriate to the available Romanian/English annotations and unlabeled books. Implement the separate application, measure results without confusing regression tests with NLP accuracy, publish the application to Azure, and update the report and presentation.

## Evidence rules

- Existing annotations retain `provided_annotation/adjudication_unknown` provenance.
- Source works and parallel translations must remain together when splitting data.
- Historical paper results must remain distinct from current ML.NET measurements.
- Retrieved GitHub examples inform implementation only after source and licensing review.
- Corpus prose, raw passages and private clustering weights remain outside public deployment.
- Every implemented capability must identify whether it uses rules, trained classifiers, or reviewed human annotations.

## Record

- 04:03 UTC: began research task; delegated independent architecture, paper synthesis and UI review.
- Existing baseline: ten supervised ML.NET classifiers, two private unlabeled clustering models, 20 corpus prediction snapshots, and 24 independent API component tests.

- .NET 10 migration completed for the separate project; ML.NET 5.0.0 training and inference use C#.
- C# corpus preparation reproduced all 1,515 TYM and 20,151 retained TimeBank task rows, labels and feature texts from the earlier import. Duplicate XML editions were checked and deduplicated.
- Books preparation: 5,213 Romanian passages from 26 files/14 groups, plus 446 English passages from one source. Raw books remain unlabeled; two private eight-cluster ML.NET models support exploration.
- TimeBank diagnostic: three folds over 50 connected document components after joining exact normalized input duplicates across 157 documents. Compared majority, whole-text SDCA, L-BFGS and exploratory structured SDCA. Structured channels improved every measured task; TLINK macro-F1 remains 22.77% and needs substantial further work.
- Trained six full-data structured classifiers as separate IDs, retaining ten baseline classifiers. No training-set accuracy estimate was substituted for grouped validation.
- API: source-preserving UTF-16 annotation drafts, field provenance, human correction/review, independent relation inventories and partial-order consistency checks. No automatic adjacent-event chronology or causal claims.
- 11:11:49 UTC: new public Azure API verified. All 16 models pass integrity/scoring readiness; 32 original authored prediction snapshots matched; source/span preservation and document validation passed. No private book prose was sent to the public endpoint.
- New branch: codex/tym-corpus-research in the original repository and separate project. Checkpoint 53b04a9 preserved the standalone source on GitHub.
- Final verification: 123 .NET tests and 18 distinct browser checks passed. Five private book passages stayed on localhost. The final public revision additionally passed manual annotation creation, reviewed graph validation and unchanged-source export.
- 19:52 UTC: final UI v2 published through Azure CLI/ACR Docker build ca7. Both the 17-page PDF and 17-slide editable PowerPoint downloaded with their expected MIME types and SHA-256 digests; the PowerPoint was also saved locally in Downloads. See docs/results/final-release.json and published-artifacts.json.
