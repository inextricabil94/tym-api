# ML.NET Corpus Explorer improvement plan

## Baseline and scope

The separately reviewed `tym-corpus-app` is a .NET/ML.NET application with a TypeScript workbench, sixteen catalog classifiers, source-preserving document drafts, explicit reference-date handling, manual edits/provenance, a supported partial-order validator, artifact integrity/readiness checks, and diagnostic algorithm comparisons. Keep those implemented protections.

Recorded integration reports distinguish passing workflows from NLP quality. The conditional TimeBank experiments supply mention spans and endpoints; they do not evaluate their discovery in novels. The parallel TYM XML represents one source work, annotations retain unknown adjudication status, and raw book clusters have no semantic gold. This plan is stored in the diagram repository; it does not modify or retrain the separate application.

## Prioritized backlog

| ID | Priority | Improvement | Acceptance condition |
| --- | --- | --- | --- |
| C1 | P0 | Expand the corpus registry | Register all supplied files with byte hashes, explicit decoding, language, source-work/translation/volume/edition identity, and annotation status. Reconcile the new PDF/text edition before reusing offsets. No prose or private weights enter a public report. |
| C2 | P0 | Independent annotation pilot | Sample several independent works by language and phenomenon; define mention, event-instance, TS, actor, and relation guidelines. Double-annotate a pilot, report agreement by task, adjudicate disagreements, and retain reviewer history. |
| C3 | P0 | Leakage-resistant splits | Lock work/document groups and duplicate components before feature fitting or augmentation. Keep translations and volumes together; all learned preprocessing is training-only. Test cross-work and within-work behavior separately. |
| C4 | P1 | Strong task contracts | Keep TYM and TimeML labels separate. Expose supported tasks, input units, directed endpoints, language, unknown/no-link policy, model and schema version. Reject whole-book text passed to a classifier that expects one marked mention/pair. |
| C5 | P1 | Context and features | Extend lexical baselines with target boundaries, directed endpoint/context channels, morphology, syntax, temporal cues, and actor continuity. Compare richer non-LLM representations only with pinned model/tokenizer identity and verified source alignment. |
| C6 | P1 | End-to-end extraction | Add separate event/TIMEX/actor candidate detectors and TS-boundary inference. Measure candidate recall and downstream errors instead of reporting supplied-mention scores as complete document understanding. |
| C7 | P1 | Model evaluation and selection | Keep model-selection folds separate from a locked test set; compare majority/rules/lexical/structured candidates, use per-class support/confusion matrices, macro F1, and document-level uncertainty estimates. Label retrospective comparisons as diagnostics. |
| C8 | P1 | Human review workflow | Persist versioned source hashes, offsets, typed annotations, edits, provenance, and review/adjudication status. Support correction/undo and disagreements; export cannot silently promote a model draft to gold. |
| C9 | P2 | Useful error analysis | Filter by rare labels, language, work, pair distance, disagreements, source artifact, and model version. Offer review queues based on disagreement/coverage; treat model scores as scores until calibrated on held-out data. |
| C10 | P2 | Controlled synthetic augmentation | Generate training-only descendants with parent/group IDs, transformation, language, phenomenon, generator version, and review status. Compare human-only against augmentation with frozen gold; reject cluster IDs as supervised labels. |
| C11 | P1 | Book-scale jobs and budgets | Use bounded cancellable jobs, checkpoints, source hashes, progress, and per-request/per-client budgets. Preserve full source on candidate truncation. Measure memory/latency/concurrency with all deployed models loaded. |
| C12 | P2 | Model release and diagram integration | Version schema/model/data/split identities and rollback bundles. Export reviewed annotations through a TYM adapter with stable IDs/provenance so the diagram API renders the same graph without reinterpretation. |

## Staged delivery

1. **Corpus and gold (C1-C3):** private source registry, reconciled editions, annotation guide, adjudicated pilot, and frozen split manifest.
2. **Models and measures (C4-C7):** task-specific contracts and paired diagnostics, followed by separately scored mention/boundary/link extraction on held-out gold.
3. **Review and diagrams (C8-C10, C12):** versioned corrections, review queues, controlled training augmentation, and deterministic export into the diagram prototype.
4. **Long-document release (C11-C12):** cancellable processing, provenance-preserving chunk boundaries, serving budgets, model integrity, and rollback verification.

## Required measurements

| Layer | Evidence |
| --- | --- |
| Annotation | Per-task agreement, adjudication counts, class support, and document/work coverage |
| Conditional classifiers | Accuracy, macro/per-class F1 and recall, confusion matrix, fold/support/model identity |
| Extraction | Exact/overlap span F1 with convention stated; mention/candidate recall; TS-boundary F1; end-to-end directed link F1 |
| Temporal semantics | TIMEX normalization with declared reference time; unknown coverage; supported graph contradictions; reviewed story-order recovery |
| Operations | Cold/warm latency, throughput at stated concurrency, retained/peak process memory, artifact bytes, quota/job behavior |
| Unlabeled discovery | Cluster stability/coverage and reviewed sample composition; no invented semantic accuracy |

Do not compare distances from different feature spaces as an accuracy ranking. Keep transductive clustering on a training sample separate from inductive held-out classification. Preserve unknown adjudication and distinguish full-data deployment weights from fold models.

## New private collection

The example importer records fifteen text files plus the Tash Aw PDF as sixteen source files. Group the two Shogun volumes together, both Tinutul blanurilor volumes together, both Morometii volumes together, and Tash Aw's English text/Romanian text/PDF together. Preserve uncertain bibliographic identities from filenames as unverified rather than inventing an author or edition.

Each bounded example has a matching `/v1/documents/analyze` request with language and text. No fictional date is added. Structured TimeBank input must be created by selecting actual reviewed mentions/endpoints; never reuse the raw excerpt as if it were a correctly marked relation pair. Full books are for private coverage exploration and future annotation, not immediate supervised training.

## Pictures and inspection

Use source-linked mention overlays, side-by-side reviewed/predicted graphs, and per-class confusion matrices when data supports them. For now, the local gallery contains real legacy-renderer SVGs for the supplied examples. Corpus-analysis screenshots and model-accuracy plots require an actual run and its metadata; label conceptual workflow illustrations separately.
