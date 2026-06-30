# TYM .NET API Prototype

TYM is a small ASP.NET Core Minimal API for turning narrative text into time-yard diagrams. It is based on the time-yard model from Cristea and Macovei: a text is represented as time tracks (`TT`), time segments (`TS`), endpoints, and temporal relations. The generated SVG mirrors the example image by showing:

- a text-order segment chain, where `TS` items appear in reading order
- a time-yard view, where segments are arranged by narrative tracks
- start/stop markers, joins/splits, rupture/commute hints, and segment labels

This prototype now follows the implementation directions from the supplied papers more closely. It first extracts event-like clauses, attaches actors, temporal anchors, locations, actions, and a `Past`/`Present`/`Future` temporal category, then groups contiguous compatible events into time segments. ML.NET is used for event temporal-category classification and for each candidate time segment's narrative type (`NAR`, `REM`, `SUP`, `GEN`, or `FIC`). The bundled models are trained at startup from seed examples in `Program.cs`; a production implementation should replace those examples with the annotated TYM corpus.

## Run

```powershell
cd tym-api
dotnet run --urls http://127.0.0.1:8765
```

Then call:

```powershell
Invoke-RestMethod `
  -Uri http://127.0.0.1:8765/v1/diagrams `
  -Method Post `
  -ContentType 'application/json' `
  -Body (Get-Content .\sample_input.json -Raw)
```

To get only SVG:

```powershell
Invoke-WebRequest `
  -Uri http://127.0.0.1:8765/v1/diagrams/svg `
  -Method Post `
  -ContentType 'application/json' `
  -Body (Get-Content .\sample_input.json -Raw) `
  -OutFile .\sample_output.svg
```

## Endpoints

- `GET /health`
- `POST /v1/diagrams` returns normalized diagram JSON plus embedded SVG
- `POST /v1/diagrams/svg` returns `image/svg+xml`
- `POST /v1/diagrams/xml` returns paper-style XML notation

See [openapi.yaml](openapi.yaml) for the full contract.

The API also serves the contract at:

```text
http://127.0.0.1:8765/openapi.yaml
```

## Core Data Model

`NarrativeEvent` is the event-level unit suggested by the second paper:

- `id`: stable event id, such as `EV1`
- `actors`: person entities participating in the event, with carry-forward when absent
- `temporal_anchor`: detected temporal expression or inherited anchor
- `location`: lightweight location phrase
- `action`: detected verb/action cue
- `temporal_category`: `Past`, `Present`, or `Future`
- `span_start`, `span_end`: source character offsets

`TimeSegment` corresponds to `TS` in the paper:

- `id`: stable segment id, such as `TS1`
- `text`: source span
- `track_id`: owning time track
- `type`: `NAR`, `REM`, `SUP`, `GEN`, or `FIC`
- `perspective`: narrator, character, or unknown
- `text_order`: source order index
- `story_order`: layout order inside the track
- `actors`: stable character set for the segment
- `location_id`: associated `TL` location id
- `temporal_anchor`: associated temporal expression
- `temporal_category`: `Past`, `Present`, or `Future`
- `event_ids`: events grouped into this segment
- `confidence`: ML.NET classifier confidence for `type`
- `classifier`: `mlnet_seed_model` or `heuristic_fallback`

`TimeTrack` corresponds to `TT`:

- `id`: stable track id, such as `TT1`
- `name`: mnemonic track name, usually inferred from character entities
- `left_endpoint`: `START` or split endpoint id
- `right_endpoint`: `STOP` or join endpoint id

`Endpoint` captures joins and splits:

- `type`: `JOIN` or `SPLIT`
- `track_ids`: the involved tracks
- `segment_id`: the segment where the event is inferred

`TimeRelation` captures shallow temporal constraints:

- `from_id`, `to_id`
- `rel`: `BEFORE`, `IMMEDIATELY_BEFORE`, `AFTER`, `IMMEDIATELY_AFTER`, or `SIMULTANEOUS`

## Production Notes

The API boundary is intentionally separate from extraction. The current pipeline is:

1. split text into event-like clauses using punctuation, conjunction, and verb cues
2. extract actor, temporal anchor, location, action, and offset features
3. classify each event as `Past`, `Present`, or `Future` with ML.NET
4. concatenate adjacent events with compatible temporal category and actor unity into `TS`
5. classify each `TS` as `NAR`, `REM`, `SUP`, `GEN`, or `FIC`
6. infer TT membership, boundaries, endpoints, and relations
7. render JSON, SVG, and paper-style XML

The renderer consumes normalized TYM JSON, so extraction can be upgraded independently.
