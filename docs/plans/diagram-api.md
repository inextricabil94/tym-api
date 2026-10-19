# Diagram API improvement plan

## Baseline and scope

This checkout uses rule-based clause/mention extraction, English ML.NET seed classifiers, Romanian rules, compatible-event TS grouping, inferred tracks/endpoints, TimeML-style annotations, and deterministic SVG/XML. Its daily quota change is already pending locally and must be preserved.

The separate research checkout already adds per-attribute provenance, explicit single-author perspective, model artifacts, and modeling tools. Port reviewed changes selectively rather than replacing this working tree wholesale. Neither implementation establishes end-to-end literary extraction accuracy.

## Prioritized backlog

| ID | Priority | Improvement | Acceptance condition |
| --- | --- | --- | --- |
| D1 | P0 | Source and inference provenance | Every actor, anchor, action, location, category, and relation has explicit/model/inherited/unknown origin and source evidence; no carry-forward is described as coreference. |
| D2 | P0 | Temporal ordering semantics | Preserve text order separately from story order; uncued adjacency is unknown, not immediately-before; directed BEFORE/AFTER and supported equivalence fixtures are consistent. |
| D3 | P0 | Source coordinates | Preserve original source and half-open UTF-16 offsets; normalization uses a reversible offset map. Every highlighted source slice equals the submitted substring, including Romanian diacritics, ligatures, and supplementary characters. |
| D4 | P0 | Graph/export consistency | Validate IDs, typed endpoints, orphan references, supported precedence cycles, and equivalence contradictions before rendering. JSON/XML/SVG reference the same nodes; unsupported interval reasoning stays documented. |
| D5 | P1 | Event and entity extraction | Separate tokenization, morphology, NER, and clause detection behind adapters. Evaluate Romanian clitics, multiword names, dialogue, nominal events, and negation on reviewed spans; report span/mention precision, recall, and F1. |
| D6 | P1 | Reference time and TimeML | Accept an explicit reference date only where appropriate; distinguish tense, narrative time, and reference time. Preserve multiple TIMEX mentions and event instances, resolve supported expressions, and retain unknown values when fiction supplies no calendar anchor. |
| D7 | P1 | Learned TS boundaries | Keep rules as the baseline; train a separate boundary model with discourse, actor, tense/aspect, and anchor features. Score boundaries separately from NAR/REM/SUP/GEN/FIC labels, using document-held-out data. |
| D8 | P1 | Directed temporal links | Generate and measure candidate pairs before classification, preserve FROM/TO direction, include no-link/unknown cases, and compare rules with a pair classifier. Report candidate recall, conditional link F1, end-to-end link F1, and consistency separately. |
| D9 | P2 | Character identity and perspective | Add reviewed entity linking and actor roles, then dialogue/focalizer attribution. Until evaluated, retain author/autor as the perspective baseline and expose identity uncertainty. |
| D10 | P2 | Diagram inspection | Select an event/TS to highlight its source, actors, anchor, relation evidence, and confidence origin. Use deterministic non-overlapping track layout, readable mobile labels, an embedded legend, and accessible SVG titles/descriptions. |
| D11 | P1 | Serving and quota correctness | Preserve the shared five-request budget across output formats. Trust forwarded IP headers only from configured ingress, verify IPv4/IPv6 normalization, bound input/concurrent work, and use an atomic shared counter before multi-replica deployment. Document window/reset semantics and restart behavior. |
| D12 | P2 | Maintainability and throughput | Extract contracts, NLP adapters, graph validation, rendering, and model loading from the large source file without behavior drift; measure cold/warm and concurrent P50/P95/P99 before changing prediction-engine ownership. |

## Staged delivery

1. **Auditable baseline (D1-D4):** source fixtures, provenance schema, unknown-order handling, and graph/export checks. Update the contract and authored sample outputs together.
2. **Measured NLP (D5-D8):** annotate a multi-work pilot, lock groups/splits, and run lexical/rule baselines before richer adapters. Integrate a model only after validation on the intended task/language.
3. **Inspectable diagrams (D9-D10):** add source-linked review and optional actor/perspective layers without presenting guesses as evidence.
4. **Operational release (D11-D12):** verify proxy trust, quotas, budgets, model identity, rollback, and clean publish contexts. Attach measured serving results to the release.

## Research protocol

Use paired baseline/candidate runs on the same frozen, adjudicated chapters or works. Report event-span and NER F1, TS-boundary F1, mode accuracy/macro F1/per-class recall, TIMEX span/type/value results, relation candidate coverage/F1, and supported graph violations. Break down results by language, dialogue, flashback, modality, implicit time, and chapter distance.

The single parallel Tash Aw work cannot establish cross-work generalization. Keep its PDF/text variants in the same source group. The newly supplied books are coverage inputs until annotated; do not infer labels or performance from a pleasing diagram.

## Example-driven review

Use the private page-9 excerpt for the move from immediate scene to earlier learning; the Verne and Dickens samples for time expressions and event sequencing; Clavell/Preda/Eliade samples for dialogue, actor identity, and shifts in perspective. These are coverage questions, not asserted gold labels. Collect errors and explicit abstentions with source offsets.

Actual renderer previews are available locally for every imported example. Review label collisions, broken references, and missing actors against the text. A picture demonstrates rendering behavior; it does not validate the proposed chronology.
