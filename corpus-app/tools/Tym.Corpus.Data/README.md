# Private corpus preparation in C#

All model fitting and scoring use ML.NET. This .NET 10 tool prepares input rows and reads ZIP/XML as data; it does not execute attached scripts or resolve external XML resources.

## Commands

```powershell
dotnet run --project tools/Tym.Corpus.Data -- tym-xml --romanian C:\private\hln_ro.xml --english C:\private\motiw_en.xml --out C:\private\tym.jsonl --report C:\private\tym-import.json
dotnet run --project tools/Tym.Corpus.Data -- timebank --zip C:\private\Ro-TimeBank.zip --exclusions tools/Tym.Modeling/ro-timebank-exclusions.json --out C:\private\timebank.jsonl --report C:\private\timebank-import.json
dotnet run --project tools/Tym.Corpus.Data -- books --zip C:\private\Books.zip --romanian C:\private\Harta.txt --english C:\private\Map.txt --out C:\private\books-ro.jsonl --en-out C:\private\books-en.jsonl --report C:\private\books-import.json
dotnet run --project tools/Tym.Corpus.Data -- book-sample --zip C:\private\Books.zip --out C:\private\ui-book-samples.json --count 5 --max-chars 1200
```

TYM labels and ISO-TimeML labels stay in separate tasks. Both annotation importers retain `provided_annotation/adjudication_unknown`. Raw books retain `unlabeled_user_provided` and chapter/document groups. Text preparation normalizes Unicode/whitespace for lexical model input; these passages do not preserve source offsets. The annotation workbench uses a separate immutable text layer and half-open UTF-16 spans.

`BookImporter` decodes strict UTF-8 with a Windows-1250 fallback, groups chapter files by their containing book, splits long units, and removes exact duplicate passages. `TimeBankImporter` resolves a short offline character-entity table from the provided archive. It rejects inline DTD subsets, unresolved entities, unsafe archive names, oversized archives, suspicious compression ratios and ambiguous participant identifiers. Duplicate signal IDs cause affected links to be skipped and counted.

`book-sample` writes original archive excerpts only to the user-selected private output. Tests must never embed those excerpts in committed fixtures, public frontend assets, screenshots intended for publication, or the research report.
