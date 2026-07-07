#!/usr/bin/env python
"""Non-LLM NLP POCs for the Time Yards Model pipeline.

The runner compares practical NLP components against the implementation steps
described in the article:

1. event/clause segmentation
2. actor/person NER
3. temporal expression detection
4. temporal event category inference: Past, Present, Future
5. temporal relation inference
6. TS grouping by actor unity and temporal category
7. TT grouping by actor set

It does not call an LLM.
"""

from __future__ import annotations

import argparse
import csv
import importlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Iterable


PERSON_LABELS = {"PERSON", "PER"}
LOCATION_LABELS = {"GPE", "LOC", "LOCATION", "FAC"}
TEMPORAL_LABELS = {"DATE", "TIME", "DURATION", "SET"}

TEMPORAL_RE = re.compile(
    r"\b("
    r"now|today|yesterday|tomorrow|earlier|years earlier|long ago|last\s+\w+|next\s+\w+|"
    r"before\s+(?:the\s+)?\w+|after\s+(?:the\s+)?\w+|while|when|"
    r"\d+\s+(?:year|years|month|months|week|weeks|day|days|hour|hours)\s+(?:old|ago)|"
    r"\d{4}|Christmas|Thanksgiving"
    r")\b",
    re.I,
)

EVENT_SPLIT_RE = re.compile(
    r"(?<=[.!?])\s+|[,;]\s+|\s+\b(?:and then|but|yet|so|when|while|because)\b\s+",
    re.I,
)

VERB_RE = re.compile(
    r"\b(?:am|is|are|was|were|be|been|being|have|has|had|do|does|did|will|shall|"
    r"would|could|might|must|can|grew|came|come|go|went|walks?|walked|walking|"
    r"travels?|traveled|travelled|searches?|searched|finds?|found|remembers?|"
    r"remembered|recalls?|recalled|lives?|lived|opens?|opened|disappears?|"
    r"disappeared|left|leave|met|meet|looks?|looked|waits?|waited|carried|"
    r"played|won|called|\w+(?:ed|ing))\b",
    re.I,
)

FUTURE_RE = re.compile(r"\b(will|shall|would|might|could|going to|tomorrow|later|soon|future)\b", re.I)
PAST_RE = re.compile(
    r"\b(had|was|were|did|grew|found|saw|came|went|left|met|thought|won|played|"
    r"called|remembered|recalled|years earlier|earlier|long ago|ago|yesterday|"
    r"last\s+\w+|before\b|\w+ed)\b",
    re.I,
)
SIMULTANEOUS_RE = re.compile(r"\b(meanwhile|while|at the same time|simultaneously)\b", re.I)
AFTER_RE = re.compile(r"\b(after|then|later|afterward|afterwards)\b", re.I)
BEFORE_RE = re.compile(r"\b(before|until|earlier)\b", re.I)

CAPITAL_ENTITY_RE = re.compile(r"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b")
LOCATION_PREP_RE = re.compile(
    r"\b(?:in|at|on|to|from|inside|outside|near)\s+("
    r"the\s+[a-z]+(?:\s+[a-z]+)?|a\s+[a-z]+(?:\s+[a-z]+)?|"
    r"[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?"
    r")",
    re.I,
)
ENTITY_STOPWORDS = {
    "A",
    "An",
    "And",
    "He",
    "She",
    "They",
    "This",
    "When",
    "While",
    "Years",
    "Now",
    "Last",
    "Tomorrow",
}


@dataclass
class PocEvent:
    text: str
    action: str = ""
    actors: list[str] = field(default_factory=list)
    locations: list[str] = field(default_factory=list)
    temporal_anchors: list[str] = field(default_factory=list)
    temporal_category: str = "Present"
    relation_to_previous: str | None = None


@dataclass
class PocRun:
    framework: str
    sample_id: str
    status: str
    runtime_ms: float
    sentences: int = 0
    tokens: int = 0
    named_entities: int = 0
    person_entities: int = 0
    location_entities: int = 0
    temporal_entities: int = 0
    temporal_anchors: int = 0
    events: int = 0
    temporal_relations: int = 0
    time_segments: int = 0
    time_tracks: int = 0
    notes: str = ""
    examples: dict[str, Any] = field(default_factory=dict)


def unique(items: Iterable[str]) -> list[str]:
    seen = set()
    result = []
    for item in items:
        clean = re.sub(r"\s+", " ", item).strip()
        if not clean:
            continue
        key = clean.lower()
        if key not in seen:
            seen.add(key)
            result.append(clean)
    return result


def temporal_category(text: str) -> str:
    if FUTURE_RE.search(text):
        return "Future"
    if PAST_RE.search(text):
        return "Past"
    return "Present"


def relation_to_previous(text: str) -> str:
    if SIMULTANEOUS_RE.search(text):
        return "SIMULTANEOUS"
    if BEFORE_RE.search(text):
        return "BEFORE"
    if AFTER_RE.search(text):
        return "AFTER"
    return "IMMEDIATELY_BEFORE"


def relation_count(events: list[PocEvent]) -> int:
    return max(0, len(events) - 1)


def group_time_segments(events: list[PocEvent]) -> list[list[PocEvent]]:
    groups: list[list[PocEvent]] = []
    current: list[PocEvent] = []
    for event in events:
        if not current:
            current.append(event)
            continue
        prev = current[-1]
        if prev.temporal_category == event.temporal_category and set(a.lower() for a in prev.actors) == set(a.lower() for a in event.actors):
            current.append(event)
        else:
            groups.append(current)
            current = [event]
    if current:
        groups.append(current)
    return groups


def track_count(segments: list[list[PocEvent]]) -> int:
    tracks = set()
    for group in segments:
        actors = sorted({actor.lower() for event in group for actor in event.actors})
        category = group[0].temporal_category if group else ""
        tracks.add(("&".join(actors), category))
    return len(tracks)


def regex_temporal_anchors(text: str) -> list[str]:
    return unique(m.group(0) for m in TEMPORAL_RE.finditer(text))


def regex_locations(text: str) -> list[str]:
    return unique(m.group(1) for m in LOCATION_PREP_RE.finditer(text))


def regex_actors(text: str, locations: list[str]) -> list[str]:
    location_keys = {loc.lower() for loc in locations}
    temporal_keys = {anchor.lower() for anchor in regex_temporal_anchors(text)}
    actors = []
    for candidate in CAPITAL_ENTITY_RE.findall(text):
        if candidate in ENTITY_STOPWORDS:
            continue
        key = candidate.lower()
        if key in location_keys or key in temporal_keys:
            continue
        actors.append(candidate)
    return unique(actors)


def make_run(framework: str, sample_id: str, status: str, runtime_ms: float, events: list[PocEvent], sentences: int, tokens: int, named_entities: int, persons: int, locations: int, temporals: int, notes: str) -> PocRun:
    segments = group_time_segments(events)
    anchors = sum(len(event.temporal_anchors) for event in events)
    return PocRun(
        framework=framework,
        sample_id=sample_id,
        status=status,
        runtime_ms=round(runtime_ms, 2),
        sentences=sentences,
        tokens=tokens,
        named_entities=named_entities,
        person_entities=persons,
        location_entities=locations,
        temporal_entities=temporals,
        temporal_anchors=anchors,
        events=len(events),
        temporal_relations=relation_count(events),
        time_segments=len(segments),
        time_tracks=track_count(segments),
        notes=notes,
        examples={
            "events": [asdict(event) for event in events[:8]],
            "segments": [[event.text for event in group] for group in segments[:8]],
        },
    )


def run_article_rules(sample_id: str, text: str) -> PocRun:
    start = time.perf_counter()
    chunks = [chunk.strip() for chunk in EVENT_SPLIT_RE.split(text) if chunk.strip()]
    events: list[PocEvent] = []
    previous_actors: list[str] = []
    previous_anchors: list[str] = []
    for chunk in chunks:
        if not VERB_RE.search(chunk):
            if events:
                events[-1].text = f"{events[-1].text} {chunk}"
            continue
        locations = regex_locations(chunk)
        actors = regex_actors(chunk, locations) or previous_actors
        anchors = regex_temporal_anchors(chunk) or previous_anchors
        action_match = VERB_RE.search(chunk)
        event = PocEvent(
            text=chunk,
            action=action_match.group(0) if action_match else "",
            actors=actors,
            locations=locations,
            temporal_anchors=anchors,
            temporal_category=temporal_category(chunk),
            relation_to_previous=relation_to_previous(chunk) if events else None,
        )
        events.append(event)
        if actors:
            previous_actors = actors
        if anchors:
            previous_anchors = anchors
    elapsed = (time.perf_counter() - start) * 1000
    return make_run(
        "article_rules_no_llm",
        sample_id,
        "available",
        elapsed,
        events,
        sentences=len(re.findall(r"[^.!?]+[.!?]?", text)),
        tokens=len(re.findall(r"\w+|[^\w\s]", text)),
        named_entities=len(regex_actors(text, regex_locations(text))) + len(regex_locations(text)) + len(regex_temporal_anchors(text)),
        persons=len(regex_actors(text, regex_locations(text))),
        locations=len(regex_locations(text)),
        temporals=len(regex_temporal_anchors(text)),
        notes="Pure rule-based approximation of the article pipeline: clause splitting, capitalized actor heuristic, temporal regexes, tense regexes.",
    )


def run_spacy(sample_id: str, text: str) -> PocRun:
    start = time.perf_counter()
    try:
        spacy = importlib.import_module("spacy")
        nlp = spacy.load("en_core_web_sm")
        doc = nlp(text)
    except Exception as exc:
        return PocRun("spacy_en_core_web_sm", sample_id, "unavailable", 0, notes=str(exc))

    events: list[PocEvent] = []
    for sent in doc.sents:
        sent_ents = [ent for ent in sent.ents]
        persons = unique(ent.text for ent in sent_ents if ent.label_ in PERSON_LABELS)
        locations = unique(ent.text for ent in sent_ents if ent.label_ in LOCATION_LABELS) or regex_locations(sent.text)
        anchors = unique(ent.text for ent in sent_ents if ent.label_ in TEMPORAL_LABELS) or regex_temporal_anchors(sent.text)
        verbs = [token for token in sent if token.pos_ in {"VERB", "AUX"}]
        if not verbs:
            continue
        event = PocEvent(
            text=sent.text.strip(),
            action=verbs[0].lemma_,
            actors=persons,
            locations=locations,
            temporal_anchors=anchors,
            temporal_category=temporal_category(sent.text),
            relation_to_previous=relation_to_previous(sent.text) if events else None,
        )
        events.append(event)
    elapsed = (time.perf_counter() - start) * 1000
    ents = list(doc.ents)
    return make_run(
        "spacy_en_core_web_sm",
        sample_id,
        "available",
        elapsed,
        events,
        sentences=len(list(doc.sents)),
        tokens=len(doc),
        named_entities=len(ents),
        persons=sum(1 for ent in ents if ent.label_ in PERSON_LABELS),
        locations=sum(1 for ent in ents if ent.label_ in LOCATION_LABELS),
        temporals=sum(1 for ent in ents if ent.label_ in TEMPORAL_LABELS),
        notes="Real spaCy model: tokenization, sentence segmentation, POS, dependency parse, NER. Temporal anchors fall back to regex if no DATE/TIME entity is found.",
    )


def run_stanza(sample_id: str, text: str, model_dir: Path) -> PocRun:
    start = time.perf_counter()
    try:
        stanza = importlib.import_module("stanza")
        nlp = stanza.Pipeline(
            "en",
            processors="tokenize,pos,lemma,depparse,ner",
            model_dir=str(model_dir),
            verbose=False,
            download_method=None,
        )
        doc = nlp(text)
    except Exception as exc:
        return PocRun("stanza_en", sample_id, "unavailable", 0, notes=str(exc))

    events: list[PocEvent] = []
    ents = list(doc.ents)
    for sentence in doc.sentences:
        sent_text = sentence.text
        sent_start_entities = [ent for ent in ents if ent.text in sent_text]
        persons = unique(ent.text for ent in sent_start_entities if ent.type in PERSON_LABELS)
        locations = unique(ent.text for ent in sent_start_entities if ent.type in LOCATION_LABELS) or regex_locations(sent_text)
        anchors = unique(ent.text for ent in sent_start_entities if ent.type in TEMPORAL_LABELS) or regex_temporal_anchors(sent_text)
        verbs = [word for word in sentence.words if word.upos in {"VERB", "AUX"}]
        if not verbs:
            continue
        events.append(
            PocEvent(
                text=sent_text.strip(),
                action=verbs[0].lemma,
                actors=persons,
                locations=locations,
                temporal_anchors=anchors,
                temporal_category=temporal_category(sent_text),
                relation_to_previous=relation_to_previous(sent_text) if events else None,
            )
        )
    elapsed = (time.perf_counter() - start) * 1000
    tokens = sum(len(sentence.words) for sentence in doc.sentences)
    return make_run(
        "stanza_en",
        sample_id,
        "available",
        elapsed,
        events,
        sentences=len(doc.sentences),
        tokens=tokens,
        named_entities=len(ents),
        persons=sum(1 for ent in ents if ent.type in PERSON_LABELS),
        locations=sum(1 for ent in ents if ent.type in LOCATION_LABELS),
        temporals=sum(1 for ent in ents if ent.type in TEMPORAL_LABELS),
        notes="Real Stanza neural pipeline: tokenization, POS, lemma, dependency parse, NER. Temporal anchors fall back to regex if no DATE/TIME entity is found.",
    )


def run_java_tool_placeholder(framework: str, sample_id: str, text: str, expected_artifact: str) -> PocRun:
    java = shutil.which("java")
    if not java:
        return PocRun(
            framework,
            sample_id,
            "unavailable",
            0,
            notes=f"Java runtime is not on PATH. Install Java and {expected_artifact} to run this POC.",
        )
    return PocRun(
        framework,
        sample_id,
        "unavailable",
        0,
        notes=f"Java is available, but {expected_artifact} is not configured in this POC folder.",
    )


def run_current_tym_sample(sample_id: str, text: str, api_sample_path: Path) -> PocRun:
    if not api_sample_path.exists():
        return PocRun("current_tym_api_sample", sample_id, "unavailable", 0, notes=f"Missing {api_sample_path}")
    if sample_id != "tym_sample":
        return PocRun("current_tym_api_sample", sample_id, "not_applicable", 0, notes="Current saved API sample only corresponds to tym_sample.")
    data = json.loads(api_sample_path.read_text(encoding="utf-8"))
    diagram = data.get("diagram", {})
    events = diagram.get("events", [])
    segments = diagram.get("segments", [])
    tracks = diagram.get("tracks", [])
    actors = diagram.get("actors", [])
    locations = diagram.get("locations", [])
    relations = diagram.get("relations", [])
    anchors = [event.get("temporal_anchor", "") for event in events if event.get("temporal_anchor")]
    return PocRun(
        "current_tym_api_sample",
        sample_id,
        "available_from_saved_output",
        0,
        sentences=0,
        tokens=0,
        named_entities=len(actors) + len(locations),
        person_entities=max(0, len(actors) - 1),
        location_entities=max(0, len(locations) - 1),
        temporal_entities=len(anchors),
        temporal_anchors=len(anchors),
        events=len(events),
        temporal_relations=len(relations),
        time_segments=len(segments),
        time_tracks=len(tracks),
        notes="Existing .NET API output. Counts come from sample_response.json rather than rerunning the API.",
        examples={
            "events": events[:8],
            "segments": [segment.get("text", "") for segment in segments[:8]],
        },
    )


def markdown_report(runs: list[PocRun], output_path: Path) -> None:
    headers = [
        "framework",
        "sample",
        "status",
        "runtime_ms",
        "events",
        "persons",
        "locations",
        "temporal_anchors",
        "relations",
        "TS",
        "TT",
    ]
    lines = [
        "# Non-LLM NLP POC Statistics",
        "",
        "This report compares non-LLM NLP approaches for the Time Yards Model steps described in the article: event segmentation, actor/NER detection, temporal anchors, temporal event categories, temporal relations, time segments, and time tracks.",
        "",
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join(["---"] * len(headers)) + " |",
    ]
    for run in runs:
        values = [
            run.framework,
            run.sample_id,
            run.status,
            f"{run.runtime_ms:.2f}",
            str(run.events),
            str(run.person_entities),
            str(run.location_entities),
            str(run.temporal_anchors),
            str(run.temporal_relations),
            str(run.time_segments),
            str(run.time_tracks),
        ]
        lines.append("| " + " | ".join(values) + " |")

    lines.extend(
        [
            "",
            "## Interpretation",
            "",
            "- `article_rules_no_llm` is the smallest reproducible version of the article pipeline. It is transparent and fast, but rough.",
            "- `spacy_en_core_web_sm` gives practical NER, POS, and dependency features. This is a good engineering baseline.",
            "- `stanza_en` gives a research-oriented neural pipeline with UD-style annotations and NER. It is slower but closer to research-grade preprocessing.",
            "- `current_tym_api_sample` shows what the existing .NET API already emits for the primary sample.",
            "- `sutime_corenlp` and `heideltime` are intentionally reported as unavailable when Java/tool jars are absent. They remain the right POCs for stronger TIMEX3-style temporal expressions.",
            "",
            "## Recommended Non-LLM Implementation",
            "",
            "1. Use Stanza or spaCy for tokenization, POS, dependency parsing, and NER.",
            "2. Add SUTime or HeidelTime for TIMEX3 temporal expressions once Java is available.",
            "3. Feed extracted features into ML.NET: tense category, verb count, conjunction class, actor count, temporal-anchor type, and relation cues.",
            "4. Train TS boundary and relation classifiers from annotated TYM XML instead of seed examples.",
            "5. Keep the SVG/XML renderer deterministic.",
        ]
    )
    output_path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--samples", default="sample_texts.json")
    parser.add_argument("--output-dir", default="results")
    parser.add_argument("--stanza-model-dir", default="../../../../work/tym-nlp-pocs/stanza_resources")
    parser.add_argument("--api-sample", default="../../sample_response.json")
    args = parser.parse_args()

    base = Path(__file__).resolve().parent
    sample_path = (base / args.samples).resolve()
    output_dir = (base / args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    stanza_model_dir = (base / args.stanza_model_dir).resolve()
    api_sample_path = (base / args.api_sample).resolve()

    samples = json.loads(sample_path.read_text(encoding="utf-8"))
    runs: list[PocRun] = []
    for sample in samples:
        sample_id = sample["id"]
        text = sample["text"]
        runs.append(run_article_rules(sample_id, text))
        runs.append(run_spacy(sample_id, text))
        runs.append(run_stanza(sample_id, text, stanza_model_dir))
        runs.append(run_java_tool_placeholder("sutime_corenlp", sample_id, text, "Stanford CoreNLP/SUTime jars"))
        runs.append(run_java_tool_placeholder("heideltime", sample_id, text, "HeidelTime distribution"))
        runs.append(run_current_tym_sample(sample_id, text, api_sample_path))

    (output_dir / "poc_results.json").write_text(
        json.dumps([asdict(run) for run in runs], indent=2, ensure_ascii=False),
        encoding="utf-8",
    )
    with (output_dir / "poc_stats.csv").open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(asdict(runs[0]).keys())[:-1])
        writer.writeheader()
        for run in runs:
            row = asdict(run)
            row.pop("examples", None)
            writer.writerow(row)
    markdown_report(runs, output_dir / "poc_report.md")

    print(json.dumps({"runs": len(runs), "output_dir": str(output_dir)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
