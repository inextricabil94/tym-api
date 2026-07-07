# Non-LLM Core and Optional LLM Layer

## Implemented Non-LLM Direction

The implemented POC suite follows the article pipeline without calling an LLM:

1. split the narrative into event-like clauses
2. extract event tuples: actors, temporal anchor, location, action, source order
3. detect temporal category: past, present, future
4. detect temporal relation cues: before, after, meanwhile, then, join, split
5. group compatible contiguous events into `TS`
6. group segments by actor set, temporal category, perspective, and narrative mode into `TT`
7. leave rendering deterministic through the existing API SVG/XML output

The .NET API also remains non-LLM. It uses deterministic rules plus ML.NET seed classifiers for temporal category and `TS` type. The Python POCs compare that direction against spaCy and Stanza preprocessing, with SUTime/CoreNLP and HeidelTime represented as installable Java-backed temporal extraction candidates.

## Recommended Non-LLM Stack

For a research-grade implementation, keep the main extractor non-LLM and make it measurable:

- Stanza: strongest default research preprocessing choice for tokenization, POS, dependencies, lemmatization, and NER.
- spaCy: fastest practical baseline and easiest production integration.
- Stanford CoreNLP/SUTime: add for stronger normalized temporal expressions.
- HeidelTime: add as a second temporal-expression extractor for comparison and TIMEX3-style normalization.
- ML.NET: keep for .NET-native classifiers once annotated TYM XML can train real `TS` boundary, `TS` type, and temporal-relation models.

## Optional LLM Role

Do not use an LLM as the primary temporal-segment generator if the goal is a reproducible research pipeline. Use an LLM only as an optional layer for:

- adjudicating low-confidence event boundaries
- proposing weak labels for later human review
- explaining why a `TS` or `TT` relation was inferred
- generating synthetic training examples that are later filtered
- comparing model output with rule/ML.NET output during evaluation

## LLM To Consider

If an optional LLM layer is added, start with `gpt-5.5` for ambiguity resolution, evaluation support, and explanation generation. Use `gpt-5.4-mini` or `gpt-5.4-nano` only for lower-cost batch assistance where perfect reasoning is less important.

Recommended configuration:

- use the Responses API
- request structured output matching the existing `NarrativeEvent`, `TimeSegment`, and `TimeRelation` schemas
- start with `reasoning.effort = "medium"` for quality/cost balance
- evaluate `low` for large batch jobs
- use `high` only for hard adjudication cases where evaluation shows a measurable improvement

The LLM should never be the only source of truth for the diagram. Treat it as another annotator whose output must be compared against deterministic rules, ML.NET classifiers, and human annotations.
