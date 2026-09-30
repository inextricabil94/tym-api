#!/usr/bin/env python3
"""Create group-safe split manifests and score held-out classification predictions."""

from __future__ import annotations

import argparse
import hashlib
import json
import random
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


def read_jsonl(path: Path) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
        except json.JSONDecodeError as exc:
            raise ValueError(f"{path}:{line_number}: invalid JSON: {exc.msg}") from exc
        if not isinstance(row, dict):
            raise ValueError(f"{path}:{line_number}: each JSONL row must be an object")
        row_id = row.get("id")
        if not isinstance(row_id, str) or not row_id.strip():
            raise ValueError(f"{path}:{line_number}: id must be a non-empty string")
        if row_id in seen_ids:
            raise ValueError(f"{path}:{line_number}: duplicate id {row_id!r}")
        seen_ids.add(row_id)
        rows.append(row)
    if not rows:
        raise ValueError(f"{path}: no JSONL records found")
    return rows


def group_values(row: dict[str, Any], group_by: str) -> list[str]:
    document_id = row.get("document_id")
    if not isinstance(document_id, str) or not document_id.strip():
        raise ValueError(f"row {row['id']!r}: document_id is required")
    if group_by == "document":
        return [document_id]
    chapter_id = row.get("chapter_id")
    if not isinstance(chapter_id, str) or not chapter_id.strip():
        raise ValueError(f"row {row['id']!r}: chapter_id is required for chapter grouping")
    return [document_id, chapter_id]


def group_digest(values: list[str]) -> str:
    canonical = json.dumps(values, ensure_ascii=False, separators=(",", ":"))
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


def require_gold_row(row: dict[str, Any], *, prediction: bool = False) -> None:
    for field in ("task", "gold_label"):
        if not isinstance(row.get(field), str) or not row[field].strip():
            raise ValueError(f"row {row['id']!r}: {field} must be a non-empty string")
    if row.get("source_type") != "human_gold":
        raise ValueError(f"row {row['id']!r}: evaluation requires source_type='human_gold'")
    if row.get("review_status") != "adjudicated":
        raise ValueError(f"row {row['id']!r}: evaluation requires review_status='adjudicated'")
    if prediction and (not isinstance(row.get("predicted_label"), str) or not row["predicted_label"].strip()):
        raise ValueError(f"row {row['id']!r}: predicted_label must be a non-empty string")


def create_splits(args: argparse.Namespace) -> dict[str, Any]:
    rows = read_jsonl(args.gold)
    groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for row in rows:
        require_gold_row(row)
        group_id = group_digest(group_values(row, args.group_by))
        groups[group_id].append(row)

    if len(groups) < 3:
        raise ValueError("At least three distinct groups are required for train/dev/test splits")

    fractions = {"train": args.train_fraction, "dev": args.dev_fraction, "test": args.test_fraction}
    if any(value <= 0 for value in fractions.values()) or abs(sum(fractions.values()) - 1.0) > 1e-8:
        raise ValueError("Train, dev, and test fractions must be positive and sum to 1")

    rng = random.Random(args.seed)
    group_ids = list(groups)
    rng.shuffle(group_ids)
    group_ids.sort(key=lambda group_id: -len(groups[group_id]))

    split_names = ["train", "dev", "test"]
    target_rows = {name: len(rows) * fraction for name, fraction in fractions.items()}
    assigned_rows = {name: 0 for name in split_names}
    assignments: dict[str, str] = {}

    # Seed one whole group into each split so all three partitions exist.
    for split_name, group_id in zip(split_names, group_ids[:3]):
        assignments[group_id] = split_name
        assigned_rows[split_name] += len(groups[group_id])

    # Assign remaining groups to the split that most improves row-count balance.
    for group_id in group_ids[3:]:
        group_size = len(groups[group_id])

        def imbalance(candidate: str) -> float:
            return sum(
                ((assigned_rows[name] + (group_size if name == candidate else 0) - target_rows[name])
                 / max(target_rows[name], 1)) ** 2
                for name in split_names
            )

        chosen = min(split_names, key=imbalance)
        assignments[group_id] = chosen
        assigned_rows[chosen] += group_size

    group_counts = Counter(assignments.values())
    row_counts = Counter(
        assignments[group_digest(group_values(row, args.group_by))]
        for row in rows
    )
    manifest = {
        "schema_version": 1,
        "group_by": args.group_by,
        "seed": args.seed,
        "fractions": fractions,
        "group_assignments": assignments,
        "summary": {
            "rows": len(rows),
            "groups": len(groups),
            "groups_by_split": dict(group_counts),
            "rows_by_split": dict(row_counts),
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return {"output": str(args.output), **manifest["summary"]}


def score_task(rows: list[dict[str, Any]]) -> dict[str, Any]:
    labels = sorted({row["gold_label"] for row in rows} | {row["predicted_label"] for row in rows})
    confusion = {gold: {predicted: 0 for predicted in labels} for gold in labels}
    for row in rows:
        confusion[row["gold_label"]][row["predicted_label"]] += 1

    per_class: dict[str, dict[str, float | int]] = {}
    f1_values: list[float] = []
    for label in labels:
        true_positive = confusion[label][label]
        support = sum(confusion[label].values())
        predicted_count = sum(confusion[gold][label] for gold in labels)
        precision = true_positive / predicted_count if predicted_count else 0.0
        recall = true_positive / support if support else 0.0
        f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
        per_class[label] = {
            "precision": round(precision, 4),
            "recall": round(recall, 4),
            "f1": round(f1, 4),
            "support": support,
        }
        f1_values.append(f1)

    correct = sum(row["gold_label"] == row["predicted_label"] for row in rows)
    return {
        "count": len(rows),
        "accuracy": round(correct / len(rows), 4),
        "macro_f1": round(sum(f1_values) / len(f1_values), 4) if f1_values else 0.0,
        "labels": labels,
        "per_class": per_class,
        "confusion_matrix": confusion,
    }


def evaluate(args: argparse.Namespace) -> dict[str, Any]:
    manifest = json.loads(args.splits.read_text(encoding="utf-8"))
    assignments = manifest.get("group_assignments")
    group_by = manifest.get("group_by")
    if not isinstance(assignments, dict) or not isinstance(group_by, str) or group_by not in {"chapter", "document"}:
        raise ValueError(f"{args.splits}: invalid split manifest")

    rows_by_task: dict[str, list[dict[str, Any]]] = defaultdict(list)
    groups_scored: set[str] = set()
    for row in read_jsonl(args.predictions):
        require_gold_row(row, prediction=True)
        group_id = group_digest(group_values(row, group_by))
        assigned_split = assignments.get(group_id)
        if assigned_split is None:
            raise ValueError(f"row {row['id']!r}: group is absent from the split manifest")
        if assigned_split == args.split:
            rows_by_task[row["task"]].append(row)
            groups_scored.add(group_id)

    if not rows_by_task:
        raise ValueError(f"No predictions found for split {args.split!r}")

    return {
        "split": args.split,
        "group_by": group_by,
        "group_count": len(groups_scored),
        "tasks": {task: score_task(rows) for task, rows in sorted(rows_by_task.items())},
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)

    split_parser = commands.add_parser("make-splits", help="assign whole documents or chapters to train/dev/test")
    split_parser.add_argument("--gold", type=Path, required=True, help="Adjudicated human-gold JSONL")
    split_parser.add_argument("--output", type=Path, required=True, help="Path for the split manifest")
    split_parser.add_argument("--group-by", choices=("chapter", "document"), default="chapter")
    split_parser.add_argument("--seed", type=int, default=42)
    split_parser.add_argument("--train-fraction", type=float, default=0.70)
    split_parser.add_argument("--dev-fraction", type=float, default=0.15)
    split_parser.add_argument("--test-fraction", type=float, default=0.15)
    split_parser.set_defaults(handler=create_splits)

    eval_parser = commands.add_parser("evaluate", help="score predictions on one held-out split")
    eval_parser.add_argument("--predictions", type=Path, required=True, help="JSONL rows with gold and predicted labels")
    eval_parser.add_argument("--splits", type=Path, required=True, help="Manifest created by make-splits")
    eval_parser.add_argument("--split", choices=("dev", "test"), default="test")
    eval_parser.add_argument("--output", type=Path, help="Optional JSON report path; defaults to stdout")
    eval_parser.set_defaults(handler=evaluate)

    args = parser.parse_args()
    try:
        result = args.handler(args)
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    rendered = json.dumps(result, indent=2, ensure_ascii=False) + "\n"
    if args.command == "evaluate" and args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
