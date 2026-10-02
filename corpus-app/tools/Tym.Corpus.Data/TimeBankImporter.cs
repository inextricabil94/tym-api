using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tym.Corpus.Data;

/// <summary>Reads the supplied ISO-TimeML archive using a bounded, offline entity table.</summary>
public static class TimeBankImporter
{
    private static readonly HashSet<string> Builtins = ["amp", "lt", "gt", "apos", "quot"];
    private static readonly HashSet<string> AnnotationTags = ["EVENT", "TIMEX3", "SIGNAL", "ENAMEX", "CARDINAL", "NUMEX"];
    private static readonly Regex EntityReference = new(@"&([A-Za-z][\w.-]*);", RegexOptions.CultureInvariant);

    public static Dictionary<string, string> EntityTable(string dtd)
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(dtd, "<!ENTITY\\s+([A-Za-z][\\w.-]*)\\s+['\"]([^'\"]*)['\"]\\s*>", RegexOptions.Singleline))
        {
            var name = match.Groups[1].Value;
            var value = match.Groups[2].Value;
            if (definitions.TryGetValue(name, out var previous) && previous != value)
                throw new InvalidDataException("Conflicting offline DTD entity definitions.");
            definitions[name] = value;
        }
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        string Resolve(string name, HashSet<string> stack)
        {
            if (Builtins.Contains(name)) return WebUtility.HtmlDecode("&" + name + ";");
            if (resolved.TryGetValue(name, out var cached)) return cached;
            if (!definitions.TryGetValue(name, out var definition) || !stack.Add(name) || stack.Count > 20)
                throw new InvalidDataException("Unresolved or recursive DTD entity.");
            var value = EntityReference.Replace(definition, match => Resolve(match.Groups[1].Value, new(stack)));
            value = WebUtility.HtmlDecode(value);
            if (value.Length > 8 || value.IndexOfAny(['<', '>', '&']) >= 0) throw new InvalidDataException("Unsupported DTD entity value.");
            return resolved[name] = value;
        }
        foreach (var name in definitions.Keys) Resolve(name, []);
        foreach (var (alias, original) in new[] { ("ABREVE", "Abreve"), ("SCEDIL", "Scedil"), ("TCEDIL", "Tcedil") })
            if (resolved.TryGetValue(original, out var value)) resolved[alias] = value;
        return resolved;
    }

    public static string SecureText(byte[] raw, IReadOnlyDictionary<string, string> entities)
    {
        var text = new UTF8Encoding(false, true).GetString(raw).TrimStart('\uFEFF');
        text = Regex.Replace(text, @"<!DOCTYPE\s+[^>]*>", match =>
        {
            if (match.Value.Contains('[') || match.Value.Contains(']')) throw new InvalidDataException("Inline DTD subsets are unsupported.");
            return string.Empty;
        }, RegexOptions.Singleline);
        text = Regex.Replace(text, @"<\?xml\s+[^?]*\?>", string.Empty, RegexOptions.Singleline);
        return EntityReference.Replace(text, match => Builtins.Contains(match.Groups[1].Value) ? match.Value
            : entities.TryGetValue(match.Groups[1].Value, out var value) ? value
            : throw new InvalidDataException("XML uses an entity absent from the offline archive table."));
    }

    public static string VisibleText(XElement element, XElement? target = null)
    {
        var text = new StringBuilder();
        foreach (var node in element.Nodes())
        {
            if (node is XText value) text.Append(value.Value);
            else if (node is XElement child)
            {
                if (AnnotationTags.Contains(child.Name.LocalName)) text.Append(' ');
                text.Append(VisibleText(child, target));
                if (AnnotationTags.Contains(child.Name.LocalName)) text.Append(' ');
            }
        }
        var result = CorpusFiles.NormalizeSpaces(text.ToString());
        return ReferenceEquals(element, target) ? $"[TARGET] {result} [/TARGET]" : result;
    }

    private static string TargetContext(XElement element)
    {
        var context = element.Ancestors().FirstOrDefault(parent =>
            parent.Name.LocalName.ToLowerInvariant() is "s" or "headline" or "leadpara" or "lp") ?? element;
        return VisibleText(context, element);
    }

    private static string Window(string context)
    {
        var start = context.IndexOf("[TARGET]", StringComparison.Ordinal);
        var end = context.IndexOf("[/TARGET]", StringComparison.Ordinal);
        if (start < 0 || end < 0) return context[..Math.Min(500, context.Length)];
        var left = Math.Max(0, start - 200);
        return context[left..Math.Min(context.Length, end + "[/TARGET]".Length + 200)];
    }

    public static (IReadOnlyList<CorpusRow> Rows, int MissingEndpoints, int AmbiguousSignals) ImportDocument(
        byte[] raw, string documentId, IReadOnlyDictionary<string, string> entities)
    {
        var root = CorpusFiles.ParseXml(SecureText(raw, entities)).Root;
        if (root?.Name.LocalName != "TimeML") throw new InvalidDataException("Expected TimeML root.");
        var rows = new List<CorpusRow>();
        var participants = new Dictionary<string, (string Mention, string Context)>(StringComparer.Ordinal);
        void AddParticipant(string? id, (string Mention, string Context) value)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (participants.TryGetValue(id, out var existing) && existing != value) throw new InvalidDataException("Ambiguous participant identifier.");
            participants[id] = value;
        }
        CorpusRow Row(string task, string id, string text, string label) => new($"ro-timebank:{documentId}:{task}:{id}", task,
            text, label, "ro", "provided_annotation", "ro-timebank-isotimeml", "adjudication_unknown", null, documentId, documentId);
        foreach (var item in root.Descendants("EVENT"))
        {
            var mention = VisibleText(item);
            var context = TargetContext(item);
            foreach (var attribute in new[] { "eid", "eiid", "eventID" }) AddParticipant((string?)item.Attribute(attribute), (mention, context));
            foreach (var (attribute, task) in new[] { ("class", "timebank_event_class"), ("tense", "timebank_event_tense") })
            {
                var label = ((string?)item.Attribute(attribute) ?? "").Trim();
                if (label.Length > 0) rows.Add(Row(task, (string?)item.Attribute("eid") ?? (string?)item.Attribute("eiid") ?? "event", $"Context: {context}\nEvent: {mention}", label));
            }
        }
        foreach (var item in root.Descendants("TIMEX3"))
        {
            var mention = VisibleText(item);
            var context = TargetContext(item);
            AddParticipant((string?)item.Attribute("tid"), (mention, context));
            var label = ((string?)item.Attribute("type") ?? "").Trim();
            if (label.Length > 0) rows.Add(Row("timebank_timex_type", (string?)item.Attribute("tid") ?? "timex", $"Context: {context}\nTime expression: {mention}", label));
        }
        var signals = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.Descendants("SIGNAL"))
        {
            var id = (string?)item.Attribute("sid");
            if (string.IsNullOrEmpty(id)) continue;
            if (!signals.TryAdd(id, VisibleText(item))) ambiguous.Add(id);
        }
        var missingCount = 0;
        var ambiguousCount = 0;
        foreach (var tag in new[] { "TLINK", "SLINK", "ALINK" })
        {
            var index = 0;
            foreach (var link in root.Descendants(tag))
            {
                index++;
                var from = new[] { "eventID", "eventInstanceID", "timeID" }.Select(name => (string?)link.Attribute(name)).FirstOrDefault(value => !string.IsNullOrEmpty(value));
                var to = new[] { "relatedToEvent", "relatedToEventInstance", "relatedToTime", "subordinatedEvent" }.Select(name => (string?)link.Attribute(name)).FirstOrDefault(value => !string.IsNullOrEmpty(value));
                if (from is null || to is null || !participants.TryGetValue(from, out var source) || !participants.TryGetValue(to, out var target)) { missingCount++; continue; }
                var signalId = (string?)link.Attribute("signalID") ?? "";
                if (ambiguous.Contains(signalId)) { ambiguousCount++; continue; }
                var signal = signals.GetValueOrDefault(signalId, string.Empty);
                var label = ((string?)link.Attribute("relType") ?? "").Trim();
                if (label.Length == 0) continue;
                var task = "timebank_" + tag.ToLowerInvariant();
                var text = $"From: {source.Mention}\nFrom context: {Window(source.Context)}\nSignal: {signal}\nTo: {target.Mention}\nTo context: {Window(target.Context)}";
                rows.Add(Row(task, $"{tag.ToLowerInvariant()}-{index}-{(string?)link.Attribute("lid") ?? "unid"}", text, label));
            }
        }
        return (rows, missingCount, ambiguousCount);
    }

    public static (IReadOnlyList<CorpusRow> Rows, object Report) ImportArchive(string path, string? exclusionsPath)
    {
        var hash = CorpusFiles.FileHash(path);
        var exclusions = new Dictionary<string, string>(StringComparer.Ordinal);
        if (exclusionsPath is not null)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(exclusionsPath));
            if (manifest.RootElement.GetProperty("archive_sha256").GetString() != hash) throw new InvalidDataException("Exclusions target a different archive hash.");
            foreach (var item in manifest.RootElement.GetProperty("documents").EnumerateArray())
                exclusions.Add(item.GetProperty("document_id").GetString()!, item.GetProperty("reason").GetString()!);
        }
        using var archive = ZipFile.OpenRead(path);
        CorpusFiles.ValidateArchive(archive);
        var dtd = archive.GetEntry("Ro-TimeBank/dtd/ISO-TimeML-Ro.dtd") ?? throw new InvalidDataException("Missing archive DTD.");
        var entities = EntityTable(Encoding.UTF8.GetString(CorpusFiles.ReadEntry(dtd)));
        var entries = archive.Entries.Where(entry => entry.FullName.StartsWith("Ro-TimeBank/data/ro/", StringComparison.Ordinal)
            && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(entry => entry.FullName, StringComparer.Ordinal).ToArray();
        if (entries.Length == 0) throw new InvalidDataException("No Romanian documents found.");
        var available = entries.Select(entry => Path.GetFileNameWithoutExtension(entry.FullName)).ToHashSet(StringComparer.Ordinal);
        if (exclusions.Keys.Any(id => !available.Contains(id))) throw new InvalidDataException("Exclusion refers to an absent document.");
        var rows = new List<CorpusRow>();
        var missing = 0;
        var ambiguous = 0;
        foreach (var entry in entries)
        {
            var documentId = Path.GetFileNameWithoutExtension(entry.FullName);
            if (exclusions.ContainsKey(documentId)) continue;
            var result = ImportDocument(CorpusFiles.ReadEntry(entry), documentId, entities);
            rows.AddRange(result.Rows);
            missing += result.MissingEndpoints;
            ambiguous += result.AmbiguousSignals;
        }
        if (rows.Select(row => row.Id).Distinct().Count() != rows.Count) throw new InvalidDataException("Duplicate generated TimeBank row IDs.");
        return (rows, new
        {
            archive = Path.GetFileName(path), archive_sha256 = hash, archive_documents = entries.Length,
            documents = entries.Length - exclusions.Count, output_rows = rows.Count,
            excluded_documents = exclusions.OrderBy(item => item.Key).Select(item => new { document_id = item.Key, reason = item.Value }),
            links_skipped_missing_endpoint = missing, links_skipped_ambiguous_signal = ambiguous,
            tasks = rows.GroupBy(row => row.Task).ToDictionary(group => group.Key, group => new
            { rows = group.Count(), labels = group.GroupBy(row => row.Label).ToDictionary(labels => labels.Key, labels => labels.Count()) }),
            provenance = "provided_annotation/adjudication_unknown", label_families = "ISO-TimeML tasks; separate from TYM",
            importer = "C#/.NET offline XML conversion; no external DTD resolution"
        });
    }
}
