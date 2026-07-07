# Non-LLM NLP POC Statistics

This report compares non-LLM NLP approaches for the Time Yards Model steps described in the article: event segmentation, actor/NER detection, temporal anchors, temporal event categories, temporal relations, time segments, and time tracks.

| framework | sample | status | runtime_ms | events | persons | locations | temporal_anchors | relations | TS | TT |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| article_rules_no_llm | tym_sample | available | 0.63 | 7 | 5 | 3 | 3 | 6 | 6 | 6 |
| spacy_en_core_web_sm | tym_sample | available | 5077.61 | 7 | 11 | 1 | 3 | 6 | 6 | 6 |
| stanza_en | tym_sample | available | 9923.99 | 7 | 11 | 1 | 3 | 6 | 6 | 6 |
| sutime_corenlp | tym_sample | unavailable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| heideltime | tym_sample | unavailable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| current_tym_api_sample | tym_sample | available_from_saved_output | 0.00 | 7 | 4 | 3 | 6 | 10 | 6 | 6 |
| article_rules_no_llm | article_example | available | 0.31 | 4 | 1 | 1 | 2 | 3 | 2 | 2 |
| spacy_en_core_web_sm | article_example | available | 798.70 | 4 | 1 | 0 | 3 | 3 | 4 | 3 |
| stanza_en | article_example | available | 6194.35 | 3 | 1 | 0 | 2 | 2 | 2 | 2 |
| sutime_corenlp | article_example | unavailable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| heideltime | article_example | unavailable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| current_tym_api_sample | article_example | not_applicable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| article_rules_no_llm | relation_mix | available | 0.36 | 5 | 6 | 4 | 3 | 4 | 4 | 4 |
| spacy_en_core_web_sm | relation_mix | available | 606.24 | 4 | 6 | 2 | 3 | 3 | 4 | 4 |
| stanza_en | relation_mix | available | 6696.01 | 4 | 6 | 2 | 3 | 3 | 4 | 4 |
| sutime_corenlp | relation_mix | unavailable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| heideltime | relation_mix | unavailable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| current_tym_api_sample | relation_mix | not_applicable | 0.00 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |

## Interpretation

- `article_rules_no_llm` is the smallest reproducible version of the article pipeline. It is transparent and fast, but rough.
- `spacy_en_core_web_sm` gives practical NER, POS, and dependency features. This is a good engineering baseline.
- `stanza_en` gives a research-oriented neural pipeline with UD-style annotations and NER. It is slower but closer to research-grade preprocessing.
- `current_tym_api_sample` shows what the existing .NET API already emits for the primary sample.
- `sutime_corenlp` and `heideltime` are intentionally reported as unavailable when Java/tool jars are absent. They remain the right POCs for stronger TIMEX3-style temporal expressions.

## Recommended Non-LLM Implementation

1. Use Stanza or spaCy for tokenization, POS, dependency parsing, and NER.
2. Add SUTime or HeidelTime for TIMEX3 temporal expressions once Java is available.
3. Feed extracted features into ML.NET: tense category, verb count, conjunction class, actor count, temporal-anchor type, and relation cues.
4. Train TS boundary and relation classifiers from annotated TYM XML instead of seed examples.
5. Keep the SVG/XML renderer deterministic.
