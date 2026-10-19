# POS-Filtered Entity Highlighting

## Scope

`POST /v1/entities` tags the exact current English/Romanian text with local spaCy models before filtering rule-based named-entity candidates and resolving references. No LLM, remote NLP service, spaCy NER or dependency parser is used. This improves the highlight menu and its source-span highlighting only; it does not replace event, actor, temporal or TimeML extraction.

Selectable names require contextual `PROPN` tokens. Supported unresolved pronouns require nonpossessive `PRON`; morphology also excludes possessives tagged `PRON` rather than `DET`. Non-name tokens at candidate boundaries are removed without discarding an adjacent valid name. `INTJ`, `VERB`, `AUX`, `ADV`, `ADJ`, `CCONJ`, `SCONJ`, `DET`, `ADP`, `NOUN`, `NUM`, `PUNCT`, `SYM` and `X` cannot become standalone entries under those tags. English existential `there` (`EX`) is excluded too. Resolved possessives and place adverbs can remain reference evidence under a canonical entity, but are not standalone menu options.

## Local Setup

Use Python 3.11 or 3.12 with PowerShell:

```powershell
.\tools\setup-pos.ps1 -Python python
dotnet run --urls http://127.0.0.1:8765
```

The script creates `private-models/.venv`, installs `tools/requirements-pos.txt` and writes `private-models/pos-settings.json`. It uses spaCy 3.8.14 and official English/Romanian small models 3.8.0. A compatible existing environment can instead be configured using these environment variables, which override the settings file:

- `TYM_POS_PYTHON`: Python executable with spaCy installed.
- `TYM_POS_EN_MODEL`: package name or model directory (default `en_core_web_sm`).
- `TYM_POS_RO_MODEL`: package name or model directory (default `ro_core_news_sm`).
- `TYM_POS_SETTINGS_FILE`: optional alternate JSON settings-file path. Default is `private-models/pos-settings.json` under the API working directory.

Restart the API after changing configuration. Local Python environments, settings and model weights are deliberately excluded from Git, .NET publishing and Docker build contexts. The API Dockerfile separately installs Python, spaCy and both licensed models inside its image, preserving their installed license/metadata files; it does not upload the local environment or texts. A plain `dotnet publish` still requires Python/models provisioned separately on its host. Diagrams without entity highlighting remain usable without Python. Entity lookup or diagrams requesting entity highlighting return `503` if tagging is unavailable; the UI clears its choices and displays the lookup error rather than accepting an unfiltered response.

## Execution and Evidence

One hidden Python worker is reused per API process, with lazy per-language model loading and serialized requests. Long texts are processed in bounded 16,000-code-point chunks at whitespace boundaries where available. A 60-second response timeout stops a failed worker; an explicit retry can start it again. Cache keys hash the exact text and language; at most eight annotation results covering at most 2,000,000 input UTF-16 units stay in memory. Worker input is not written to disk or sent to a network service. Browser/HTTP caches do not store entity responses. Offsets are converted to UTF-16, including astral characters, so .NET and browser slicing agree.

Responses expose `parts_of_speech` per entity, `part_of_speech` per reference, and `pos_filter` with provider, model/version, allowed standalone classes and excluded input-token counts by POS. Those counts are not rejected-entity counts or accuracy measurements. Method: `spacy-pos+rule-ner-anaphora-en-ro-v3`.

## Limitations and Verification

Statistical POS tags are fallible. Capitalized common nouns can be tagged as names; homographs such as personal name "May" can be tagged as auxiliaries and omitted. Model fine tags lacking a universal POS mapping are treated as `X` (unknown) and excluded, not guessed or allowed to fail the entire book lookup. News-trained models are not validated on these literary/OCR sources. Chunk boundaries reduce available context. The existing candidate recognizer and conservative anaphora rules also remain limitations. There is no annotated evaluation corpus or measured precision/recall claim.

`tools/Tym.GenerationChecks` checks authored English/Romanian fixtures for retained people/places, excluded function/common words, POS metadata, UTF-16 offsets and linked possessives, alongside existing anaphora/density tests. Browser checks exercise the actual API/menu and unavailable-runtime error. These are regression checks, not research accuracy statistics.

Local checks on 19 October 2026 passed: four worker tests (unknown tags, possessives, bounded chunks and astral UTF-16 offsets), three POS fixtures, 19 anaphora cases, 202 density cases, and 19 chapter/grouping tests. Headless browser checks covered actual bilingual lookup, missing-runtime `503` with cleared choices and retry recovery, highlight spans, both themes, exports, separate rate limits and widths 320-1920 pixels. A complete Romanian local book (1,346,622 UTF-16 units) completed entity lookup in 28.1 seconds on this machine; all 20,831 returned reference spans and 1,139 selectable entries had matching source slices. These counts are output/processing checks, not annotated precision/recall. Build succeeded with a NuGet vulnerability-feed availability warning (`NU1900`).

References: [spaCy POS tagging](https://spacy.io/usage/linguistic-features#pos-tagging), [English models](https://spacy.io/models/en), [Romanian models](https://spacy.io/models/ro). See `THIRD_PARTY_NOTICES.md` for model licenses.
