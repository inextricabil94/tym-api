using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;

internal static class TemporalOrderingChecks
{
    public static void Run(Func<string, DiagramOptions, DiagramResponse> analyze)
    {
        var fixtures = new[]
        {
            (Name: "multi-sentence-continuity", Language: "en", Text: "Adam opened the door. Margaret entered the room. Karl waited beside them.", Segments: 1),
            (Name: "multi-sentence-continuity", Language: "ro", Text: "Ana a deschis usa. Stefan a intrat in camera. Ion a asteptat langa ei.", Segments: 1),
            (Name: "embedded-flashback", Language: "en", Text: "Adam waited at home; years earlier, Adam had traveled to Paris; now, Adam opened the door.", Segments: 3),
            (Name: "embedded-flashback", Language: "ro", Text: "Ana a asteptat acasa; cu ani in urma, Ana a calatorit la Iasi; acum, Ana a deschis usa.", Segments: 3),
            (Name: "participant-separation", Language: "en", Text: "Adam waited with Margaret. They parted ways. Adam traveled to Paris.", Segments: 2),
            (Name: "participant-separation", Language: "ro", Text: "Ana a asteptat cu Stefan. Ei s-au despartit. Ana a calatorit la Iasi.", Segments: 2),
            (Name: "unresolved-perspectives", Language: "en", Text: "According to Adam, Karl waited. According to Margaret, Johan searched the room.", Segments: 2),
            (Name: "unresolved-perspectives", Language: "ro", Text: "Din perspectiva lui Ana, Stefan a asteptat. Din perspectiva lui Ion, Stefan a cautat cheia.", Segments: 2),
            (Name: "sequence-not-immediate", Language: "en", Text: "Adam waited and then Margaret entered.", Segments: 1),
            (Name: "sequence-not-immediate", Language: "ro", Text: "Ana a asteptat si apoi Stefan a intrat.", Segments: 1),
            (Name: "sequence-not-immediate", Language: "ro", Text: "Ana a a\u0219teptat \u0219i apoi \u0218tefan a intrat.", Segments: 1),
            (Name: "embedded-flashback", Language: "ro", Text: "Ana a a\u0219teptat acas\u0103; cu ani \u00een urm\u0103, Ana a c\u0103l\u0103torit la Ia\u0219i; acum, Ana a deschis u\u0219a.", Segments: 3),
            (Name: "explicit-immediate", Language: "en", Text: "Adam waited. Immediately after that, Margaret entered.", Segments: 1),
            (Name: "explicit-immediate", Language: "ro", Text: "Ana a asteptat. Imediat dupa aceea, Stefan a intrat.", Segments: 1),
            (Name: "parallel-not-identical", Language: "en", Text: "Adam waited. Meanwhile, Margaret searched the room.", Segments: 2),
            (Name: "parallel-not-identical", Language: "ro", Text: "Ana a asteptat. Intre timp, Stefan a cautat cheia.", Segments: 2),
            (Name: "spatial-not-temporal", Language: "en", Text: "Adam waited. Elsewhere, Margaret searched the room.", Segments: 2),
            (Name: "spatial-not-temporal", Language: "ro", Text: "Ana a asteptat. In alta parte, Stefan a cautat cheia.", Segments: 2),
            (Name: "calendar-retrospection", Language: "en", Text: "In 1991, Adam waited. In 1990, Adam opened the shop.", Segments: 2),
            (Name: "calendar-retrospection", Language: "ro", Text: "In 1991, Ana a asteptat. In 1990, Ana a deschis magazinul.", Segments: 2),
            (Name: "calendar-conflict", Language: "en", Text: "In 1991, Adam waited. Later, in 1990, Adam opened the shop.", Segments: 2),
            (Name: "calendar-conflict", Language: "ro", Text: "In 1991, Ana a asteptat. Mai tarziu, in 1990, Ana a deschis magazinul.", Segments: 2),
            (Name: "overlap-conflict", Language: "en", Text: "In 1991, Adam waited. Meanwhile, in 1990, Adam opened the shop.", Segments: 2),
            (Name: "same-period-not-simultaneous", Language: "en", Text: "In 1990, Adam waited. In 1990, Margaret entered.", Segments: 1),
            (Name: "bare-tense-not-order", Language: "en", Text: "Adam waited. Margaret had traveled to Paris.", Segments: 2),
            (Name: "object-time-not-predecessor", Language: "en", Text: "Adam waited. Margaret searched after dinner.", Segments: 1)
        };
        var checks = 0;
        foreach (var fixture in fixtures)
        foreach (var algorithm in new[] { "article_rules", "mlnet_logistic", "mlnet_perceptron" })
        {
            var options = new DiagramOptions(Language: fixture.Language, SegmentationAlgorithm: algorithm, TrackDensity: 10);
            var response = analyze(fixture.Text, options);
            var diagram = response.Diagram;
            var ordering = diagram.TemporalOrdering!;
            Require(diagram.Segments.Count == fixture.Segments,
                $"{fixture.Name}/{fixture.Language}/{algorithm}: expected {fixture.Segments} TS, got {diagram.Segments.Count}");
            Require(ordering.Method == "evidence_partial_order_v1" && ordering.Placements.Count == diagram.Segments.Count, "Missing canonical ordering");
            Require(diagram.Relations.All(item => item.FromId.StartsWith("TS") && item.ToId.StartsWith("TS")), "Track adjacency became a temporal assertion");
            Require(diagram.TimeMl.TLinks.All(item => item.RelType is not ("UNKNOWN" or "OVERLAPS") && item.Origin != "event_text_order"), "Unsupported/default TimeML link");
            Require(diagram.TimeMl.Timex3.All(item => item.Source != "inherited_temporal_anchor"), "Invented inherited TIMEX mention");
            foreach (var segment in diagram.Segments)
                Require(segment.Text == fixture.Text[segment.SpanStart..segment.SpanEnd], "Original source span changed");
            var eventLinks = diagram.TimeMl.TLinks.Where(item => item.RelatedToEventInstance is not null).ToArray();
            if (fixture.Name is "multi-sentence-continuity" or "unresolved-perspectives" or "spatial-not-temporal"
                or "same-period-not-simultaneous" or "bare-tense-not-order" or "object-time-not-predecessor")
            {
                Require(ordering.Constraints.Count == 0 && eventLinks.Length == 0, $"{fixture.Name}: unsupported chronology");
                Require(ordering.Unresolved.Count == diagram.Events.Count - 1, "Missing unresolved event pairs");
                Require(diagram.Events.Skip(1).All(item => item.RelationToPrevious == "UNKNOWN"), "Adjacency/tense became chronology");
                Require(response.Render.Svg.Contains("? Order unresolved"), "Uncertainty absent from SVG");
            }
            if (fixture.Name is "embedded-flashback" or "calendar-retrospection")
            {
                Require(diagram.Segments[1].StoryOrder < diagram.Segments[0].StoryOrder, "Flashback drawn after the current scene");
                Require(ordering.Constraints.Any(item => item.FromId == "EV2" && item.ToId == "EV1" && item.Rel == "BEFORE"), "Missing earlier-event evidence");
                Require(eventLinks.Any(item => item.EventInstanceId == "ei2" && item.RelatedToEventInstance == "ei1" && item.RelType == "BEFORE"), "TimeML flashback reversed");
                if (fixture.Name == "embedded-flashback")
                    Require(diagram.Segments[1].StoryOrder < diagram.Segments[2].StoryOrder, "Return drawn before the flashback");
            }
            if (fixture.Name == "sequence-not-immediate")
                Require(ordering.Constraints.Count == 1 && eventLinks.Single().RelType == "BEFORE", "Then/apoi asserted immediate succession or lost its scope");
            if (fixture.Name == "explicit-immediate")
                Require(ordering.Constraints.Count == 1 && eventLinks.Single().RelType == "IBEFORE", "Explicit immediate cue lost");
            if (fixture.Name == "parallel-not-identical")
                Require(ordering.Constraints.Single().Rel == "OVERLAPS" && eventLinks.Length == 0, "Parallelism became identical intervals");
            if (fixture.Name is "calendar-conflict" or "overlap-conflict")
            {
                Require(ordering.Conflicts.Count >= 2 && ordering.Constraints.Count == 0 && eventLinks.Length == 0, "Conflicting claims were exported");
                Require(response.Analysis.Issues.Any(item => item.Code == "TEMPORAL_CONFLICT"), "Conflict not surfaced");
            }
            var facts = JsonSerializer.Serialize(new { diagram.Events, diagram.TimeMl, diagram.Relations, diagram.TemporalOrdering });
            for (var density = 10; density <= 100; density += 10)
            {
                var alternate = analyze(fixture.Text, options with { TrackDensity = density, Width = density % 20 == 0 ? 720 : 2400 });
                Require(facts == JsonSerializer.Serialize(new { alternate.Diagram.Events, alternate.Diagram.TimeMl,
                    alternate.Diagram.Relations, alternate.Diagram.TemporalOrdering }), "Density/resolution changed temporal facts");
                Require(diagram.Segments.Select(item => (item.Id, item.StoryOrder)).SequenceEqual(
                    alternate.Diagram.Segments.Select(item => (item.Id, item.StoryOrder))), "Density changed story placement");
                XDocument.Parse(alternate.Render.Svg);
            }
            checks++;
        }
        CheckCycleHandling();
        Console.WriteLine($"Temporal ordering: {checks} bilingual algorithm cases, ten densities/alternating resolutions each, and cycle isolation passed. Authored regressions, not NLP accuracy.");
    }

    private static void CheckCycleHandling()
    {
        var method = typeof(DiagramOptions).Assembly.GetType("TymTemporalOrdering", true)!
            .GetMethod("RemoveCycles", BindingFlags.NonPublic | BindingFlags.Static)!;
        var claims = new List<TemporalConstraint>
        {
            new("A", "BEFORE", "B", "", "fixture", "authored"), new("B", "BEFORE", "A", "", "fixture", "authored"),
            new("B", "BEFORE", "C", "", "fixture", "authored"), new("C", "BEFORE", "D", "", "fixture", "authored")
        };
        var conflicts = new List<TemporalOrderConflict>();
        var accepted = (List<TemporalConstraint>)method.Invoke(null, [new[] { "A", "B", "C", "D" }, claims, conflicts])!;
        Require(conflicts.Count == 2 && accepted.Count == 2 && accepted.Any(item => item.ToId == "D"), "Cycle removal discarded downstream noncyclic evidence");
        var random = new Random(42);
        for (var sample = 0; sample < 100; sample++)
        {
            var ids = Enumerable.Range(0, 6).Select(index => index.ToString()).ToArray();
            var closure = new bool[6, 6];
            var edges = new List<TemporalConstraint>();
            for (var i = 0; i < 6; i++) for (var j = 0; j < 6; j++)
                if (random.NextDouble() < 0.2)
                { closure[i, j] = true; edges.Add(new(ids[i], "BEFORE", ids[j], "", "fixture", "authored")); }
            for (var k = 0; k < 6; k++) for (var i = 0; i < 6; i++) for (var j = 0; j < 6; j++)
                closure[i, j] |= closure[i, k] && closure[k, j];
            var expected = edges.Where(edge => !closure[int.Parse(edge.ToId), int.Parse(edge.FromId)]).ToArray();
            var actual = (List<TemporalConstraint>)method.Invoke(null, [ids, edges, new List<TemporalOrderConflict>()])!;
            Require(actual.SequenceEqual(expected), "Cycle rejection differs from independent transitive-closure oracle");
        }
        var longIds = Enumerable.Range(0, 20_000).Select(index => index.ToString()).ToArray();
        var chain = Enumerable.Range(1, longIds.Length - 1).Select(index => new TemporalConstraint(longIds[index - 1], "BEFORE", longIds[index], "", "fixture", "authored")).ToList();
        var retained = (List<TemporalConstraint>)method.Invoke(null, [longIds, chain, new List<TemporalOrderConflict>()])!;
        Require(retained.Count == chain.Count, "Book-length chain failed iterative cycle handling");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
