using System.Xml.Linq;

namespace Tym.Corpus.Data;

/// <summary>Converts supplied TYM XML without using summaries or identifiers as model features.</summary>
public static class TymXmlImporter
{
    private static readonly IReadOnlyDictionary<string, string> SegmentLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["narration"] = "NAR", ["remembers"] = "REM", ["supporting"] = "SUP", ["generalknowledge"] = "GEN", ["fiction"] = "FIC" };
    private static readonly IReadOnlyDictionary<string, string> RelationLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["before"] = "BEFORE", ["immediately_before"] = "IMMEDIATELY_BEFORE", ["after"] = "AFTER", ["immediately_after"] = "IMMEDIATELY_AFTER", ["simultaneous"] = "SIMULTANEOUS", ["simmultaneous"] = "SIMULTANEOUS" };

    private static Dictionary<string, XElement> Index(XElement root, string name)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var item in root.Elements(name))
        {
            var id = ((string?)item.Attribute("ID") ?? "").Trim();
            if (id.Length == 0 || !result.TryAdd(id, item)) throw new InvalidDataException($"Missing or repeated {name} ID.");
        }
        return result;
    }

    public static (IReadOnlyList<CorpusRow> Rows, object Report) Import(string path, string language,
        string sourceGroup = "provided-hln-motiw-narrative") => ImportText(File.ReadAllText(path), language, sourceGroup);

    public static (IReadOnlyList<CorpusRow> Rows, object Report) ImportText(string xml, string language,
        string sourceGroup = "provided-hln-motiw-narrative")
    {
        if (language is not ("en" or "ro")) throw new ArgumentException("Language must be en or ro.");
        var root = CorpusFiles.ParseXml(xml).Root;
        if (root?.Name.LocalName != "TAGS") throw new InvalidDataException("Expected a TYM TAGS root.");
        var segments = Index(root, "TS");
        var relations = Index(root, "TREL");
        var rows = new List<CorpusRow>();
        var spans = new HashSet<(int Start, int End)>();
        foreach (var (id, segment) in segments)
        {
            var span = ((string?)segment.Attribute("SPANS") ?? "").Split('~');
            if (span.Length != 2 || !int.TryParse(span[0], out var start) || !int.TryParse(span[1], out var end)
                || start < 0 || end <= start || !spans.Add((start, end))) throw new InvalidDataException("Invalid or repeated TS span.");
            var text = ((string?)segment.Attribute("TEXT") ?? "").Trim();
            var rawLabel = ((string?)segment.Attribute("TYPE") ?? "").Trim();
            if (text.Length == 0 || !SegmentLabels.TryGetValue(rawLabel, out var label)) throw new InvalidDataException("Missing text or unsupported TS TYPE.");
            rows.Add(Row($"{sourceGroup}:{language}:segment:{id}", "segment_type", text, label));
        }
        var skipped = 0;
        foreach (var (id, relation) in relations)
        {
            var from = (string?)relation.Attribute("FROM") ?? "";
            var to = (string?)relation.Attribute("TO") ?? "";
            if (!segments.TryGetValue(from, out var source) || !segments.TryGetValue(to, out var target)) { skipped++; continue; }
            if (!RelationLabels.TryGetValue(((string?)relation.Attribute("REL") ?? "").Trim(), out var label)) throw new InvalidDataException("Unsupported TREL REL.");
            var feature = $"Earlier segment: {((string?)source.Attribute("TEXT") ?? "").Trim()}\nRelation cue: {((string?)relation.Attribute("TRIGGER") ?? "").Trim()}\nLater segment: {((string?)target.Attribute("TEXT") ?? "").Trim()}";
            rows.Add(Row($"{sourceGroup}:{language}:relation:{id}", "temporal_relation", feature, label));
        }
        return (rows, new { language, segments = segments.Count, relations_in_xml = relations.Count,
            relations_imported = relations.Count - skipped, relations_skipped_missing_segment_endpoint = skipped,
            tasks = rows.GroupBy(row => row.Task).ToDictionary(group => group.Key, group => group.GroupBy(row => row.Label).ToDictionary(labels => labels.Key, labels => labels.Count())),
            provenance = "provided_annotation/adjudication_unknown", source_group = sourceGroup,
            notes = new[] { "Parallel translations belong to one source work.", "Source offsets require reconciliation with any separately supplied raw edition." } });

        CorpusRow Row(string id, string task, string text, string label) => new(id, task, text, label, language,
            "provided_annotation", sourceGroup, "adjudication_unknown", null, sourceGroup, sourceGroup);
    }
}
