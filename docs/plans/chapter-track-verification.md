# Chapter and track generation checks

## Automated regression checks

```powershell
node --test .\tools\tests\chapter-splitter.test.cjs
dotnet run --project .\tools\Tym.GenerationChecks -c Release
```

The JavaScript tests cover Arabic/Roman headings, optional titles, original/repeated numbering, front matter, line anchoring, invalid numerals, CR/LF/CRLF, BOM/form feeds, Romanian/astral Unicode, unmarked text, final newlines, and lossless reconstruction of a 100-chapter input over one million characters.

The extended splitter also tests Romanian/English spelled-out cardinals and ordinals, including standalone `UNU/DOI/TREI/PATRU` and `ONE/TWO/THREE/FOUR`. Auto mode requires three consecutive standalone headings spaced by at least 500 UTF-16 units and excludes form-feed-separated PDF text. An explicit short-numbered-sections mode permits shorter two-heading sequences; prefixed-only mode disables bare-number inference. Original source content remains lossless under each mode. On the supplied private sources, automatic structural detection now finds candidate chapters in eleven text editions (441 candidate chapters total); these counts are heuristic observations, not adjudicated chapter boundaries.

The .NET executable exercises all 101 density values on authored English and Romanian fixtures. It verifies monotonic track counts, nested partitions, unchanged events/segments/TimeML, valid track references, nonempty short action-summary titles, and parseable SVG/XML. It calls the analyzer directly, without spending HTTP quota. `--refresh-sample` regenerates the public authored JSON/SVG example.

Nineteen additional authored anaphora cases cover English/Romanian pronouns, possessives, reflexives, clitics, relative and place references, coordinated plural groups, gender evidence from earlier unambiguous resolutions, quoted/ambiguous/unsupported references, distance cutoffs, and exact UTF-16 reference spans. No annotated-corpus coreference accuracy is asserted. Browser tests verify that canonical entity and unresolved-pronoun selections highlight only the appropriate tracks, rather than relying on inherited actors. They also verify full-book/excerpt isolation, four Romanian/English word-numbered sections, ordinal headings, exact selected-excerpt chapter submission, original source restoration, and no page overflow at 320/390/768/1440/1920 pixels. These checks use an isolated loopback API without consuming the working API's quota.

Observed fixture counts (software behavior, not NLP accuracy):

| Language | Density 0 TTs | Density 50 TTs | Density 100 TTs | Events | Segments |
| --- | ---: | ---: | ---: | ---: | ---: |
| English | 3 | 6 | 9 | 9 | 9 |
| Romanian | 2 | 6 | 9 | 9 | 9 |

The default density preserves the previous natural grouping. Coarse density retains distinct narrative modes and temporal categories, but intentionally abstracts actor/perspective differences. Fine density subdivides natural tracks. Titles are term-frequency-ranked extractive summaries; these checks do not establish summary quality, temporal correctness, or annotated-corpus performance.

## Local browser checks

The local verification uses an isolated loopback API so it does not consume the working API's five-request quota. It checks each detected supplied book's first/last/front-matter sections, complete source/excerpt restoration, exact selected-chapter submission, real generations at densities 0/50/100, invalid density rejection, actor search, stale-diagram clearing, chapter editing, and pasted text detection. Screenshots and private source inventory are retained only in ignored `build_verify/`.

Detected marked chapters: Shogun volume 1, 36; Shogun volume 2, 25; Tinutul blanurilor volume 1, 23; volume 2, 24; 20000 de leghe sub mari, 47. All five sources also retain a front-matter section. The remaining supplied sources have no matching marked headings and retain full-text/excerpt mode.

Real HTTP generations of Tinutul blanurilor volume 1's first chapter retained 134 events and 107 temporal segments at every tested density, with 4/90/107 tracks at density 0/50/100 respectively. Both out-of-range values (-1 and 101) returned HTTP 400. Browser checks at widths 390, 768, 1440, and 1920 found no page overflow or SVG text-label overlap. No browser script errors were observed.
