using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record TrackDescriptor(string NaturalKey, string ContextKey);

internal static class TymDensityCache
{
    public static readonly int[] Presets = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    public static CachedDensityView View(DiagramResponse response) => new(response.Diagram.Tracks,
        response.Diagram.Segments.Select(segment => new SegmentPlacement(segment.Id, segment.TrackId, segment.StoryOrder)).ToList(),
        response.Diagram.Endpoints, response.Diagram.Relations, response.Diagram.Boundaries,
        response.Analysis, response.Render, response.Xml, response.Warnings);

    public static DensityCacheInfo Build(DiagramResponse baseline, IReadOnlyList<TrackDescriptor> descriptors,
        int[] baselineAssignments, Func<int, int[], DiagramResponse> generate)
    {
        var options = new List<DensityCacheOption>();
        var views = new Dictionary<string, CachedDensityView>();
        var partitions = new List<(int[] Assignments, string ViewId, int TrackCount)>
        {
            (baselineAssignments, "base", baseline.Diagram.Tracks.Count)
        };
        foreach (var density in Presets)
        {
            var assignments = TymTrackGranularity.Assign(descriptors, density);
            var existing = partitions.FindIndex(partition => partition.Assignments.AsSpan().SequenceEqual(assignments));
            if (existing >= 0)
            {
                var partition = partitions[existing];
                options.Add(new DensityCacheOption(density, partition.ViewId, partition.TrackCount));
                continue;
            }
            var response = generate(density, assignments);
            var id = $"v{views.Count + 1}";
            var diagram = response.Diagram;
            views[id] = new CachedDensityView(diagram.Tracks,
                diagram.Segments.Select(segment => new SegmentPlacement(segment.Id, segment.TrackId, segment.StoryOrder)).ToList(),
                diagram.Endpoints, diagram.Relations, diagram.Boundaries, response.Analysis, response.Render, response.Xml, response.Warnings);
            partitions.Add((assignments, id, diagram.Tracks.Count));
            options.Add(new DensityCacheOption(density, id, diagram.Tracks.Count));
        }
        return new DensityCacheInfo(options, views);
    }
}

internal static class TymTrackGranularity
{
    public static int[] Assign(IReadOnlyList<TrackDescriptor> descriptors, int density)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(density);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(density, 100);
        if (descriptors.Count == 0) return [];

        var natural = descriptors.Select((item, index) => (item, index))
            .GroupBy(pair => pair.item.NaturalKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Select(pair => pair.index).ToList()).ToList();
        List<List<int>> partitions;
        var assignment = new int[descriptors.Count];
        if (density <= 50)
        {
            var contexts = natural.Select((group, index) => (group, index))
                .GroupBy(pair => descriptors[pair.group[0]].ContextKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Select(pair => pair.index).ToList()).ToList();
            var target = contexts.Count + (int)Math.Round((natural.Count - contexts.Count) * density / 50.0);
            partitions = Refine(contexts, target);
            for (var i = 0; i < partitions.Count; i++)
                foreach (var group in partitions[i])
                    foreach (var index in natural[group]) assignment[index] = i;
        }
        else
        {
            var target = natural.Count + (int)Math.Round((descriptors.Count - natural.Count) * (density - 50) / 50.0);
            partitions = Refine(natural, target);
            for (var i = 0; i < partitions.Count; i++)
                foreach (var index in partitions[i]) assignment[index] = i;
        }
        return assignment;
    }

    private static List<List<int>> Refine(List<List<int>> groups, int target)
    {
        // Repeated balanced splits produce nested, deterministic partitions as density rises.
        var queue = new PriorityQueue<List<int>, (int Size, int Order)>();
        var order = 0;
        foreach (var group in groups) queue.Enqueue(group, (-group.Count, order++));
        while (queue.Count < target)
        {
            var group = queue.Dequeue();
            var middle = group.Count / 2;
            queue.Enqueue(group.GetRange(0, middle), (-middle, order++));
            queue.Enqueue(group.GetRange(middle, group.Count - middle), (middle - group.Count, order++));
        }
        return queue.UnorderedItems.Select(item => item.Element).OrderBy(group => group[0]).ToList();
    }
}

internal static partial class TymTrackTitles
{
    private static readonly HashSet<string> StopWords = new(
        ("a an the and or but of to in on at by for from with as is are was were be been being " +
         "has have had he she it they we you his her their this that then when while before after " +
         "un o unui unei si sau dar de din la pe cu pentru prin spre este sunt era erau fost fi " +
         "a au am ai ati va vor sa se s eu el ea ei ele noi voi lui le lor isi l i il nu ca ce " +
         "care cand inainte dupa apoi intr intre capitol capitolul chapter").Split(' '), StringComparer.Ordinal);

    public static string Generate(IReadOnlyList<string> eventTexts, string fallback)
    {
        var candidates = eventTexts.Select(text => HeadingPattern().Replace(text, ""))
            .Select(text => WhitespacePattern().Replace(text, " ").Trim())
            .Where(text => text.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return fallback;
        var tokens = candidates.Select(text => WordPattern().Matches(text).Select(match => Fold(match.Value))
            .Where(word => word.Length > 2 && !StopWords.Contains(word)).Distinct().ToArray()).ToArray();
        var frequencies = tokens.SelectMany(words => words).GroupBy(word => word)
            .ToDictionary(group => group.Key, group => group.Count());
        var best = Enumerable.Range(0, candidates.Length).OrderByDescending(index =>
            tokens[index].Sum(word => 1 + Math.Log(frequencies[word])) / Math.Sqrt(Math.Max(1, tokens[index].Length)))
            .ThenBy(index => index).First();
        var words = candidates[best].Split(' ');
        var title = string.Join(" ", words.Take(12));
        var truncated = words.Length > 12 || title.Length > 96;
        if (title.Length > 96)
        {
            title = title[..93];
            var lastSpace = title.LastIndexOf(' ');
            if (lastSpace > 40) title = title[..lastSpace];
            if (char.IsHighSurrogate(title[^1])) title = title[..^1];
        }
        title = title.TrimEnd(' ', '.', ',', ';', ':', '!', '?');
        return title + (truncated ? "..." : "");
    }

    private static string Fold(string text) => string.Concat(text.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
        .ToLowerInvariant();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
    [GeneratedRegex(@"^[\t \f]*(?:capitolul|capitol|chapter)[\t ]+(?:[0-9]+|[ivxlcdm]+)\b[^\r\n]*(?:\r?\n|\r|$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();
}
