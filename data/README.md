# Training data files

`seed-examples.jsonl` contains the examples used by the two startup ML.NET classifiers. It preserves the existing 43 segment-type and 24 event-temporal examples, but labels each as `synthetic_seed` and `unreviewed`. These short English examples are demo seeds, not annotations from the TYM corpus, and they do not support accuracy or generalization claims.

The API loads only this file for its seed models. It rejects duplicate IDs, missing metadata, non-seed sources, or document/chapter IDs on a seed row. The same examples and labels remain in use, so moving them out of `Program.cs` does not itself improve model quality.

Each JSONL object records:

- `id`, `task`, `text`, `label`, and `language`
- `source_type`, `source_group`, and `review_status`
- optional `parent_id` for an augmentation's immediate source
- optional `document_id` and `chapter_id` for examples derived from a real document

For reviewed corpus annotations, keep the corpus outside this repository unless its license and privacy terms explicitly permit publication. Use `source_type: "human_gold"`, `review_status: "adjudicated"`, stable document/chapter IDs, and a task-specific `gold_label`. Preserve source offsets in `span_start` and `span_end` when the task is span-based. Synthetic or weakly labeled examples should use their own `source_type`, retain a `parent_id` when derived from another example, and never be described as human gold.

The modeling CLI additionally permits local `provided_annotation` rows with `review_status: "adjudication_unknown"` in the `train` command only. These rows cannot enter grouped evaluation or be called human gold. The private importer at `tools/Tym.Modeling/import-tym-xml.py` converts the supplied custom TYM `<TAGS>` pair to this format; it excludes annotator-written summaries/actor IDs/location IDs from features, skips unresolved relation endpoints with a count, and marks the translated versions as one source group. Keep the generated JSONL and models outside GitHub.

## Ro-TimeBank import

The separate Ro-TimeBank XML archive uses ISO-TimeML and its rights differ from these TYM annotations. `tools/Tym.Modeling/import-ro-timebank.py` imports its labels into separate task namespaces with `provided_annotation/adjudication_unknown` provenance for training only. Keep corpus text, converted JSONL, and model weights outside Git; this import supports no held-out accuracy claim.

Pass `--exclusions tools/Tym.Modeling/ro-timebank-exclusions.json` from the repository root. The manifest must match the input archive's SHA-256 and quarantines 26 documents containing untranslated English body passages, including their Romanian passages. The importer also skips one link referencing an ambiguous duplicate signal ID. The supplied archive then yields 157 documents and 20,151 rows:

| Task | Rows |
| --- | ---: |
| `timebank_event_class` | 5,947 |
| `timebank_event_tense` | 5,920 |
| `timebank_timex_type` | 1,144 |
| `timebank_tlink` | 4,808 |
| `timebank_slink` | 2,133 |
| `timebank_alink` | 199 |

Contexts identify each event/time mention with `[TARGET] ... [/TARGET]`. Relation examples preserve source/target direction and use contexts centered on each marked endpoint. Sentence, headline, and lead-paragraph text provide context when available. Do not map these event/TIMEX/link labels to TYM segment or relation labels by name alone.

## Unlabeled books

Raw books are a third data category. `tools/Tym.Modeling/prepare-book-corpus.py` converts the supplied text archive and standalone English/Romanian books into provenance-marked `unlabeled_user_provided` passages for ML.NET K-Means exploration. They contain no supervised labels and cannot enter `train` or `evaluate`. Keep the raw text, passages, assignments, and cluster weights private until publication and model-distribution rights are confirmed.

`tools/Tym.Modeling.Tests` includes self-contained synthetic component tests and 20 free-text prediction snapshot cases for the ten private corpus models. The snapshots record model behavior, not gold labels or generalization scores; set `TYM_PRIVATE_MODEL_DIR` to run them. They are skipped when that variable is unset, while component tests remain available.

## Grouped evaluation scaffold

See [`pocs/model-evaluation/README.md`](../pocs/model-evaluation/README.md) for a standard-library Python tool that creates chapter- or document-held-out split manifests and scores per-class precision/recall/F1, macro-F1, accuracy, and confusion matrices. The tool accepts only adjudicated human-gold rows. Create splits before producing synthetic descendants, then keep every descendant of a gold example in that example's training group. The evaluator does not yet score exact span boundaries or whole-graph consistency.
