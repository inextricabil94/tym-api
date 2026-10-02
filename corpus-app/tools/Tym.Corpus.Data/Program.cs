using Tym.Corpus.Data;

try
{
    if (args.Length == 0 || args[0] is "--help" or "help")
    {
        Console.WriteLine("TYM private corpus preparation (.NET). Commands: books, book-sample, tym-xml, timebank.\nUse --zip/--romanian/--english inputs and --out/--report private outputs; see README.md.");
        return 0;
    }
    if ((args.Length - 1) % 2 != 0) throw new ArgumentException("Options require a --name value pair.");
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 1; index < args.Length; index += 2)
    {
        if (!args[index].StartsWith("--") || !options.TryAdd(args[index][2..], args[index + 1])) throw new ArgumentException("Malformed or repeated option.");
    }
    string Required(string name) => options.GetValueOrDefault(name) ?? throw new ArgumentException("Missing --" + name);
    string? Optional(string name) => options.GetValueOrDefault(name);
    switch (args[0])
    {
        case "books":
        {
            var sources = BookImporter.ReadArchive(Required("zip")).ToList();
            if (Optional("romanian") is { } roPath) sources.Add(BookImporter.ReadFile(roPath));
            var ro = BookImporter.Prepare(sources, "ro");
            CorpusFiles.WriteJsonLines(Required("out"), ro.Rows);
            var summaries = new List<object> { ro.Report };
            if (Optional("english") is { } enPath)
            {
                var en = BookImporter.Prepare([BookImporter.ReadFile(enPath)], "en");
                CorpusFiles.WriteJsonLines(Required("en-out"), en.Rows);
                summaries.Add(en.Report);
            }
            var report = new { archive_sha256 = CorpusFiles.FileHash(Required("zip")), importer = "C#/.NET", corpora = summaries };
            if (Optional("report") is { } reportPath) CorpusFiles.WriteJson(reportPath, report);
            Console.WriteLine($"Prepared {ro.Rows.Count} Romanian unlabeled passages; raw source text stays in private output files.");
            break;
        }
        case "book-sample":
        {
            var count = int.Parse(Optional("count") ?? "5");
            var maximum = int.Parse(Optional("max-chars") ?? "1200");
            if (count is < 1 or > 25 || maximum is < 100 or > 5000) throw new ArgumentException("Invalid sample limits.");
            var samples = BookImporter.ReadArchive(Required("zip")).GroupBy(source => source.DocumentId).Take(count)
                .Select(group => group.First()).Select(source => new
                {
                    source.Source, source.DocumentId, source.ChapterId, language = "ro",
                    text = BookImporter.Prefix(BookImporter.Passages(source.Text).First(), maximum),
                    provenance = "private Books.zip excerpt; unlabeled; UI behavior test only"
                }).ToArray();
            CorpusFiles.WriteJson(Required("out"), samples);
            Console.WriteLine($"Wrote {samples.Length} private UI excerpts. No source text printed.");
            break;
        }
        case "tym-xml":
        {
            var ro = TymXmlImporter.Import(Required("romanian"), "ro");
            var en = TymXmlImporter.Import(Required("english"), "en");
            var rows = ro.Rows.Concat(en.Rows).ToArray();
            CorpusFiles.WriteJsonLines(Required("out"), rows);
            if (Optional("report") is { } path) CorpusFiles.WriteJson(path, new { importer = "C#/.NET", output_rows = rows.Length, files = new[] { ro.Report, en.Report } });
            Console.WriteLine($"Imported {rows.Length} provided-annotation task rows from the parallel XML pair.");
            break;
        }
        case "timebank":
        {
            var result = TimeBankImporter.ImportArchive(Required("zip"), Optional("exclusions"));
            CorpusFiles.WriteJsonLines(Required("out"), result.Rows);
            if (Optional("report") is { } path) CorpusFiles.WriteJson(path, result.Report);
            Console.WriteLine($"Imported {result.Rows.Count} separate ISO-TimeML task rows.");
            break;
        }
        default: throw new ArgumentException("Unknown corpus command.");
    }
    return 0;
}
catch (Exception error) when (error is ArgumentException or IOException or System.Xml.XmlException
    or System.Text.Json.JsonException or FormatException or NotSupportedException)
{
    Console.Error.WriteLine("error: " + error.Message);
    return 2;
}
