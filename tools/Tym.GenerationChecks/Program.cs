using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

var libraryFolder = Path.Combine(Path.GetTempPath(), "tym-library-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(libraryFolder);
try
{
    var text = "\U0001f642 Ana a plecat.\n\u0218tefan a venit.\n";
    var boundary = text.IndexOf('\n') + 1;
    var book = new LibraryBook("fixture", "Authored fixture", "ro", "Test", text.Length, text.Length,
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
        [new BookSection("s1", "ONE", "chapter", 0, boundary, boundary),
         new BookSection("s2", "TWO", "chapter", boundary, text.Length, text.Length - boundary)]);
    var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["TYM_LIBRARY_PATH"] = libraryFolder }).Build();
    void Save(LibraryBook value) => File.WriteAllText(Path.Combine(libraryFolder, "catalog.json"),
        JsonSerializer.Serialize(new[] { value }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
    File.WriteAllText(Path.Combine(libraryFolder, "fixture.txt"), text, new UTF8Encoding(false));
    Save(book);
    var library = new BookLibrary(settings);
    Check(library.Catalog.Count() == 1, "Library catalog missing book");
    Check(library.Read("fixture", new BookAnalysisRequest())!.Text == text, "Full book truncated");
    var part = library.Read("fixture", new BookAnalysisRequest("s2", new DiagramOptions(Language: "en")))!;
    Check(part.Text == text[boundary..] && part.Options!.Language == "ro", "Chapter UTF-16 slice or book language incorrect");
    Check(library.Read("../fixture", new BookAnalysisRequest()) is null, "Book path traversal accepted");
    Check(library.Read("fixture", new BookAnalysisRequest("unknown")) is null, "Unknown chapter accepted");
    foreach (var invalid in new[] { book with { Sha256Utf8 = "invalid" }, book with { Id = "../fixture" },
        book with { Sections = [new BookSection("s1", "ONE", "chapter", 1, text.Length, text.Length - 1)] } })
    {
        Save(invalid);
        var rejected = false;
        try { _ = new BookLibrary(settings); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "Invalid library passed integrity checks");
    }
    Check(!new BookLibrary(new ConfigurationBuilder().Build()).Catalog.Any(), "Unconfigured library should be empty");
}
finally { Directory.Delete(libraryFolder, true); }
Console.WriteLine("Book library integrity, complete text, UTF-16, language, path and boundary checks passed.");
if (args.Contains("--library-only")) return;

var analyze = typeof(DiagramOptions).Assembly.GetType("TymAnalyzer", true)!.GetMethod("Analyze", BindingFlags.Public | BindingFlags.Static)!;
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };
var samples = new[]
{
    (Language: "en", Text: "Adam searched for Margaret after the storm. Karl traveled across the island. Adam waited at the doorway. Margaret found Adam at dawn. Years earlier, Karl had carried Adam through the rain. Meanwhile, Johan searched for Karl. Adam searched for Margaret after the storm. Karl traveled across the island. Adam waited at the doorway."),
    (Language: "ro", Text: "Adam a cautat-o pe Margaret dupa furtuna. Karl a calatorit pe insula. Adam a asteptat la usa. Margaret l-a gasit pe Adam. Cu ani in urma, Karl l-a dus pe Adam prin ploaie. Intre timp, Johan l-a cautat pe Karl. Adam a cautat-o pe Margaret dupa furtuna. Karl a calatorit pe insula. Adam a asteptat la usa.")
};
var reports = new List<object>();
var recognize = typeof(DiagramOptions).Assembly.GetType("TymAnalyzer", true)!.GetMethod("RecognizeEntities", BindingFlags.Public | BindingFlags.Static)!;
foreach (var fixture in new[]
{
    (Language: "en", Text: "Adam met Karl in Jakarta. Adam found Margaret near London.", Person: "Adam", Place: "Jakarta"),
    (Language: "ro", Text: "\U0001f642 \u0218tefan s-a \u00eent\u00e2lnit cu Ana la Ia\u0219i. Ana l-a g\u0103sit pe \u0218tefan.", Person: "\u0218tefan", Place: "Ia\u0219i")
})
{
    var response = (EntityRecognitionResponse)recognize.Invoke(null, [fixture.Text, fixture.Language])!;
    Check(response.Entities.Any(entity => entity.Name == fixture.Person && entity.Label == "PERSON" && entity.MentionCount - entity.ResolvedMentionCount == 2), "NER person/count incorrect");
    Check(response.Entities.Any(entity => entity.Name == fixture.Place && entity.Label == "LOCATION"), "NER place missing");
    foreach (var entity in response.Entities)
        Check(fixture.Text[entity.SpanStart..entity.SpanEnd] == entity.Name, "NER UTF-16 span incorrect");
}
Check(((EntityRecognitionResponse)recognize.Invoke(null, ["the rain falls softly.", "en"])!).Entities.Count == 0, "NER invented an entity");
var posChecks = 0;
foreach (var fixture in new[]
{
    (Language: "en", Text: "Oh! Suddenly Adam shouted. Yes! Quickly Margaret ran. But Karl stayed in London. The boy and a girl watched. She said that he had left. Was Adam waiting? This book belongs to Adam. Three people waited. There is a house.",
        Keep: new[] { "Adam", "Margaret", "Karl", "London" }, Drop: new[] { "Oh", "Suddenly", "Yes", "Quickly", "But", "The", "Was", "This", "Three", "There", "that", "boy", "girl" }),
    (Language: "ro", Text: "Vai! Ana a mers repede la Ia\u0219i. Dar \u0218tefan a r\u0103mas. Un b\u0103iat \u0219i o fat\u0103 au venit. El a spus c\u0103 ea plecase. Acolo este un parc.",
        Keep: new[] { "Ana", "\u0218tefan", "Ia\u0219i" }, Drop: new[] { "Vai", "Dar", "Un", "Acolo", "repede", "mers", "\u0219i", "o", "b\u0103iat", "fat\u0103" })
})
{
    var response = (EntityRecognitionResponse)recognize.Invoke(null, [fixture.Text, fixture.Language])!;
    Check(response.PosFilter is { Provider: "spaCy" }, "Missing model-backed POS metadata");
    Check(response.PosFilter!.Model.StartsWith(fixture.Language + "_"), "Wrong POS language/model");
    Check(response.PosFilter.AllowedPos.SequenceEqual(new[] { "PROPN", "PRON" }), "Unexpected selectable POS classes");
    Check(response.PosFilter.ExcludedTokenCounts.ContainsKey("VERB") && response.PosFilter.ExcludedTokenCounts.ContainsKey("ADV")
        && response.PosFilter.ExcludedTokenCounts.ContainsKey("DET"), "Missing excluded POS counts");
    Check(fixture.Keep.All(name => response.Entities.Any(entity => entity.Name == name)), "POS filter lost an expected name");
    Check(!response.Entities.Any(entity => fixture.Drop.Contains(entity.Name, StringComparer.OrdinalIgnoreCase)), "Nonentity leaked into picker");
    Check(response.Entities.Any(entity => entity.Label == "REFERENCE"), "POS filter lost unresolved pronoun options");
    foreach (var entity in response.Entities)
    {
        Check(entity.PartsOfSpeech!.SequenceEqual(new[] { entity.Label == "REFERENCE" ? "PRON" : "PROPN" }), "Invalid entity POS evidence");
        Check(fixture.Text[entity.SpanStart..entity.SpanEnd] == entity.Name, "POS-filtered span incorrect");
    }
    posChecks++;
}
var possessive = (EntityRecognitionResponse)recognize.Invoke(null, ["Adam waited. His coat was wet. Her bag was dry.", "en"])!;
Check(possessive.Entities.Any(entity => entity.Name == "Adam" && entity.ReferenceForms!.Contains("His")), "Possessive alias lost");
Check(!possessive.Entities.Any(entity => entity.Name.Equals("His", StringComparison.OrdinalIgnoreCase)
    || entity.Name.Equals("Her", StringComparison.OrdinalIgnoreCase)), "Possessive determiner became a standalone option");
posChecks++;
var anaphoraChecks = 0;
foreach (var fixture in new[]
{
    (Language: "en", Text: "Adam waited. He left with his coat.", Word: "He", Names: new[] { "Adam" }),
    (Language: "en", Text: "Adam waited. He left with his coat.", Word: "his", Names: new[] { "Adam" }),
    (Language: "en", Text: "Adam saw himself.", Word: "himself", Names: new[] { "Adam" }),
    (Language: "en", Text: "Adam saw him.", Word: "him", Names: Array.Empty<string>()),
    (Language: "en", Text: "Adam met Karl. He left.", Word: "He", Names: Array.Empty<string>()),
    (Language: "en", Text: "Adam and Johan waited. They left. They returned.", Word: "They", Names: new[] { "Adam", "Johan" }),
    (Language: "en", Text: "Adam waited. He called Margaret. She returned.", Word: "She", Names: new[] { "Margaret" }),
    (Language: "en", Text: "Margaret went to Jakarta. She waited there.", Word: "there", Names: new[] { "Jakarta" }),
    (Language: "en", Text: "Adam, who waited, left.", Word: "who", Names: new[] { "Adam" }),
    (Language: "en", Text: "Adam waited. I left.", Word: "I", Names: Array.Empty<string>()),
    (Language: "en", Text: "Adam said, \"He left.\"", Word: "He", Names: Array.Empty<string>()),
    (Language: "en", Text: "He saw Adam.", Word: "He", Names: Array.Empty<string>()),
    (Language: "en", Text: "Adam arrived. It rained. It snowed. It thundered. He left.", Word: "He", Names: Array.Empty<string>()),
    (Language: "ro", Text: "\u0218tefan a plecat. El a revenit.", Word: "El", Names: new[] { "\u0218tefan" }),
    (Language: "ro", Text: "Ana a plecat. Ea a revenit.", Word: "Ea", Names: new[] { "Ana" }),
    (Language: "ro", Text: "Ana a plecat. \u0218tefan a g\u0103sit-o.", Word: "o", Names: new[] { "Ana" }),
    (Language: "ro", Text: "Adam \u0219i Johan au plecat. Ei au revenit.", Word: "Ei", Names: new[] { "Adam", "Johan" }),
    (Language: "ro", Text: "Adam a plecat. El \u00ee\u0219i amintea.", Word: "\u00ee\u0219i", Names: new[] { "Adam" }),
    (Language: "ro", Text: "Ana a mers la Ia\u0219i. Ea a locuit acolo.", Word: "acolo", Names: new[] { "Ia\u0219i" })
})
{
    var response = (EntityRecognitionResponse)recognize.Invoke(null, [fixture.Text, fixture.Language])!;
    var references = response.References!.Where(reference => reference.Text == fixture.Word && reference.Resolution != "explicit_name").ToList();
    Check(references.Count > 0, $"Reference not detected: {fixture.Word}");
    foreach (var reference in references)
    {
        Check(reference.Antecedents.Order().SequenceEqual(fixture.Names.Order()), $"Incorrect antecedent: {fixture.Text} -> {fixture.Word}");
        Check(fixture.Text[reference.SpanStart..reference.SpanEnd] == reference.Text, "Reference span incorrect");
    }
    if (fixture.Names.Length == 0) Check(response.Entities.Any(entity => entity.Label == "REFERENCE"
        && entity.Name.Equals(fixture.Word, StringComparison.OrdinalIgnoreCase)), "Unresolved reference missing from picker");
    anaphoraChecks++;
}
var placeResponse = (DiagramResponse)analyze.Invoke(null,
    ["Margaret remembered her mother in Jakarta.", new DiagramOptions(HighlightEntities: ["Jakarta"])])!;
Check(XDocument.Parse(placeResponse.Render.Svg).Descendants().Any(element => element.Name.LocalName == "line"
    && (string?)element.Attribute("stroke-width") == "5"), "Selected named place did not highlight its track");
foreach (var sample in samples)
{
    DiagramResponse Run(int density) => (DiagramResponse)analyze.Invoke(null,
        [sample.Text, new DiagramOptions(Language: sample.Language, TrackDensity: density)])!;
    var baseline = Run(50);
    DiagramResponse? previous = null;
    var counts = new List<int>();
    for (var density = 0; density <= 100; density++)
    {
        var response = Run(density);
        Check(JsonSerializer.Serialize(response.Diagram.Events) == JsonSerializer.Serialize(baseline.Diagram.Events), "Density changed events");
        Check(JsonSerializer.Serialize(response.Diagram.TimeMl) == JsonSerializer.Serialize(baseline.Diagram.TimeMl), "Density changed TimeML");
        Check(response.Diagram.Segments.Count == baseline.Diagram.Segments.Count, "Lost segments");
        var ids = response.Diagram.Tracks.Select(track => track.Id).ToHashSet();
        foreach (var (segment, original) in response.Diagram.Segments.Zip(baseline.Diagram.Segments))
        {
            Check(segment with { TrackId = original.TrackId, StoryOrder = original.StoryOrder } == original
                || JsonSerializer.Serialize(segment with { TrackId = original.TrackId, StoryOrder = original.StoryOrder }) == JsonSerializer.Serialize(original), "Segment content changed");
            Check(ids.Contains(segment.TrackId), "Invalid segment track reference");
        }
        foreach (var track in response.Diagram.Tracks)
        {
            Check(!string.IsNullOrWhiteSpace(track.Name) && track.Name.Length <= 99, "Invalid summary title");
            Check(response.Diagram.Segments.Any(segment => segment.TrackId == track.Id), "Empty track");
        }
        foreach (var endpoint in response.Diagram.Endpoints) Check(endpoint.TrackIds.All(ids.Contains), "Invalid endpoint reference");
        foreach (var boundary in response.Diagram.Boundaries) Check(boundary.TrackIds.All(ids.Contains), "Invalid boundary reference");
        if (previous is not null)
        {
            Check(response.Diagram.Tracks.Count >= previous.Diagram.Tracks.Count, "Non-monotonic density");
            var prior = previous.Diagram.Segments.ToDictionary(segment => segment.Id, segment => segment.TrackId);
            foreach (var group in response.Diagram.Segments.GroupBy(segment => segment.TrackId))
                Check(group.Select(segment => prior[segment.Id]).Distinct().Count() == 1, "Density partitions are not nested");
        }
        Check(response.TrackGeneration.Density == density, "Missing effective density");
        var svg = XDocument.Parse(response.Render.Svg);
        XNamespace svgNamespace = "http://www.w3.org/2000/svg";
        var renderedTracks = svg.Descendants(svgNamespace + "g")
            .Where(group => (string?)group.Attribute("class") == "time-track").ToList();
        Check(renderedTracks.Count == response.Diagram.Tracks.Count, "Missing rendered tracks");
        foreach (var group in renderedTracks)
        {
            var id = (string?)group.Attribute("data-track-id");
            var heading = group.Elements(svgNamespace + "g")
                .Single(element => (string?)element.Attribute("class") == "track-heading");
            Check(heading.Value == id, "Diagram heading must show only the TT identifier");
            Check(group.Element(svgNamespace + "title")?.Value == id, "TT title leaked into SVG tooltip");
        }
        XDocument.Parse(response.Xml);
        counts.Add(response.Diagram.Tracks.Count);
        previous = response;
    }
    Check(counts[100] == baseline.Diagram.Segments.Count, "Density 100 must retain one track per TS");
    Check(counts[0] < counts[100], "Fixture did not exercise density");
    Check(baseline.Diagram.Tracks.Any(track => track.Name.Contains(sample.Language == "en" ? "searched" : "cautat")), "Titles did not summarize actions");
    var cached = (DiagramResponse)analyze.Invoke(null, [sample.Text, new DiagramOptions(Language: sample.Language, CacheTrackDensities: true)])!;
    Check(JsonSerializer.Serialize(cached with { DensityCache = null }) == JsonSerializer.Serialize(baseline), "Caching changed the primary diagram");
    Check(cached.DensityCache!.Options.Select(option => option.Density).SequenceEqual(new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }), "Missing cache presets");
    foreach (var option in cached.DensityCache.Options)
    {
        var expected = Run(option.Density);
        var view = option.ViewId == "base" ? null : cached.DensityCache.Views[option.ViewId];
        var placements = view?.SegmentPlacements.ToDictionary(segment => segment.Id);
        var reconstructed = view is null ? cached.Diagram : cached.Diagram with
        {
            Tracks = view.Tracks,
            Segments = cached.Diagram.Segments.Select(segment => segment with
            {
                TrackId = placements![segment.Id].TrackId,
                StoryOrder = placements[segment.Id].StoryOrder
            }).ToList(),
            Endpoints = view.Endpoints,
            Relations = view.Relations,
            Boundaries = view.Boundaries
        };
        Check(option.TrackCount == expected.Diagram.Tracks.Count, "Cache track count incorrect");
        Check(JsonSerializer.Serialize(reconstructed) == JsonSerializer.Serialize(expected.Diagram), "Cached topology differs from independent generation");
        Check((view?.Render ?? cached.Render) == expected.Render, "Cached SVG differs from independent generation");
        Check((view?.Xml ?? cached.Xml) == expected.Xml, "Cached XML differs from independent generation");
        Check(JsonSerializer.Serialize(view?.Analysis ?? cached.Analysis) == JsonSerializer.Serialize(expected.Analysis), "Cached statistics incorrect");
    }
    reports.Add(new { language = sample.Language, density_cases = counts.Count, coarse_tracks = counts[0], default_tracks = counts[50], detailed_tracks = counts[100], events = baseline.Diagram.Events.Count, segments = baseline.Diagram.Segments.Count });
}
var single = (DiagramResponse)analyze.Invoke(null, ["Adam waited.", new DiagramOptions(CacheTrackDensities: true)])!;
Check(single.DensityCache!.Options.Count == 11 && single.DensityCache.Views.Count == 0
    && single.DensityCache.Options.All(option => option.ViewId == "base"), "Identical density views must reuse the base payload");
if (args.Contains("--refresh-sample"))
{
    var request = JsonSerializer.Deserialize<DiagramRequest>(File.ReadAllText("sample_input.json"), json)!;
    var response = (DiagramResponse)analyze.Invoke(null, [request.Text, request.Options ?? new DiagramOptions()])!;
    File.WriteAllText("sample_response.json", JsonSerializer.Serialize(response, json));
    File.WriteAllText("sample_output.svg", response.Render.Svg);
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, pos_checks = posChecks, anaphora_checks = anaphoraChecks, reports }, json));

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
