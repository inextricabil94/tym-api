# Evidence-Based Temporal Ordering: First Increment

Local v0.10 implementation; not a deployment announcement.

## Behavior

- Missing evidence yields event `relation_to_previous = UNKNOWN`, not immediate
  succession. Tense, a mentioned name, track changes, paragraph boundaries and
  source adjacency do not establish event chronology.
- Leading English/Romanian retrospective and forward cues support precedence.
  `then`, `and then`, `apoi` and `si apoi` mean succession, not immediate
  succession. Only explicit immediate-successor cues produce TimeML `IBEFORE`.
- Supported parallel cues produce internal `OVERLAPS`, not identical intervals.
  Purely spatial cues such as `elsewhere` leave temporal order unresolved.
- Valid leading ISO dates/years support order between disjoint explicit calendar
  intervals and the previous explicitly dated event in the retained context.
  A repeated year, compatible date ranges, weekdays and upload dates do not
  imply ordering. Calendar context is cleared on supported discourse transitions.
- A supported return from a lexically established flashback follows its last
  event. This is a bounded context rule, not nested discourse/coreference parsing.
- Contradictory strict-precedence cycles are withheld and reported. Overlap
  conflicting with precedence is reported rather than silently exported.
  This is not a complete 13-relation Allen reasoner.

The existing temporal-development segmentation remains in place. A TS may span
multiple sentences or begin inside a sentence at a supported temporal cue.
Incidental name changes do not split it; existing explicit interaction/separation
cues can delimit a genuine group transition. Arbitrary subordinate flashbacks,
implicit participant changes and nested narrative containers remain future work.

## API and Viewer

`diagram.temporal_ordering` contains accepted event `constraints` with source and
cue, adjacent `unresolved` pairs, withheld `conflicts`, and TS `placements`.
Unknown pairs are not exhaustively enumerated: consumers must not interpret the
absence of a pair as simultaneity or certainty. The XML has a separate
`TEMPORAL-ORDER` section, outside the TimeML-style annotation section.

`diagram.relations` projects event constraints onto TS spans; its evidence names
the source events. These projections do not assert that every point in the whole
source segment interval has that relation. Projection cycles are reported and
excluded from layout without deleting valid event-level evidence.

`story_order` is a stable topological layout position. Text order breaks ties;
it is not a timestamp, a fully inferred chronology or evidence of simultaneity.
The SVG marks affected tracks with `? Order unresolved` and explains the marker
in its English/Romanian legend. This marker can also reflect unresolved event
order within a multi-event TS, even when that TS's external position is known.

Only accepted `BEFORE`/`IMMEDIATELY_BEFORE` event constraints become event-event
TimeML-style TLINKs. `UNKNOWN` and `OVERLAPS` are not invented TimeML 1.2.1
relation types. Event temporal anchors now represent explicit mentions only;
unanchored events do not acquire a previous event's TIMEX as an asserted fact.
The wider TimeML layer is still approximate, not a fully schema-validated export.

Temporal facts, event/TS IDs and layout order are computed before density views,
and remain identical across densities and viewport widths. Density still changes
display track partitions and endpoint topology; it does not yet implement the
canonical semantic TT hierarchy proposed for a later increment. Existing cache
namespaces are versioned so older inferred chronology is not reused.
Fresh API responses and reopened saved diagrams must advertise the ordering
method; the workbench rejects older responses that lack this layer.

## Example

```text
Adam waited at home; years earlier, Adam had traveled to Paris;
now, Adam opened the door. Margaret searched the room.
```

The supported cues delimit three TSs. The earlier event precedes both the
opening scene and the return. The final search has no supported direct temporal
relation to the preceding door-opening event, so that pair remains unresolved.
No event-event `IBEFORE` is invented. The opening scene and return are not
declared simultaneous merely because their mutual order is not established.

## Verification

```powershell
dotnet build tools/Tym.GenerationChecks/Tym.GenerationChecks.csproj -c Release -o build_verify/temporal-order/checks
dotnet build_verify/temporal-order/checks/Tym.GenerationChecks.dll --temporal-only
node --test tools/tests/*.test.cjs
# With the local API running on port 8796:
./tools/tests/temporal-ordering.http.ps1
```

Authored fixtures cover English/Romanian multi-sentence continuity, embedded
within-sentence flashbacks/returns, explicit participant separation, incidental
names, differing perspectives, unknown ordering, connector retention, immediate
versus ordinary succession, overlapping versus spatial developments, calendar
order/conflicts and isolation of cycles from downstream noncyclic evidence.
Ordering fixtures compare ten density presets and alternating 720/2400-pixel
widths. These are regression checks, not measured NLP accuracy.

Verified locally on 23 October 2026:

- API/checker and UI Release builds: zero warnings/errors.
- 93 existing temporal-development cases and 78 ordering cases pass, including
  Romanian diacritics. An independent transitive-closure oracle checks 100 small
  graphs; a 20,000-node precedence chain checks nonrecursive cycle handling.
- Existing trained EN/RO NER, three POS fixtures, nineteen reference fixtures,
  202 density values and SQLite cache restart/persistence checks pass.
- All 52 Node tests pass. The 36-case browser suite verifies translated markers,
  heading/edge clearance, source inspection, density switching, old-API rejection
  and source-access restrictions, with zero page errors. Mobile and desktop
  viewport screenshots were inspected.
- The real loopback HTTP test verifies both examples and all twenty progressive
  density views. The browser offline suite verifies IndexedDB reload/reopening,
  old-epoch exclusion, resolution isolation and protected-source handling.

No Git commit/push, Azure deployment, new paid service or account/email
configuration is part of this increment.
