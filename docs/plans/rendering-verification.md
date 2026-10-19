# Track layout and icon verification

19 October 2026. This records rendering and UI behavior, not NLP accuracy.

## Layout behavior

Track headings show only TT identifiers, without generated titles, and occupy their own band above markers. TT tooltips also show identifiers only; generated names remain in JSON/XML. Minimum 240-pixel cells determine the number of segments per row; additional segments wrap onto continuation rows that repeat the track ID. Labels wrap to three bounded lines, with the complete text retained in a tooltip. Segment IDs use wider markers as IDs grow. Endpoint badges occupy a separate heading band. All extracted segments remain in the exported SVG.

The large text-order overview samples segment IDs across the document, including its first and last. That overview does not remove events or time-yard segments. Coordinates use invariant decimal formatting. Presentation-only XML sanitization permits PDF form feeds in the source without producing invalid SVG.

## Tag icons

| Tag | Icon | Meaning |
| --- | --- | --- |
| NAR | Play | Narration |
| REM | Rewind | Memory / flashback |
| SUP | Circled question | Supposition |
| GEN | Repeat | Generalization |
| FIC | Sparkles | Fiction / imagined event |
| JOIN | Merge arrows | Tracks meet |
| SPLIT | Split arrows | Tracks separate |

Icons supplement text labels and color. Their SVG symbols and license notices are embedded, so exports and client-side track pages require no icon CDN or font. The legend also retains the explanations of lines, triangles, boxes, and boundary ticks.

## Verification scope

- Renderer fixtures: 40 tracks, 146 segments, every narrative mode, JOIN/SPLIT endpoints, long headings, unbroken wide-letter strings, Romanian diacritics, and supplementary Unicode characters.
- Canvas widths: 720, 900, 1200, and 2400 pixels.
- Layouts: both, time_yard, and text_order.
- Locales: en-US and ro-RO, giving 24 combinations.
- Real Chromium text bounds were checked for label intersections and clipping; shape/icon bounds were checked against text, excluding the intended text inside its own TS marker.
- No label intersections, out-of-bounds labels, shape/label intersections, or unresolved icon references were observed in those checks. This is evidence for the tested fixtures, not a claim about every possible input or font.
- Viewer checks at 390, 768, 1440, and 1920 pixels covered page overflow, track filtering, previous/next paging, 100% default size, manual zoom, and Fit.
- Actual complete-PDF analyzer output contained 2,056 tracks and 5,440 segments; the exported SVG retained all 5,440 segment groups. Browser navigation used that saved analyzer response, injected into the fetch client to repeat presentation checks without consuming the daily quota.

The viewer displays twelve tracks at a time and paginates the existing SVG locally. Exported SVG/JSON/XML contain the full result. Rendering fixtures, measurements, and screenshots remain under ignored `build_verify/rendering/`; private example previews remain under ignored `private-examples/`.

Build and syntax checks passed. NuGet still reported the previously observed unavailable vulnerability metadata feed; that advisory status was not verified by the rendering checks.
