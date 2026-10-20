# Trained English/Romanian NER

On 20 October 2026, the existing local spaCy worker was changed to retain the trained `ner` component of `en_core_web_sm` and `ro_core_news_sm` 3.8.0. There is no new service, paid resource, LLM or model download at request time. Parser, lemmatizer and sentence-recognizer components remain excluded. This is pretrained inference, not new model training on the supplied books.

## Projection Into Highlight Entity

- PERSON/PER maps to `PERSON`.
- GPE/LOC/FAC/FACILITY maps to `LOCATION`.
- ORG/ORGANIZATION maps to `ORGANIZATION`.
- Other trained classes remain counted in metadata but are not offered as people/places/organizations. This does not change TimeML extraction.
- A retained model span must contain at least one `NOUN` or `PROPN`. Its full extent is preserved, even when internal words have `ADP`, `DET` or `ADJ` POS. Standalone verbs, interjections and function words are not eligible merely because the model predicted a name over them.
- Existing PROPN rules fill uncovered spans only. They cannot create a nested/partial name inside any model entity, including a DATE or number.
- Pronoun resolution remains conservative and rule-based. Organizations do not become person antecedents. Unresolved pronouns remain a separate group.

The response method is `spacy-trained-ner+pos+rule-anaphora-en-ro-v4`. Every option records `recognition_source`: `trained_ner`, `proper_name_rule` or `unresolved_reference`. `model_label` preserves the original trained label when applicable. For repeated identical label/name pairs, the source, POS and model label describe the first explicit occurrence; mention counts aggregate occurrences. This is not a claim that every occurrence was model-detected. `ner` records provider, model/version, active components and raw counts for all predicted classes. No calibrated confidence is invented.

The picker groups organizations separately in English and Romanian. It preserves original spelling and source spans. Offsets are UTF-16, including supplementary Unicode characters, and highlight selection uses explicit/resolved spans. Event actors, TS boundaries, tense, TT grouping, temporal relations and TimeML are not replaced by this increment. Lookup remains separate from the daily diagram allowance.

## Known Limits

News-domain small models can omit or misclassify literary names. In the authored sentence `Adam și Johan au plecat. Ei au revenit.`, the installed Romanian model predicts `Johan` as LOC. The adapter retains that model type and does not force a guessed person correction; the plural reference stays unresolved. A separate deterministic fixture verifies that the existing resolver still handles coordination when supplied correct person annotations. Better NER is not an established accuracy improvement without a held-out annotated evaluation.

The existing worker still uses bounded 16,000-code-point chunks. An entity crossing a hard chunk boundary can be split or missed; no cross-chunk or cross-chapter entity linking is claimed. Worker access and cache character limits remain operational constraints. Worker responses have a 60-second budget for inputs up to 100,000 UTF-16 units and 180 seconds for larger inputs; this is not a latency guarantee or cooperative cancellation. Entity/model counts are not precision, recall or memory measurements. The response fails with 503 if the model has no active NER rather than silently claiming trained inference.

A detected name does not create an event/track by itself. The old rule-based event recognizer can omit sentences such as `John Smith works at the Bank of England.`; a selected name can highlight only a segment whose source range actually covers that mention. The NER increment does not silently rewrite this separate event algorithm.

## Verification

Eight worker unit tests cover NER activation, rejection of a missing component, entity UTF-16 offsets, later-chunk offsets, unknown tags, possessives and bounded input processing. Generation checks cover actual English/Romanian people and organizations, complete internal-function-word spans, provenance, suppression of nested name/date fragments, POS filtering, anaphora and existing diagram/density behavior. Bilingual browser fixtures check organization options and exclusion of a standalone adverb. These are regression checks, not an annotated NLP benchmark.

A complete local Romanian input (`shogun-1`) completed trained NER in 45.75 seconds, returning 3,672 selectable entities and 26,407 reference spans with `ro_core_news_sm/3.8.0` and active `ner`. This is one cold local request, not a p95, Azure performance result or accuracy estimate. The 202 density regression cases and the real book/density browser suite also passed.

After deployment, the same complete input returned the same counts and model metadata on Azure in 87.23 seconds, without a container restart. Actual browser/API checks on both live UI hosts verified complete organization spans, localized groups, UTF-16 source slices, public chapter reading and all ten cached densities. See [release verification](verification/i18n-ner-20261020.md).

Run `python -B -m unittest discover -s tools/tests -p test_pos_worker.py -v` in the configured spaCy environment and `dotnet run --project tools/Tym.GenerationChecks -c Release -- --entities-only`. Omit the last argument for the full generation/density checks. Release verification is recorded separately.

Primary references: [spaCy EntityRecognizer](https://spacy.io/api/entityrecognizer), [English models](https://spacy.io/models/en), [Romanian models](https://spacy.io/models/ro). Existing model license notices remain in `THIRD_PARTY_NOTICES.md` and the container's installed distributions.
