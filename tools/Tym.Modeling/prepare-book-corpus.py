#!/usr/bin/env python3
"""Prepare private, unlabeled book passages for exploratory ML.NET clustering.

The script treats book contents only as data. It reads ZIP entries in place, rejects
unsafe archive paths, and writes no source text into the repository.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import unicodedata
import zipfile
from collections import Counter
from pathlib import Path, PurePosixPath
from typing import Iterable


MAX_ARCHIVE_BYTES = 100 * 1024 * 1024
MAX_COMPRESSION_RATIO = 100
SENTENCE_BOUNDARY = re.compile(r"(?<=[.!?])\s+(?=[A-ZĂÂÎȘȚ0-9\"„«])")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def slug(value: str) -> str:
    value = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode("ascii")
    return re.sub(r"[^a-z0-9]+", "-", value.casefold()).strip("-") or "document"


def decode_text(data: bytes, source: str) -> tuple[str, str]:
    for encoding in ("utf-8-sig", "cp1250"):
        try:
            text = data.decode(encoding)
            break
        except UnicodeDecodeError:
            continue
    else:
        raise ValueError(f"{source}: unsupported text encoding")
    text = unicodedata.normalize("NFC", text).replace("\u00a0", " ").replace("\x00", "")
    return text, encoding


def split_long_unit(unit: str, maximum: int) -> list[str]:
    sentences = SENTENCE_BOUNDARY.split(unit)
    if len(sentences) == 1:
        words = unit.split()
        result: list[str] = []
        current: list[str] = []
        length = 0
        for word in words:
            if current and length + 1 + len(word) > maximum:
                result.append(" ".join(current))
                current, length = [], 0
            current.append(word)
            length += len(word) + (1 if length else 0)
        if current:
            result.append(" ".join(current))
        return result
    result, current = [], ""
    for sentence in sentences:
        if len(sentence) > maximum:
            if current:
                result.append(current)
                current = ""
            result.extend(split_long_unit(sentence, maximum))
        elif current and len(current) + 1 + len(sentence) > maximum:
            result.append(current)
            current = sentence
        else:
            current = f"{current} {sentence}".strip()
    if current:
        result.append(current)
    return result


def passages(text: str, target: int = 1800, maximum: int = 2400, minimum: int = 300) -> list[str]:
    paragraphs = [re.sub(r"\s+", " ", item).strip() for item in re.split(r"(?:\r?\n\s*){2,}", text)]
    units: list[str] = []
    for paragraph in paragraphs:
        if not paragraph:
            continue
        units.extend(split_long_unit(paragraph, maximum) if len(paragraph) > maximum else [paragraph])
    result: list[str] = []
    current = ""
    for unit in units:
        if current and len(current) + 2 + len(unit) > target:
            result.append(current)
            current = unit
        else:
            current = f"{current}\n\n{unit}".strip()
    if current:
        if result and len(current) < minimum and len(result[-1]) + 2 + len(current) <= maximum:
            result[-1] += "\n\n" + current
        else:
            result.append(current)
    return [item for item in result if len(item) >= minimum]


def archive_sources(path: Path) -> Iterable[dict]:
    with zipfile.ZipFile(path) as archive:
        files = [item for item in archive.infolist() if not item.is_dir()]
        if sum(item.file_size for item in files) > MAX_ARCHIVE_BYTES:
            raise ValueError("Archive uncompressed size exceeds the 100 MiB limit")
        for item in files:
            item_path = PurePosixPath(item.filename)
            if item_path.is_absolute() or ".." in item_path.parts or "\\" in item.filename or ":" in item.filename:
                raise ValueError(f"Unsafe archive path: {item.filename}")
            if item.flag_bits & 1:
                raise ValueError(f"Encrypted archive entry is unsupported: {item.filename}")
            ratio = item.file_size / max(item.compress_size, 1)
            if ratio > MAX_COMPRESSION_RATIO:
                raise ValueError(f"Suspicious compression ratio for: {item.filename}")
            if item_path.suffix.casefold() != ".txt":
                raise ValueError(f"Books archive contains a non-text file: {item.filename}")
            data = archive.read(item)
            text, encoding = decode_text(data, item.filename)
            if len(item_path.parts) > 2:
                document_name = item_path.parts[-2]
            else:
                document_name = item_path.stem
            yield {
                "source": item.filename,
                "document_id": slug(document_name),
                "chapter_id": slug(item_path.stem),
                "text": text,
                "encoding": encoding,
                "bytes": len(data),
                "sha256": sha256(data),
            }


def standalone_source(path: Path) -> dict:
    data = path.read_bytes()
    text, encoding = decode_text(data, path.name)
    return {
        "source": path.name,
        "document_id": slug(path.stem),
        "chapter_id": slug(path.stem),
        "text": text,
        "encoding": encoding,
        "bytes": len(data),
        "sha256": sha256(data),
    }


def write_language(sources: list[dict], language: str, output: Path) -> dict:
    rows: list[dict] = []
    seen_text: set[str] = set()
    skipped_duplicates = 0
    source_summaries = []
    for source in sources:
        source_passages = passages(source["text"])
        accepted = 0
        for index, text in enumerate(source_passages, start=1):
            text_hash = sha256(text.encode("utf-8"))
            if text_hash in seen_text:
                skipped_duplicates += 1
                continue
            seen_text.add(text_hash)
            accepted += 1
            rows.append({
                "id": f"book:{language}:{source['document_id']}:{source['chapter_id']}:{index:05d}",
                "text": text,
                "language": language,
                "source_type": "unlabeled_user_provided",
                "source_group": "user-provided-books-20261002",
                "document_id": source["document_id"],
                "chapter_id": source["chapter_id"],
            })
        source_summaries.append({
            "source": source["source"],
            "bytes": source["bytes"],
            "sha256": source["sha256"],
            "encoding": source["encoding"],
            "document_id": source["document_id"],
            "chapter_id": source["chapter_id"],
            "passages": accepted,
        })
    if not rows:
        raise ValueError(f"No usable {language} passages were produced")
    ids = [row["id"] for row in rows]
    if len(ids) != len(set(ids)):
        duplicates = [identifier for identifier, count in Counter(ids).items() if count > 1]
        raise ValueError(f"Generated row IDs are not unique: {duplicates[:5]}")
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("w", encoding="utf-8", newline="\n") as stream:
        for row in rows:
            stream.write(json.dumps(row, ensure_ascii=False, separators=(",", ":")) + "\n")
    return {
        "language": language,
        "source_files": len(source_summaries),
        "documents": len({row["document_id"] for row in rows}),
        "passages": len(rows),
        "characters": sum(len(row["text"]) for row in rows),
        "exact_duplicate_passages_skipped": skipped_duplicates,
        "output": str(output.resolve()),
        "sources": source_summaries,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True, type=Path)
    parser.add_argument("--romanian", required=True, type=Path)
    parser.add_argument("--english", required=True, type=Path)
    parser.add_argument("--ro-out", required=True, type=Path)
    parser.add_argument("--en-out", required=True, type=Path)
    parser.add_argument("--report", required=True, type=Path)
    args = parser.parse_args()
    ro_sources = list(archive_sources(args.archive)) + [standalone_source(args.romanian)]
    en_sources = [standalone_source(args.english)]
    report = {
        "schema_version": 1,
        "archive": args.archive.name,
        "archive_sha256": sha256(args.archive.read_bytes()),
        "purpose": "unlabeled exploratory ML.NET clustering and coverage analysis",
        "provenance": "user-provided raw books; no annotation or gold-label claim",
        "rights_note": "The supplied files contain no machine-readable license grant. Keep source text and derived artifacts private until publication and model-distribution rights are confirmed.",
        "corpora": [write_language(ro_sources, "ro", args.ro_out), write_language(en_sources, "en", args.en_out)],
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "status": "prepared",
        "archive": report["archive"],
        "archive_sha256": report["archive_sha256"],
        "corpora": [{key: value for key, value in corpus.items() if key != "sources"} for corpus in report["corpora"]],
    }, ensure_ascii=True, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        print(f"error: {error}", file=__import__("sys").stderr)
        raise SystemExit(2)
