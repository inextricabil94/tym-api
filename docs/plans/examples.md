# Private literary examples

## Inputs and provenance

The user supplied `Tash Aw - Harta lumii nevazute TIPAR+razlete.pdf` and fifteen `.txt` books. The private importer reads documents as data and records SHA-256, byte size, chosen encoding, source-work group, language, excerpt coordinates, and preparation steps. Original files are unchanged. Complete UTF-8 copies/extractions, request fixtures, and excerpts are prepared inside the ignored `private-examples/` directory; they are not public repository content.

The PDF's **Full text** contains extraction from every page, in order, including front matter and page labels. Page boundaries are retained as form feeds. PDF extraction artifacts remain present; this is not an OCR-corrected transcription. The optional **Excerpt** is the final paragraph of printed page 9/PDF page 9, beginning with Adam's attempt to call Karl and ending with earlier learning. That paragraph was visually checked and has recorded whitespace cleanup. The text edition is a separate source; its offsets are never borrowed from the PDF.

| Text source | Language | Source-work group |
| --- | --- | --- |
| Shogun-1-James-Clavell.txt; Shogun-2-James-Clavell.txt | ro | shogun |
| Tinutul-blanurilor-vol.-1-Jules-Verne.txt; Tinutul-blanurilor-vol-2-Jules-Verne.txt | ro | tinutul-blanurilor |
| Morometii - vol.1 - Marin Preda.txt; Morometii-vol.2-Marin-Preda.txt | ro | morometii |
| Harta lumii nevazute.txt; Map of the Invisible World - Tash Aw.txt; supplied PDF | ro/en | map-of-the-invisible-world |
| Ultima-sansa-Arthur-Hailey.txt | ro | ultima-sansa |
| Un-colind-de-Craciun-Charles-Dickens.txt | ro | un-colind-de-craciun |
| Uniforme-de-general-Mircea-Eliade.txt | ro | uniforme-de-general |
| Zeii-insisi-Isaac-Asimov.txt | ro | zeii-insisi |
| 20000 de leghe sub mari - Jules Verne.txt | ro | 20000-de-leghe-sub-mari |
| Arta tacerii - B. Blanchard.txt | ro | arta-tacerii |
| scaune_de_plus_ion.txt | ro | scaune-de-plus |

Work IDs are grouping decisions, not verified bibliographic metadata. Volumes/translations/editions cannot cross a model-evaluation split. `scaune_de_plus_ion.txt` retains its supplied identity without guessing authorship.

## Prepare examples

Use Python with `pypdf` for the PDF input:

```powershell
python .\tools\prepare-private-examples.py `
  --pdf 'C:\Users\serba\Downloads\Tash Aw - Harta lumii nevazute TIPAR+razlete.pdf' `
  --texts-root 'C:\Users\serba\OneDrive\Desktop\PhD Final\texts'
```

The output defaults to ignored `private-examples/`. It contains the complete decoded sources in `full-text/`, `examples.json`, `manifest.json`, full-source diagram requests in `requests/full-diagram/`, and short-example inputs in `requests/diagram/` and `requests/corpus/`. Every text file's complete source content, including line endings, is preserved after explicit decoding. Full-text UTF-16 lengths and UTF-8 hashes are recorded separately from excerpt coordinates. PDF cleanup applies only to the short excerpt. Rerunning preparation refreshes the generated files and removes preview references; regenerate the pictures afterward.

## Run the local example UI

Start the diagram API:

```powershell
dotnet run --project .\Tym.Api.csproj --urls http://127.0.0.1:8765
```

Start the UI in another terminal:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:TYM_API_BASE_URL = 'http://127.0.0.1:8765'
dotnet run --project .\ui\Tym.Ui\Tym.Ui.csproj --urls http://127.0.0.1:8780
```

Choose a book from the selector: the complete source loads on demand. Books longer than 20,000 UTF-16 code units with marked headings default to **Chapters**, initially selecting the first actual chapter. Otherwise **Full text** is the default. **Full text** always restores the complete input. **Excerpt** switches to the short example and its existing picture. Loading is cancellable by selecting another source/mode; stale responses cannot replace the current selection. Original English/Romanian demo tabs remain available.

**Chapters** recognizes case-insensitive, line-start `Capitolul {number}`, `Capitol {number}`, and `Chapter {number}` headings, including Arabic/Roman numbers, digit ordinals, and Romanian/English spelled-out cardinals and ordinals. Examples include `Capitolul al doilea`, `Capitolul UNU`, and `Chapter Twenty-First`. Auto mode also detects standalone consecutive sequences (including `UNU/DOI/TREI/PATRU` and `ONE/TWO/THREE/FOUR`) when at least three headings have 500 or more UTF-16 units between them. This enables structural splitting in the Tash Aw text editions and Morometii sources. Automatic bare numbering is disabled for form-feed-separated PDF text to avoid page folios. Choose **Prefixed headings only** to suppress standalone detection, or **Include short numbered sections** to deliberately permit shorter two-or-more-heading sequences in excerpts. The latter can also treat page numbers/lists as chapters; inspect the ranges. These are heuristics, not bibliographically validated annotations.

The chapter menu retains original numbers, including Shogun volume 2's starting number 37 and numbering restarts across parts. Nonempty text before the first heading appears as **Front matter**. Leading whitespace attaches to the first section when there is no front matter. All sections are contiguous exact UTF-16 slices: concatenating them reproduces the complete source, including line endings and the book's end. Inline references to a chapter do not split prose. Chapter mode uses the currently loaded full text or excerpt, rather than substituting a full book when the excerpt has its own headings.

The selector and previous/next buttons change the active chapter. Its selected/total character count and full-source UTF-16 range are shown explicitly. Generate submits that entire chapter, not the rest of the book and not a truncated opening fragment. Event and TimeML offsets in the returned document are chapter-local; add the displayed start offset to map them to the loaded source. Chapter analysis is independent and does not infer cross-chapter links/coreference. Full-book generation remains available. One generation, whether chapter, excerpt, or full book, uses one normal quota request; switching sections does not generate or spend quota automatically.

Source attribution appears below the text. Editing clears its source selection and stored preview. In chapter mode, an edit replaces only the current chapter while retaining other sections; offsets are recalculated for the edited text. Pasted texts also expose Full text/Chapters controls when marked headings are found. Generate submits the complete current selection without silently taking its beginning or trimming source boundaries. A full-book diagram can be much larger than a chapter/excerpt diagram; use JSON/XML downloads to inspect all extracted items. These remain baseline predictions, not corrected annotations.

The UI loads private examples only when both conditions hold: Development environment and a loopback connecting address. This also protects `GET /local-examples/{id}/text`. Production returns 404 for full-text requests even when files are present. Responses use `Cache-Control: no-store`. `TYM_PRIVATE_EXAMPLES_FILE` overrides the catalog path; complete texts are resolved only beneath its adjacent `full-text/` directory using restricted IDs. Missing files preserve the standard demos. Invalid local JSON is ignored with a warning that contains no prose.

To exercise the independent Corpus Explorer, POST the short-example `requests/corpus/<id>.json` to its local `/v1/documents/analyze`. That application's existing 50,000-code-unit document limit still applies: the full-book sources are available for preparation, but this change does not add book-sized job processing to the separate application. Its fixtures remain explicitly excerpts. No document date or supervised labels are invented.

## Pictures

Generated pictures live under `private-examples/previews/`, with an `index.html` contact sheet. Each SVG comes from the current diagram analyzer/renderer on that example's text; aggregate counts and model version identify its origin. Local previews can also appear under the selected UI example. These pictures demonstrate current extraction and rendering, including possible errors, not reference annotations.

Regenerate the pictures after preparing the inputs:

```powershell
dotnet run --project .\tools\Tym.ExamplePreviews -c Release -- .\private-examples
```

This local developer command calls the existing analyzer directly, creates SVGs and a gallery, and refreshes each example's stored preview and model version. It does not send the private text to the deployed API. HTTP generation from the UI still uses the ordinary quota.

Keep private outputs out of commits, Docker contexts, published assets, public logs, and screenshots intended for sharing. `.gitignore`, `.dockerignore`, and project publish exclusions enforce the local repository boundary. The original source files remain in their supplied locations.
