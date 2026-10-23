using System.Text.Json;

internal static class TemporalSegmentationChecks
{
    public static void Run(Func<string, DiagramOptions, DiagramResponse> analyze)
    {
        var cases = new[]
        {
            (Name: "shared-scene", Language: "en", Text: "Adam opened the door and Margaret entered the room. Karl waited beside them.\n\nThen Adam closed the door.", Segments: 1),
            (Name: "shared-scene", Language: "ro", Text: "Ana a intrat in camera si Stefan a deschis usa. Ion a asteptat langa ei.\n\nApoi Ana a cautat cheia.", Segments: 1),
            (Name: "journey", Language: "en", Text: "Adam traveled from London. Adam arrived in Paris. Then Adam entered the hotel.", Segments: 1),
            (Name: "journey", Language: "ro", Text: "Ana a plecat din Bucuresti. Ana a calatorit la Iasi. Apoi Ana a intrat in hotel.", Segments: 1),
            (Name: "routine-sequence", Language: "en", Text: "Adam opened the door. After dinner, Adam searched for Karl. Then Adam waited.", Segments: 1),
            (Name: "routine-sequence", Language: "ro", Text: "Ana a deschis usa. Dupa cina, Ana a cautat cheia. Apoi Ana a asteptat.", Segments: 1),
            (Name: "flashback-return", Language: "en", Text: "Adam waited at home. Years earlier, Adam had traveled to Paris. Margaret had waited there. Now, Adam opened the door. Adam searched the room.", Segments: 3),
            (Name: "flashback-return", Language: "ro", Text: "Ana a asteptat acasa. Cu ani in urma, Ana a calatorit la Iasi. Stefan a asteptat acolo. Acum, Ana a deschis usa. Ana a cautat cheia.", Segments: 3),
            (Name: "forward-jump", Language: "en", Text: "Adam waited at home. Three years later, Adam opened a shop. Adam searched for Karl.", Segments: 2),
            (Name: "forward-jump", Language: "ro", Text: "Ana a asteptat acasa. Dupa trei ani, Ana a deschis un magazin. Ana a cautat cheia.", Segments: 2),
            (Name: "anchor-through-unanchored-events", Language: "en", Text: "In 1990, Adam opened a shop. Margaret entered the shop. In 1991, Adam closed the shop. Karl waited outside.", Segments: 2),
            (Name: "anchor-through-unanchored-events", Language: "ro", Text: "In 1990, Ana a deschis un magazin. Stefan a intrat in magazin. In 1991, Ana a inchis magazinul. Ion a asteptat afara.", Segments: 2),
            (Name: "same-calendar-period", Language: "en", Text: "In 1990, Adam opened a shop. On 1990-05-02, Adam searched for Karl. In 1990, Margaret entered the shop.", Segments: 1),
            (Name: "parallel-development", Language: "en", Text: "Adam waited at home. Meanwhile, Margaret searched in Paris. Margaret entered the hotel.", Segments: 2),
            (Name: "parallel-development", Language: "ro", Text: "Ana a asteptat acasa. Intre timp, Stefan a cautat cheia. Stefan a intrat in camera.", Segments: 2),
            (Name: "reality-plane", Language: "en", Text: "Adam waited at home. He imagined a garden. He walked in the garden. In reality, Adam searched the room.", Segments: 3),
            (Name: "same-day", Language: "en", Text: "Today, Adam opened the shop. On the same day, Margaret entered the shop. Today, Karl waited outside.", Segments: 1),
            (Name: "same-day-diacritics", Language: "ro", Text: "Ast\u0103zi, Ana a deschis magazinul. \u00cen aceea\u0219i zi, \u0218tefan a intrat. Azi, Ion a a\u0219teptat afar\u0103.", Segments: 1),
            (Name: "weekday", Language: "en", Text: "On Monday, Adam opened a shop. Adam waited there. On Tuesday, Adam searched for Karl.", Segments: 2),
            (Name: "duration-not-shift", Language: "en", Text: "Adam waited for two hours. Margaret waited for three hours. Adam searched the room.", Segments: 1),
            (Name: "non-temporal-discourse-marker", Language: "en", Text: "Adam opened the door. In fact, Margaret waited beside him. In reality, Karl waited too.", Segments: 1),
            (Name: "standalone-time-headings", Language: "en", Text: "Adam waited. Years earlier.\nKarl traveled. Karl waited.\nNow. Adam opened the door.", Segments: 3),
            (Name: "standalone-time-headings", Language: "ro", Text: "Ana a asteptat. Cu ani in urma.\nStefan a calatorit. Stefan a asteptat.\nAcum. Ana a deschis usa.", Segments: 3),
            (Name: "standalone-calendar-heading", Language: "en", Text: "1990.\nAdam opened a shop. Margaret entered.\n1991.\nAdam closed the shop.", Segments: 2),
            (Name: "calendar-precision-retained", Language: "en", Text: "On 1990-05-02, Adam opened a shop. In 1990, Margaret entered. On 1990-05-03, Adam searched the shop.", Segments: 2),
            (Name: "invalid-date-not-anchor", Language: "en", Text: "Adam waited in 1990. Adam searched on 1991-02-31. Margaret entered.", Segments: 1),
            (Name: "communication-development", Language: "en", Text: "Adam waited with Margaret. They started a phone call. They talked. They ended the call. Adam waited.", Segments: 3),
            (Name: "communication-development", Language: "ro", Text: "Ana a asteptat cu Stefan. Ei au inceput o convorbire telefonica. Ei au discutat. Ei au incheiat convorbirea. Ana a asteptat.", Segments: 3),
            (Name: "explicit-separation", Language: "en", Text: "Adam waited with Margaret. They parted ways. Adam traveled to Paris.", Segments: 2),
            (Name: "perspective-development", Language: "en", Text: "Adam waited. According to Margaret, Adam searched the room. According to Margaret, Adam opened the door. According to Karl, Adam waited.", Segments: 3)
        };
        var count = 0;
        foreach (var fixture in cases)
        foreach (var algorithm in new[] { "article_rules", "mlnet_logistic", "mlnet_perceptron" })
        {
            var options = new DiagramOptions(Language: fixture.Language, SegmentationAlgorithm: algorithm);
            var response = analyze(fixture.Text, options);
            Require(response.Diagram.Segments.Count == fixture.Segments,
                $"{fixture.Language}/{fixture.Name}/{algorithm}: expected {fixture.Segments} TS, got {response.Diagram.Segments.Count}");
            Require(response.Segmentation!.Unit == "temporal_development", "Missing semantic segmentation unit");
            Require(response.Diagram.Segments.SelectMany(segment => segment.EventIds).SequenceEqual(response.Diagram.Events.Select(ev => ev.Id)), "Dropped/reordered events");
            foreach (var segment in response.Diagram.Segments)
                Require(segment.Text == fixture.Text[segment.SpanStart..segment.SpanEnd], "TS is not an exact original source span");
            foreach (var decision in response.Segmentation.Decisions.Skip(1).Where(item => item.IsBoundary))
                Require(decision.Evidence.Any(item => item != "temporal_continuity" && item != "same_time_reference"), "Boundary has no temporal/development evidence");
            if (fixture.Name is "flashback-return" or "standalone-time-headings")
            {
                var expected = fixture.Language == "en" ? new[] { "Years earlier", "Now" } : new[] { "Cu ani in urma", "Acum" };
                Require(response.Diagram.Segments.Skip(1).Select(segment => segment.SpanStart)
                    .SequenceEqual(expected.Select(marker => fixture.Text.IndexOf(marker, StringComparison.Ordinal))), "Temporal marker attached to the wrong TS");
                var boundaries = response.Segmentation.Decisions.Where(decision => decision.IsBoundary).ToArray();
                Require(boundaries[1].Evidence.Contains("flashback_entry") && boundaries[2].Evidence.Contains("narrative_time_return"), "Missing flashback/return evidence");
            }
            if (fixture.Name == "anchor-through-unanchored-events")
            {
                Require(response.Diagram.Segments[1].SpanStart == fixture.Text.IndexOf("In 1991", StringComparison.Ordinal), "Calendar boundary moved to an unanchored event");
                Require(response.Segmentation.Decisions[1].TimeReference == "calendar:1990", "Unanchored event lost its temporal context");
            }
            var alternate = analyze(fixture.Text, options with { TrackDensity = 100 });
            Require(JsonSerializer.Serialize(response.Segmentation) == JsonSerializer.Serialize(alternate.Segmentation), "Density changed temporal segmentation");
            count++;
        }
        var longScene = string.Join("\n\n", Enumerable.Repeat("Adam opened the door. Margaret entered the room. Adam waited beside her.", 60));
        foreach (var algorithm in new[] { "article_rules", "mlnet_logistic", "mlnet_perceptron" })
        {
            var response = analyze(longScene, new DiagramOptions(SegmentationAlgorithm: algorithm));
            Require(response.Diagram.Events.Count >= 180 && response.Diagram.Segments.Count == 1, "Long scene degenerated into phrase segmentation");
            Require(response.Diagram.Segments[0].Text == longScene, "Long scene lost paragraphs or source text");
            count++;
        }
        Console.WriteLine($"Temporal development: {count} algorithm cases passed (multi-sentence scenes, journeys, flashbacks/returns, anchors, parallelism, modes, exact spans, density invariance).");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
