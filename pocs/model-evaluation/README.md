# Grouped TYM model evaluation

This first evaluation utility is for classification predictions, including the API's current `segment_type` and `event_temporal` labels. It creates train/dev/test partitions by whole chapter or document and reports accuracy, macro-F1, per-class precision/recall/F1, support, and a confusion matrix. It uses only the Python standard library (Python 3.10+).

It is a scaffold, not an evaluation result: no annotated corpus is included, and no metrics are reported by this repository. The tool rejects seed, synthetic, weakly labeled, and unadjudicated examples as evaluation gold. It does not yet score event-span boundaries or end-to-end temporal graph consistency.

## Input rows

Provide one UTF-8 JSON object per line. Gold annotations must include:

```json
{"id":"doc1-ch1-e1","task":"event_temporal","document_id":"doc1","chapter_id":"ch1","gold_label":"Past","source_type":"human_gold","review_status":"adjudicated"}
```

Add `text` and source offsets such as `span_start` and `span_end` to your private annotation records as needed by the model pipeline. For predictions, preserve the gold fields and add `predicted_label`. IDs must be unique within each file.

## Create the split manifest

For a single annotated novel, hold out whole chapters:

```powershell
python .\pocs\model-evaluation\evaluate_predictions.py make-splits `
  --gold C:\data\tym-gold.jsonl `
  --output C:\data\tym-splits.json `
  --group-by chapter `
  --seed 42
```

For multiple independent works, prefer holding out whole documents:

```powershell
python .\pocs\model-evaluation\evaluate_predictions.py make-splits `
  --gold C:\data\tym-gold.jsonl `
  --output C:\data\tym-splits.json `
  --group-by document
```

At least three distinct groups are required. The default ratios are 70% train, 15% dev, and 15% test by row count; assignment is by whole group, so actual ratios can differ and class balance is not guaranteed. Inspect the manifest summary before training. The manifest stores hashes of group IDs rather than text or raw document/chapter IDs.

Create synthetic or weakly labeled descendants only after making the split. Keep them in training with their source example's group; never use them as held-out gold or to tune decisions against the test set.

## Score held-out predictions

The prediction file may contain predictions for multiple groups. The evaluator uses only the selected held-out split from the manifest and requires adjudicated human-gold labels:

```powershell
python .\pocs\model-evaluation\evaluate_predictions.py evaluate `
  --predictions C:\data\tym-heldout-predictions.jsonl `
  --splits C:\data\tym-splits.json `
  --split test `
  --output C:\data\tym-test-metrics.json
```

Run `--split dev` for development metrics. Use dev, not test, to select thresholds or tune the model. Keep the test set for the final comparison. For temporal links, make each annotated relation pair a row with a task such as `temporal_relation`; for boundary labels, use a task such as `segment_boundary`. Both receive ordinary classification metrics in this scaffold.
