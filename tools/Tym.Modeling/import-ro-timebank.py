#!/usr/bin/env python3
"""Create private train-only ML.NET rows from Ro-TimeBank ISO-TimeML XML.

The output keeps TimeML tasks separate from TYM segment/relation labels. The
archive is read in place; source files are never copied into this repository.
"""

from __future__ import annotations

import argparse
import collections
import html
import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path
from typing import Any


BUILTIN_ENTITIES = {"amp", "lt", "gt", "apos", "quot"}
ENTITY_DEFINITION = re.compile(
    r"<!ENTITY\s+([A-Za-z][\w.-]*)\s+['\"]([^'\"]*)['\"]\s*>", re.DOTALL
)
ENTITY_REFERENCE = re.compile(r"&([A-Za-z][\w.-]*);")
DOCTYPE = re.compile(r"<!DOCTYPE\s+[^>]*>", re.DOTALL)
ANNOTATION_TAGS = {"EVENT", "TIMEX3", "SIGNAL", "ENAMEX", "CARDINAL", "NUMEX"}
XML_LINK_TYPES = ("TLINK", "SLINK", "ALINK")


def read_entity_table(dtd_text: str) -> dict[str, str]:
    raw_entities = dict(ENTITY_DEFINITION.findall(dtd_text))
    resolved: dict[str, str] = {}

    def resolve(name: str, stack: frozenset[str] = frozenset()) -> str:
        if name in BUILTIN_ENTITIES:
            return {"amp": "&", "lt": "<", "gt": ">", "apos": "'", "quot": '"'}[name]
        if name in resolved:
            return resolved[name]
        if name not in raw_entities or name in stack:
            raise ValueError(f"cannot resolve DTD entity &{name};")

        value = raw_entities[name]
        value = ENTITY_REFERENCE.sub(
            lambda match: resolve(match.group(1), stack | {name}), value
        )
        value = html.unescape(value)
        if any(character in value for character in "<>&") or len(value) > 8:
            raise ValueError(f"unsafe or unsupported value for DTD entity &{name};")
        resolved[name] = value
        return value

    for entity_name in raw_entities:
        resolve(entity_name)
    # Three source files use all-caps spellings absent from the DTD.
    resolved.update({"ABREVE": resolved["Abreve"], "SCEDIL": resolved["Scedil"], "TCEDIL": resolved["Tcedil"]})
    return resolved


def secure_xml_text(raw: bytes, entities: dict[str, str], source_name: str) -> str:
    try:
        xml_text = raw.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise ValueError(f"{source_name}: expected UTF-8 XML") from error

    doctype_match = DOCTYPE.search(xml_text)
    if doctype_match:
        declaration = doctype_match.group(0)
        if "[" in declaration or "]" in declaration:
            raise ValueError(f"{source_name}: inline DTD subsets are not accepted")
        xml_text = xml_text[: doctype_match.start()] + xml_text[doctype_match.end() :]

    xml_text = re.sub(r"<\?xml\s+[^?]*\?>", "", xml_text, count=1)

    def replace_entity(match: re.Match[str]) -> str:
        name = match.group(1)
        if name in BUILTIN_ENTITIES:
            return match.group(0)
        if name not in entities:
            raise ValueError(f"{source_name}: entity &{name}; is not declared in the archive DTD")
        return entities[name]

    return ENTITY_REFERENCE.sub(replace_entity, xml_text)


def visible_text(element: ET.Element, target: ET.Element | None = None) -> str:
    """Flatten inline markup and restore word boundaries around annotation tags."""
    parts = [element.text or ""]
    for child in element:
        child_text = visible_text(child, target)
        if child.tag in ANNOTATION_TAGS:
            parts.extend((" ", child_text, " "))
        else:
            parts.append(child_text)
        parts.append(child.tail or "")
    result = re.sub(r"\s+", " ", "".join(parts)).strip()
    return f"[TARGET] {result} [/TARGET]" if element is target else result


def sentence_context(root: ET.Element) -> dict[int, ET.Element]:
    contexts: dict[int, ET.Element] = {}

    def visit(element: ET.Element, context: ET.Element | None = None) -> None:
        if element.tag.lower() in {"s", "headline", "leadpara", "lp"}:
            context = element
        for child in element:
            if context is not None:
                contexts[id(child)] = context
            visit(child, context)

    visit(root)
    return contexts


def target_context(element: ET.Element, contexts: dict[int, ET.Element]) -> str:
    context = contexts.get(id(element), element)
    return visible_text(context, target=element)


def relation_context_window(context: str) -> str:
    start = context.find("[TARGET]")
    end = context.find("[/TARGET]")
    if start < 0 or end < 0:
        return context[:500]
    return context[max(0, start - 200): min(len(context), end + len("[/TARGET]") + 200)]


def add_identifier(mapping: dict[str, tuple[str, str]], key: str | None, value: tuple[str, str]) -> None:
    if key and key in mapping and mapping[key] != value:
        raise ValueError(f"ambiguous participant identifier {key!r}")
    if key:
        mapping[key] = value


def row(
    document_id: str,
    task: str,
    item_id: str,
    text: str,
    label: str,
) -> dict[str, Any]:
    return {
        "id": f"ro-timebank:{document_id}:{task}:{item_id}",
        "task": task,
        "text": text,
        "label": label,
        "language": "ro",
        "source_type": "provided_annotation",
        "source_group": "ro-timebank-isotimeml",
        "review_status": "adjudication_unknown",
        "parent_id": None,
        "document_id": document_id,
        "chapter_id": document_id,
    }


def import_document(
    raw: bytes,
    filename: str,
    entities: dict[str, str],
) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    text = secure_xml_text(raw, entities, filename)
    try:
        root = ET.fromstring(text)
    except ET.ParseError as error:
        raise ValueError(f"{filename}: invalid ISO-TimeML XML: {error}") from error

    if root.tag != "TimeML":
        raise ValueError(f"{filename}: expected a TimeML root, found <{root.tag}>")

    document_id = Path(filename).stem
    contexts = sentence_context(root)
    rows: list[dict[str, Any]] = []
    task_labels: dict[str, collections.Counter[str]] = collections.defaultdict(collections.Counter)

    participants: dict[str, tuple[str, str]] = {}
    signals: dict[str, tuple[str, str]] = {}
    for event in root.iter("EVENT"):
        mention = visible_text(event)
        context = target_context(event, contexts)
        for identifier_attribute in ("eid", "eiid", "eventID"):
            add_identifier(participants, event.get(identifier_attribute), (mention, context))
        for attribute, task in (("class", "timebank_event_class"), ("tense", "timebank_event_tense")):
            label = (event.get(attribute) or "").strip()
            if label:
                rows.append(row(document_id, task, event.get("eid") or event.get("eiid") or "event", f"Context: {context}\nEvent: {mention}", label))
                task_labels[task][label] += 1

    for timex in root.iter("TIMEX3"):
        mention = visible_text(timex)
        context = target_context(timex, contexts)
        add_identifier(participants, timex.get("tid"), (mention, context))
        label = (timex.get("type") or "").strip()
        if label:
            rows.append(row(document_id, "timebank_timex_type", timex.get("tid") or "timex", f"Context: {context}\nTime expression: {mention}", label))
            task_labels["timebank_timex_type"][label] += 1

    ambiguous_signals: set[str] = set()
    for signal in root.iter("SIGNAL"):
        signal_id = signal.get("sid")
        if signal_id and signal_id in signals:
            ambiguous_signals.add(signal_id)
        elif signal_id:
            signals[signal_id] = (visible_text(signal), target_context(signal, contexts))

    skipped_links = collections.Counter()
    skipped_ambiguous_signals = collections.Counter()
    for link_tag in XML_LINK_TYPES:
        task = f"timebank_{link_tag.lower()}"
        for link_index, link in enumerate(root.iter(link_tag), start=1):
            source_id = next((link.get(name) for name in ("eventID", "eventInstanceID", "timeID") if link.get(name)), None)
            target_id = next((link.get(name) for name in ("relatedToEvent", "relatedToEventInstance", "relatedToTime", "subordinatedEvent") if link.get(name)), None)
            source = participants.get(source_id or "")
            target = participants.get(target_id or "")
            if source is None or target is None:
                skipped_links[link_tag] += 1
                continue

            if link.get("signalID") in ambiguous_signals:
                skipped_ambiguous_signals[link_tag] += 1
                continue

            signal = signals.get(link.get("signalID", ""), ("", ""))[0]
            relation_text = (
                f"From: {source[0]}\nFrom context: {relation_context_window(source[1])}\n"
                f"Signal: {signal}\nTo: {target[0]}\nTo context: {relation_context_window(target[1])}"
            )
            label = (link.get("relType") or "").strip()
            # Some source files repeat TLINK IDs; include document order to keep
            # generated JSONL keys unique while preserving the original ID.
            item_id = f"{link_tag.lower()}-{link_index}-{link.get('lid') or 'unid'}"
            if label:
                rows.append(row(document_id, task, item_id, relation_text, label))
                task_labels[task][label] += 1

    summary = {
        "document_id": document_id,
        "task_rows": {task: sum(counter.values()) for task, counter in sorted(task_labels.items())},
        "task_labels": {task: dict(sorted(counter.items())) for task, counter in sorted(task_labels.items())},
        "links_skipped_missing_endpoint": dict(sorted(skipped_links.items())),
        "links_skipped_ambiguous_signal": dict(sorted(skipped_ambiguous_signals.items())),
    }
    return rows, summary


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--archive", required=True, type=Path, help="Private Ro-TimeBank.zip path")
    parser.add_argument("--out", required=True, type=Path, help="Private train-only JSONL output path")
    parser.add_argument("--report", type=Path, help="Optional aggregate report JSON path")
    parser.add_argument("--exclusions", type=Path, help="Optional audited document exclusions JSON, bound to the input archive SHA-256")
    args = parser.parse_args()

    try:
        archive_sha256 = hashlib.sha256(args.archive.read_bytes()).hexdigest()
        exclusions: dict[str, str] = {}
        if args.exclusions:
            exclusion_manifest = json.loads(args.exclusions.read_text(encoding="utf-8-sig"))
            if exclusion_manifest.get("archive_sha256") != archive_sha256:
                raise ValueError("The exclusions manifest is for a different archive SHA-256")
            exclusions = {
                item["document_id"]: item["reason"]
                for item in exclusion_manifest["documents"]
            }
        with zipfile.ZipFile(args.archive) as archive:
            dtd_name = "Ro-TimeBank/dtd/ISO-TimeML-Ro.dtd"
            if dtd_name not in archive.namelist():
                raise ValueError(f"{args.archive}: missing {dtd_name}")
            entities = read_entity_table(archive.read(dtd_name).decode("utf-8", "replace"))
            filenames = sorted(
                name for name in archive.namelist()
                if name.startswith("Ro-TimeBank/data/ro/") and name.endswith(".xml")
            )
            if not filenames:
                raise ValueError(f"{args.archive}: no Romanian TimeML documents were found")
            available_documents = {Path(name).stem for name in filenames}
            unknown_exclusions = set(exclusions) - available_documents
            if unknown_exclusions:
                raise ValueError(f"Unknown excluded documents: {sorted(unknown_exclusions)}")

            examples: list[dict[str, Any]] = []
            document_summaries: list[dict[str, Any]] = []
            for filename in filenames:
                if Path(filename).stem in exclusions:
                    continue
                document_examples, summary = import_document(archive.read(filename), filename, entities)
                examples.extend(document_examples)
                document_summaries.append(summary)

        ids = [example["id"] for example in examples]
        if len(ids) != len(set(ids)):
            duplicate_ids = [identifier for identifier, count in collections.Counter(ids).items() if count > 1]
            raise ValueError(f"Generated row IDs are not unique: {duplicate_ids[:10]}")

        args.out.parent.mkdir(parents=True, exist_ok=True)
        with args.out.open("w", encoding="utf-8", newline="\n") as stream:
            for example in examples:
                stream.write(json.dumps(example, ensure_ascii=False, separators=(",", ":")) + "\n")

        task_counts: dict[str, collections.Counter[str]] = collections.defaultdict(collections.Counter)
        for summary in document_summaries:
            for task, label_counts in summary["task_labels"].items():
                task_counts[task].update(label_counts)
        skipped_totals: collections.Counter[str] = collections.Counter()
        ambiguous_signal_totals: collections.Counter[str] = collections.Counter()
        for summary in document_summaries:
            skipped_totals.update(summary["links_skipped_missing_endpoint"])
            ambiguous_signal_totals.update(summary["links_skipped_ambiguous_signal"])
        report = {
            "schema_version": 1,
            "archive": args.archive.name,
            "archive_sha256": archive_sha256,
            "archive_documents": len(filenames),
            "documents": len(document_summaries),
            "excluded_documents": [{"document_id": name, "reason": reason} for name, reason in sorted(exclusions.items())],
            "output_rows": len(examples),
            "provenance": "provided_annotation/adjudication_unknown; train-only",
            "corpus_license_scope": "research-use; commercial use and redistribution require checking source-owner terms",
            "label_families": "ISO-TimeML only; never mapped to TYM TS/TREL classes",
            "tasks": {task: {"rows": sum(labels.values()), "labels": dict(sorted(labels.items()))} for task, labels in sorted(task_counts.items())},
            "links_skipped_missing_endpoint": dict(sorted(skipped_totals.items())),
            "links_skipped_ambiguous_signal": dict(sorted(ambiguous_signal_totals.items())),
            "notes": [
                "Romanian TimeBank documents are translated news, not TYM literary segment annotations.",
                "The paired XCES English/Romanian files are not used to project ISO-TimeML labels onto English.",
                "This importer preserves separate EVENT, TIMEX3, TLINK, SLINK, and ALINK task namespaces.",
                "No held-out evaluation is supported by this import; use independent adjudicated documents for evaluation.",
            ],
        }
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, ET.ParseError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
