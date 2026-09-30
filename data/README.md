# Training data files

`seed-examples.jsonl` contains the examples used by the two startup ML.NET classifiers. It preserves the existing 43 segment-type and 24 event-temporal examples, but labels each as `synthetic_seed` and `unreviewed`. These short English examples are demo seeds, not annotations from the TYM corpus, and they do not support accuracy or generalization claims.

The API loads only this file for its seed models. It rejects duplicate IDs, missing metadata, non-seed sources, or document/chapter IDs on a seed row. The same examples and labels remain in use, so moving them out of `Program.cs` does not itself improve model quality.

Each JSONL object records:

- `id`, `task`, `text`, `label`, and `language`
- `source_type`, `source_group`, and `review_status`
- optional `parent_id` for an augmentation's immediate source
- optional `document_id` and `chapter_id` for examples derived from a real document

For reviewed corpus annotations, keep the corpus outside this repository unless its license and privacy terms explicitly permit publication. Use `source_type: "human_gold"`, `review_status: "adjudicated"`, stable document/chapter IDs, and a task-specific `gold_label`. Preserve source offsets in `span_start` and `span_end` when the task is span-based. Synthetic or weakly labeled examples should use their own `source_type`, retain a `parent_id` when derived from another example, and never be described as human gold.

## Grouped evaluation scaffold

See [`pocs/model-evaluation/README.md`](../pocs/model-evaluation/README.md) for a standard-library Python tool that creates chapter- or document-held-out split manifests and scores per-class precision/recall/F1, macro-F1, accuracy, and confusion matrices. The tool accepts only adjudicated human-gold rows. Create splits before producing synthetic descendants, then keep every descendant of a gold example in that example's training group. The evaluator does not yet score exact span boundaries or whole-graph consistency.
