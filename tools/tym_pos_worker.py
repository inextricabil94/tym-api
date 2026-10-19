"""Local JSON-lines POS worker. No text is sent to a network service."""
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
        models[language] = spacy.load(model, exclude=["parser", "ner", "lemmatizer", "senter"])
    nlp = models[language]
    tokens = []
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
        for token in nlp(chunk):
            if token.is_space:
                continue
            tokens.append({"span_start": offset + utf16[token.idx], "span_end": offset + utf16[token.idx + len(token)],
                           "pos": token.pos_ or "X", "tag": token.tag_, "possessive": "Yes" in token.morph.get("Poss") or token.tag_ == "PRP$"})
        offset += utf16[-1]
        start = end
    return {"provider": "spaCy", "model": nlp.meta["lang"] + "_" + nlp.meta["name"] + "/" + nlp.meta["version"],
            "character_count": offset, "tokens": tokens}


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
