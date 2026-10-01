#!/usr/bin/env python3
"""Convert paired TYM XML annotations into private, provenance-marked JSONL.

This importer intentionally handles the project's TYM <TAGS> export, not TimeML.
It does not copy the source XMLs or generated JSONL into the repository.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path
from typing import Any


SEGMENT_LABELS = {
    "narration": "NAR",
    "remembers": "REM",
    "supporting": "SUP",
    "generalknowledge": "GEN",
    "fiction": "FIC",
}

RELATION_LABELS = {
    "before": "BEFORE",
    "immediately_before": "IMMEDIATELY_BEFORE",
    "after": "AFTER",
    "immediately_after": "IMMEDIATELY_AFTER",
    "simultaneous": "SIMULTANEOUS",
    "simmultaneous": "SIMULTANEOUS",  # known source typo, normalized explicitly
}


def secure_parse(path: Path) -> ET.Element:
    data = path.read_bytes()
    if re.search(br"<!\s*(DOCTYPE|ENTITY)\b", data, re.IGNORECASE):
        raise ValueError(f"{path}: DTD/entity declarations are not accepted by this importer")
    root = ET.fromstring(data)
    if root.tag != "TAGS":
        raise ValueError(f"{path}: expected a TYM <TAGS> root, found <{root.tag}>")
    return root


def one_per_id(root: ET.Element, element_name: str, path: Path) -> dict[str, ET.Element]:
    result: dict[str, ET.Element] = {}
    for element in root.findall(element_name):
        identifier = element.get("ID", "").strip()
        if not identifier:
            raise ValueError(f"{path}: <{element_name}> without an ID")
        if identifier in result:
            raise ValueError(f"{path}: duplicate {element_name} ID {identifier!r}")
        result[identifier] = element
    return result


def normalize_segment_label(raw: str, path: Path, identifier: str) -> str:
    label = SEGMENT_LABELS.get(raw.strip().casefold())
    if label is None:
        raise ValueError(f"{path}: unsupported TS TYPE {raw!r} on {identifier}")
    return label


def normalize_relation_label(raw: str, path: Path, identifier: str) -> str:
    label = RELATION_LABELS.get(raw.strip().casefold())
    if label is None:
        raise ValueError(f"{path}: unsupported TREL REL {raw!r} on {identifier}")
    return label


def parse_span(raw: str, path: Path, identifier: str) -> tuple[int, int]:
    try:
        start, end = (int(part) for part in raw.split("~", maxsplit=1))
    except (TypeError, ValueError) as exc:
        raise ValueError(f"{path}: invalid SPANS value {raw!r} on {identifier}") from exc
    if start < 0 or end <= start:
        raise ValueError(f"{path}: invalid half-open span {raw!r} on {identifier}")
    return start, end


def row_base(row_id: str, task: str, language: str, text: str, label: str, source_group: str) -> dict[str, Any]:
    return {
        "id": row_id,
        "task": task,
        "text": text,
        "label": label,
        "language": language,
        "source_type": "provided_annotation",
        "source_group": source_group,
        "review_status": "adjudication_unknown",
        "parent_id": None,
        "document_id": source_group,
        "chapter_id": source_group,
    }


def import_one(path: Path, language: str, source_group: str) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    root = secure_parse(path)
    segments = one_per_id(root, "TS", path)
    relations = one_per_id(root, "TREL", path)
    rows: list[dict[str, Any]] = []
    segment_labels: Counter[str] = Counter()
    span_to_segment: dict[tuple[int, int], str] = {}

    for identifier, segment in segments.items():
        start, end = parse_span(segment.get("SPANS", ""), path, identifier)
        if (start, end) in span_to_segment:
            raise ValueError(f"{path}: duplicate segment span {start}~{end}")
        span_to_segment[(start, end)] = identifier
        text = segment.get("TEXT", "").strip()
        if not text:
            raise ValueError(f"{path}: empty TS TEXT on {identifier}")
        label = normalize_segment_label(segment.get("TYPE", ""), path, identifier)
        segment_labels[label] += 1
        rows.append(row_base(
            f"{source_group}:{language}:segment:{identifier}",
            "segment_type", language, text, label, source_group,
        ))

    invalid_relations = 0
    relation_labels: Counter[str] = Counter()
    for identifier, relation in relations.items():
        source_id = relation.get("FROM", "")
        target_id = relation.get("TO", "")
        if source_id not in segments or target_id not in segments:
            invalid_relations += 1
            continue
        source = segments[source_id]
        target = segments[target_id]
        trigger = relation.get("TRIGGER", "").strip()
        # Keep annotation summaries, actor IDs, and location IDs out of features.
        # Segment surface text + the annotated trigger are the model's inputs.
        text = (
            f"Earlier segment: {source.get('TEXT', '').strip()}\n"
            f"Relation cue: {trigger}\n"
            f"Later segment: {target.get('TEXT', '').strip()}"
        )
        label = normalize_relation_label(relation.get("REL", ""), path, identifier)
        relation_labels[label] += 1
        rows.append(row_base(
            f"{source_group}:{language}:relation:{identifier}",
            "temporal_relation", language, text, label, source_group,
        ))

    stats = {
        "file": path.name,
        "language": language,
        "segments": len(segments),
        "segment_labels": dict(sorted(segment_labels.items())),
        "relations_in_xml": len(relations),
        "relations_imported": len(relations) - invalid_relations,
        "relations_skipped_missing_segment_endpoint": invalid_relations,
        "relation_labels": dict(sorted(relation_labels.items())),
        "span_count": len(span_to_segment),
    }
    return rows, stats


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--romanian", required=True, type=Path, help="Romanian annotated TYM XML")
    parser.add_argument("--english", required=True, type=Path, help="English annotated TYM XML")
    parser.add_argument("--out", required=True, type=Path, help="Private JSONL output path")
    parser.add_argument("--report", type=Path, help="Optional aggregate import report JSON path")
    parser.add_argument("--source-group", default="provided-hln-motiw-narrative")
    args = parser.parse_args()

    all_rows: list[dict[str, Any]] = []
    stats = []
    for path, language in ((args.romanian, "ro"), (args.english, "en")):
        rows, detail = import_one(path, language, args.source_group)
        all_rows.extend(rows)
        stats.append(detail)

    ids = [row["id"] for row in all_rows]
    if len(ids) != len(set(ids)):
        raise ValueError("Generated row IDs are not unique")
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8", newline="\n") as handle:
        for row in all_rows:
            handle.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")

    report = {
        "schema_version": 1,
        "source_group": args.source_group,
        "output_rows": len(all_rows),
        "provenance": "provided_annotation/adjudication_unknown; train-only",
        "files": stats,
        "notes": [
            "The English and Romanian files are translations/parallel annotations of one story and must remain in the same split.",
            "This parser converts the custom TYM TAGS XML; it does not convert Ro-TimeBank or ISO-TimeML.",
            "Evaluation must use a separate, independently adjudicated human-gold corpus.",
            "A TREL whose endpoint is not present in its source file is skipped and counted above.",
        ],
    }
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, ET.ParseError) as error:
        print(f"error: {error}", file=sys.stderr)
        raise SystemExit(2)
