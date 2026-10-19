"""Prepare complete local book inputs and separate, explicitly labeled excerpts."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path


# Volumes, translations, and the PDF edition deliberately share source-work IDs.
BOOKS = [
    ("Shogun-1-James-Clavell.txt", "shogun-1", "Shogun, vol. 1", "ro", "shogun"),
    ("Shogun-2-James-Clavell.txt", "shogun-2", "Shogun, vol. 2", "ro", "shogun"),
    ("Tinutul-blanurilor-vol.-1-Jules-Verne.txt", "tinutul-1", "Tinutul blanurilor, vol. 1", "ro", "tinutul-blanurilor"),
    ("Tinutul-blanurilor-vol-2-Jules-Verne.txt", "tinutul-2", "Tinutul blanurilor, vol. 2", "ro", "tinutul-blanurilor"),
    ("Ultima-sansa-Arthur-Hailey.txt", "ultima-sansa", "Ultima sansa", "ro", "ultima-sansa"),
    ("Un-colind-de-Craciun-Charles-Dickens.txt", "colind", "Un colind de Craciun", "ro", "un-colind-de-craciun"),
    ("Uniforme-de-general-Mircea-Eliade.txt", "uniforme", "Uniforme de general", "ro", "uniforme-de-general"),
    ("Zeii-insisi-Isaac-Asimov.txt", "zeii", "Zeii insisi", "ro", "zeii-insisi"),
    ("20000 de leghe sub mari - Jules Verne.txt", "leghe", "20000 de leghe sub mari", "ro", "20000-de-leghe-sub-mari"),
    ("Arta tacerii - B. Blanchard.txt", "arta-tacerii", "Arta tacerii", "ro", "arta-tacerii"),
    ("Harta lumii nevazute.txt", "tash-aw-ro", "Harta lumii nevazute (RO)", "ro", "map-of-the-invisible-world"),
    ("Map of the Invisible World - Tash Aw.txt", "tash-aw-en", "Map of the Invisible World (EN)", "en", "map-of-the-invisible-world"),
    ("Morometii - vol.1 - Marin Preda.txt", "morometii-1", "Morometii, vol. 1", "ro", "morometii"),
    ("Morometii-vol.2-Marin-Preda.txt", "morometii-2", "Morometii, vol. 2", "ro", "morometii"),
    ("scaune_de_plus_ion.txt", "scaune", "scaune_de_plus_ion", "ro", "scaune-de-plus"),
]


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")


def decode_text(data: bytes) -> tuple[str, str]:
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16"), "utf-16-bom"
    try:
        return data.decode("utf-8-sig"), "utf-8-sig" if data.startswith(b"\xef\xbb\xbf") else "utf-8"
    except UnicodeDecodeError:
        # This is an explicit fallback choice, not statistical encoding detection.
        return data.decode("cp1250"), "cp1250-fallback-review-required"


def save_full_text(output: Path, example_id: str, text: str, language: str) -> dict:
    data = text.encode("utf-8")
    target = output / "full-text" / f"{example_id}.txt"
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    write_json(output / "requests" / "full-diagram" / f"{example_id}.json", {
        "text": text,
        "options": {"language": language, "layout": "both", "width": 1200},
    })
    return {
        "full_text_url": f"/local-examples/{example_id}/text",
        "full_text_characters": len(text.encode("utf-16-le")) // 2,
        "full_text_sha256_utf8": digest(data),
    }


def opening_excerpt(text: str, limit: int = 1200) -> tuple[int, int]:
    for line in re.finditer(r"[^\r\n]+", text):
        candidate = line.group().strip()
        if len(candidate) < 180 or not re.search(r"[.!?]", candidate):
            continue
        start = line.start() + len(line.group()) - len(line.group().lstrip())
        end = min(start + limit, len(text))
        # End at a sentence punctuation mark where available; preserve the source slice.
        endings = [m.end() for m in re.finditer(r"[.!?](?:[\"\u201d\u00bb])?(?=\s|$)", text[start:end])]
        if endings and endings[-1] >= 180:
            end = start + endings[-1]
        else:
            whitespace = text.rfind(" ", start + 180, end)
            if whitespace > start:
                end = whitespace
        return start, end
    raise ValueError("No prose passage found; choose a reviewed excerpt manually.")


def prepare(pdf_path: Path, texts_root: Path, output: Path) -> None:
    from pypdf import PdfReader

    examples: list[dict] = []
    sources: list[dict] = []
    for filename, example_id, label, language, group in BOOKS:
        path = texts_root / filename
        data = path.read_bytes()
        text, encoding = decode_text(data)
        start, end = opening_excerpt(text)
        excerpt = text[start:end]
        full_text = save_full_text(output, example_id, text, language)
        start16 = len(text[:start].encode("utf-16-le")) // 2
        end16 = start16 + len(excerpt.encode("utf-16-le")) // 2
        source = {
            "id": example_id,
            "path": str(path.resolve()),
            "filename": filename,
            "sha256": digest(data),
            "byte_count": len(data),
            "encoding": encoding,
            "language": language,
            "source_work_group": group,
            "annotation_status": "unlabeled",
            "bibliography_status": "filename_supplied_unverified",
            "decoded_code_points": len(text),
            "full_text_utf16_length": full_text["full_text_characters"],
            "full_text_sha256_utf8": full_text["full_text_sha256_utf8"],
            "excerpt_code_point_span": {"start": start, "end": end},
            "excerpt_utf16_span": {"start": start16, "end": end16},
            "excerpt_sha256_utf8": digest(excerpt.encode("utf-8")),
            "preparation": "exact source slice; opening-prose heuristic v1; end exclusive",
        }
        sources.append(source)
        examples.append({
            "id": example_id, "label": label, "language": language,
            "highlight": "Adam" if group == "map-of-the-invisible-world" else "",
            "text": excerpt, "is_private": True,
            "source": f"{filename}; UTF-16 [{start16}, {end16}); unlabeled local excerpt",
            "full_source": f"{filename}; complete text; unlabeled local input",
            **full_text,
        })

    pdf_data = pdf_path.read_bytes()
    pdf_pages = [page.extract_text() or "" for page in PdfReader(pdf_path).pages]
    page_text = pdf_pages[8]
    full_pdf = save_full_text(output, "tash-aw-ro-pdf", "\n\f\n".join(pdf_pages), "ro")
    raw = page_text[page_text.index("Stai! Adam"):]
    raw = re.sub(r"\s+9\s*$", "", raw)
    passage = re.sub(r"\s+", " ", raw).strip().replace("as cuns", "ascuns")
    if not passage.endswith("frica."):
        raise ValueError("PDF passage boundary differs from the visually reviewed page.")
    sources.append({
        "id": "tash-aw-ro-pdf", "path": str(pdf_path.resolve()),
        "filename": pdf_path.name, "sha256": digest(pdf_data), "byte_count": len(pdf_data),
        "language": "ro", "source_work_group": "map-of-the-invisible-world",
        "annotation_status": "unlabeled", "pdf_page": 9, "printed_page": 9,
        "author": "Tash Aw", "translator": "Florica Sincu", "romanian_edition_year": 2012,
        "preparation": ["pypdf text extraction", "final paragraph selected and visually checked",
                        "page footer removed", "line whitespace joined", "as cuns -> ascuns: extraction gap repaired"],
        "excerpt_sha256_utf8": digest(passage.encode("utf-8")),
        "full_text_utf16_length": full_pdf["full_text_characters"],
        "full_text_sha256_utf8": full_pdf["full_text_sha256_utf8"],
        "full_text_page_count": len(pdf_pages),
        "full_text_preparation": "All PDF pages extracted in order, including front matter; page boundaries retained as form feeds; extraction artifacts retained",
        "coordinate_note": "PDF page anchor; no offsets borrowed from separate text editions",
    })
    examples.insert(0, {
        "id": "tash-aw-ro-pdf", "label": "Tash Aw (RO, PDF)", "language": "ro",
        "highlight": "Adam", "text": passage, "is_private": True,
        "source": "Tash Aw, Harta lumii nevazute; tr. Florica Sincu, 2012; p. 9 / PDF p. 9; unlabeled local excerpt",
        "full_source": f"Tash Aw, Harta lumii nevazute; tr. Florica Sincu, 2012; complete {len(pdf_pages)}-page PDF extraction; unlabeled local input",
        "full_text_pages": len(pdf_pages),
        **full_pdf,
    })

    for example in examples:
        write_json(output / "requests" / "diagram" / f"{example['id']}.json", {
            "text": example["text"],
            "options": {"language": example["language"], "layout": "both", "width": 1200,
                        "highlight_entities": [example["highlight"]] if example["highlight"] else []},
        })
        write_json(output / "requests" / "corpus" / f"{example['id']}.json", {
            "text": example["text"], "language": example["language"],
        })

    write_json(output / "examples.json", examples)
    write_json(output / "manifest.json", {
        "schema_version": 2, "selection_version": "complete-text+opening-prose-v1+reviewed-pdf-p9",
        "source_file_count": len(sources), "text_file_count": len(BOOKS),
        "source_work_group_count": len({s["source_work_group"] for s in sources}),
        "example_count": len(examples), "annotation_status": "unlabeled", "sources": sources,
    })
    print(json.dumps({"source_files": len(sources), "examples": len(examples),
                      "source_work_groups": len({s["source_work_group"] for s in sources}),
                      "output": str(output.resolve()),
                      "encoding_review_required": sum("review-required" in s.get("encoding", "") for s in sources)}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pdf", type=Path, required=True)
    parser.add_argument("--texts-root", type=Path, required=True)
    parser.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[1] / "private-examples")
    args = parser.parse_args()
    prepare(args.pdf, args.texts_root, args.out)
