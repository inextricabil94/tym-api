# Project Instructions

## Research scope

- This repository implements an interpretable TYM baseline with deterministic rules and ML.NET seed classifiers. Do not describe the seed classifiers as the CNN evaluated in the paper or as models trained on the annotated TYM corpus.
- Keep reported evidence separate: the paper reports 58.9% accuracy and approximately 58.7% macro F1 on 2,000 event instances; this API and its framework POCs do not reproduce those results.
- Treat transformer encoders, sequence segmentation, pairwise relation models, schema-constrained LLM extraction, coreference resolution, speaker attribution, and human review as proposed work until implemented and evaluated.
- The current perspective assumption is a single author/narrator (`author` in English and `autor` in Romanian). Preserve that assumption unless a change includes an explicit model and evaluation plan.

## Extraction and provenance

- Keep source text offsets, TimeML-style annotations, TYM events/segments/tracks, JSON, XML, and SVG output consistent.
- Every carried or inferred event actor, temporal anchor, or relation must identify its method and a short evidence note. Do not label actor carry-forward as coreference resolution.
- When changing the response model, update `openapi.yaml`, the README, and the checked-in sample outputs where appropriate.
- Keep rendering deterministic and keep optional NLP components separate from the core API path.

## Evaluation and data

- The tiny sample POCs measure component availability, counts, and runtime. Do not present those measurements as accuracy or generalization results.
- For model evaluation, report accuracy, macro F1, per-class precision and recall, TS boundary scores, and temporal-relation accuracy. Prefer chapter/document-level splits to random event-level splits to reduce context leakage.
- The checked-in seed examples are synthetic demo inputs, not gold labels. Keep human annotations private unless publication is permitted; split gold data by chapter/document before creating synthetic descendants, and keep descendants in training only.
- `pocs/model-evaluation` currently computes classification metrics only. Do not claim it evaluates event-span boundaries or end-to-end temporal graph consistency.
- Preserve annotated corpus files and their licensing/privacy constraints. Do not add corpus text to commits without confirming it is intended for publication.

## Useful commands

- Build the solution from the repository root with `dotnet build Tym.Api.sln`.
- Run the small framework comparison from `pocs/nlp-framework-comparison` with `python nlp_poc_runner.py`.
