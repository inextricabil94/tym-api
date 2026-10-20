"""Local JSON-lines POS and trained NER worker. No text leaves this process."""
import json
import os
import sys

import spacy

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
models = {}


def annotate(text, language):
    if language not in models:
        model = os.environ.get("TYM_POS_RO_MODEL", "ro_core_news_sm") if language == "ro" else os.environ.get("TYM_POS_EN_MODEL", "en_core_web_sm")
        nlp = spacy.load(model, exclude=["parser", "lemmatizer", "senter"])
        if "ner" not in nlp.pipe_names:
            raise ValueError("Configured model must include trained NER")
        models[language] = nlp
    nlp = models[language]
    tokens = []
    entities = []
    offset = 0
    start = 0
    # Bounded chunks retain contextual tagging without constructing a book-sized spaCy Doc.
    while start < len(text):
        end = min(start + 16000, len(text))
        if end < len(text):
            boundary = max(text.rfind("\n", start + 8000, end), text.rfind(" ", start + 8000, end))
            if boundary > start:
                end = boundary + 1
        chunk = text[start:end]
        utf16 = [0]
        for character in chunk:
            utf16.append(utf16[-1] + (2 if ord(character) > 0xFFFF else 1))
        doc = nlp(chunk)
        for entity in doc.ents:
            entities.append({"span_start": offset + utf16[entity.start_char],
                             "span_end": offset + utf16[entity.end_char], "label": entity.label_})
        for token in doc:
            if token.is_space:
                continue
            tokens.append({"span_start": offset + utf16[token.idx], "span_end": offset + utf16[token.idx + len(token)],
                           "pos": token.pos_ or "X", "tag": token.tag_, "possessive": "Yes" in token.morph.get("Poss") or token.tag_ == "PRP$"})
        offset += utf16[-1]
        start = end
    return {"provider": "spaCy", "model": nlp.meta["lang"] + "_" + nlp.meta["name"] + "/" + nlp.meta["version"],
            "character_count": offset, "tokens": tokens, "entities": entities,
            "pipeline_components": list(nlp.pipe_names)}


def main():
    for line in sys.stdin:
        try:
            request = json.loads(line)
            response = annotate(request["text"], request["language"])
        except Exception as error:
            response = {"error": type(error).__name__}
        try:
            print(json.dumps(response, ensure_ascii=True, separators=(",", ":")), flush=True)
        except BrokenPipeError:
            break


if __name__ == "__main__":
    main()
