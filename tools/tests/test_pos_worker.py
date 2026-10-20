import importlib.util
from pathlib import Path
import re
from types import SimpleNamespace
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("tym_pos_worker", Path(__file__).parents[1] / "tym_pos_worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class FakeModel:
    meta = {"lang": "en", "name": "test", "version": "1.0"}
    pipe_names = ["tagger", "ner"]

    def __init__(self, pos="PROPN", tag="NNP", entity_text=None):
        self.pos, self.tag, self.chunks, self.entity_text = pos, tag, [], entity_text

    def __call__(self, text):
        self.chunks.append(text)
        doc = FakeDoc(FakeToken(match.group(), match.start(), self.pos, self.tag) for match in re.finditer(r"\S+", text))
        doc.ents = [SimpleNamespace(start_char=match.start(), end_char=match.end(), label_="ORG")
                    for match in re.finditer(re.escape(self.entity_text), text)] if self.entity_text else []
        return doc


class FakeDoc(list):
    pass


class FakeToken(SimpleNamespace):
    def __init__(self, text, start, pos, tag):
        super().__init__(text=text, idx=start, pos_=pos, tag_=tag, morph={"Poss": []}, is_space=False)

    def __len__(self):
        return len(self.text)


class PosWorkerChecks(unittest.TestCase):
    def annotate(self, text, model=None):
        worker.models["en"] = model or FakeModel()
        return worker.annotate(text, "en")

    def test_unmapped_fine_tags_are_excluded_as_unknown(self):
        response = self.annotate("word", FakeModel(pos="", tag="Vmsp2p"))
        self.assertEqual(response["tokens"][0]["pos"], "X")
        self.assertEqual(response["tokens"][0]["tag"], "Vmsp2p")

    def test_utf16_offsets_after_astral_character(self):
        response = self.annotate("\U0001f642 Adam")
        self.assertEqual(response["character_count"], 7)
        self.assertEqual([(token["span_start"], token["span_end"]) for token in response["tokens"]], [(0, 2), (3, 7)])

    def test_bounded_chunks_preserve_all_spans(self):
        text = "Adam " * 7000
        model = FakeModel()
        response = self.annotate(text, model)
        self.assertGreater(len(model.chunks), 1)
        self.assertLessEqual(max(map(len, model.chunks)), 16000)
        self.assertEqual("".join(model.chunks), text)
        self.assertEqual(len(response["tokens"]), 7000)
        for token in response["tokens"]:
            self.assertEqual(text[token["span_start"]:token["span_end"]], "Adam")

    def test_possessive_fine_tag_is_retained(self):
        response = self.annotate("Her", FakeModel(pos="PRON", tag="PRP$"))
        self.assertTrue(response["tokens"][0]["possessive"])

    def test_trained_entity_spans_use_utf16_and_preserve_internal_words(self):
        response = self.annotate("\U0001f642 Bank of England", FakeModel(entity_text="Bank of England"))
        self.assertEqual(response["entities"], [{"span_start": 3, "span_end": 18, "label": "ORG"}])
        self.assertIn("ner", response["pipeline_components"])

    def test_entities_after_chunk_boundaries_keep_global_offsets(self):
        text = "word " * 4000 + "Bank of England"
        response = self.annotate(text, FakeModel(entity_text="Bank of England"))
        self.assertEqual(response["entities"], [{"span_start": 20000, "span_end": len(text), "label": "ORG"}])

    def test_loading_retains_ner(self):
        worker.models.pop("en", None)
        with patch.object(worker.spacy, "load", return_value=FakeModel()) as load:
            worker.annotate("Adam", "en")
        self.assertNotIn("ner", load.call_args.kwargs["exclude"])

    def test_model_without_ner_is_rejected(self):
        worker.models.pop("en", None)
        model = FakeModel()
        model.pipe_names = ["tagger"]
        with patch.object(worker.spacy, "load", return_value=model):
            with self.assertRaisesRegex(ValueError, "trained NER"):
                worker.annotate("Adam", "en")


if __name__ == "__main__":
    unittest.main()
