# Non-LLM NLP Framework POCs

This folder contains a reproducible comparison for non-LLM NLP approaches that can support the Time Yards Model pipeline described in the article.

The runner compares:

- `article_rules_no_llm`: transparent rule-based event/TS/TT extraction
- `spacy_en_core_web_sm`: spaCy sentence/POS/dependency/NER features
- `stanza_en`: Stanza neural POS/dependency/NER features
- `sutime_corenlp`: placeholder POC that reports availability until Java/CoreNLP are installed
- `heideltime`: placeholder POC that reports availability until Java/HeidelTime are installed
- `current_tym_api_sample`: counts from the existing .NET API sample output

## Run

From this folder:

```powershell
C:\Users\serba\Documents\Codex\2026-06-29\n\work\tym-nlp-pocs\.venv\Scripts\python.exe .\nlp_poc_runner.py
```

To recreate the Python environment elsewhere:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
.\.venv\Scripts\python.exe -m spacy download en_core_web_sm
.\.venv\Scripts\python.exe -c "import stanza; stanza.download('en', processors='tokenize,pos,lemma,depparse,ner', model_dir='stanza_resources')"
.\.venv\Scripts\python.exe .\nlp_poc_runner.py --stanza-model-dir .\stanza_resources
```

`sutime_corenlp` and `heideltime` require a Java runtime plus their respective distributions. The runner reports them as unavailable until those binaries are configured.

Generated files:

- `results/poc_results.json`
- `results/poc_stats.csv`
- `results/poc_report.md`

## What Is Measured

- event count
- detected person entities
- detected location entities
- temporal anchors
- temporal relations
- time segments
- time tracks
- runtime per framework/sample
- availability notes

No LLM is called by this POC suite. See `llm_considerations.md` for the recommended non-LLM architecture and the limited role an optional LLM should play if one is added later.
