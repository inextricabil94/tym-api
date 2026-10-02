using System.Text.RegularExpressions;

namespace Tym.Corpus.Api;

/// <summary>
/// Checks export structure and a limited subset of temporal consistency. This is not full Allen algebra,
/// TimeML closure, causal inference or verification that an edited relation is true in the narrative.
/// </summary>
public static class DocumentValidator
{
    public const string ConsistencyScope = "Checks supplied TYM/TLINK strict precedence cycles and simultaneous/identity contradictions. "
        + "BEFORE/AFTER and immediate variants are oriented as strict precedence; immediacy itself is not proved. "
        + "Other interval labels, SLINK and ALINK are validated structurally but are not temporally inferred. "
        + "Story order exposes reviewed edges only; unrelated nodes have no inferred order.";
    private static readonly Regex Identifier = new(@"^[A-Za-z][A-Za-z0-9_.:-]{0,127}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static DocumentValidationResponse Validate(AnnotatedDocument? document)
    {
        var diagnostics = new List<DocumentDiagnostic>();
        var graphRelations = new List<DocumentRelation>();
        if (document is null)
        {
            Error("invalid_document", "An annotated document is required.");
            return Finish();
        }
        if (document.SchemaVersion != 1) Error("invalid_schema_version", "Only schema version 1 is supported.");
        if (string.IsNullOrWhiteSpace(document.Text) || document.Text.Length > DocumentAnalyzer.MaximumTextLength)
            Error("invalid_text", "Source text must be nonblank and no longer than 50000 UTF-16 code units.");
        if (document.OffsetEncoding != "utf-16") Error("invalid_offset_encoding", "Offsets must use utf-16 code units.");
        if (document.Language is not ("en" or "ro")) Error("invalid_language", "Language must be en or ro.");
        if (document.DocumentDate is not null && !DocumentAnalyzer.TryDocumentDate(document.DocumentDate, out _))
            Error("invalid_document_date", "Document date must be an explicit valid yyyy-MM-dd date.");
        if (document.Text is not null && !string.Equals(document.TextSha256, DocumentAnalyzer.SourceHash(document.Text), StringComparison.Ordinal))
            Error("source_hash_mismatch", "Source text does not match its SHA256 digest.");
        CheckId(document.DocumentId);
        CheckReview(document.ReviewStatus, document.DocumentId);
        if (document.Segments is null || document.Mentions is null || document.Relations is null)
        {
            Error("missing_annotation_collection", "Segments, mentions and relations must be JSON arrays, including when empty.");
            return Finish();
        }
        if ((long)document.Segments.Count + document.Mentions.Count + document.Relations.Count > 10000)
        {
            Error("too_many_annotations", "At most 10000 annotations may be validated together.");
            return Finish();
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var segments = new Dictionary<string, DocumentSegment>(StringComparer.Ordinal);
        var mentions = new Dictionary<string, DocumentMention>(StringComparer.Ordinal);
        foreach (var segment in document.Segments)
        {
            if (segment is null) { Error("null_annotation", "A segment cannot be null."); continue; }
            CheckUnique(segment.Id);
            if (!string.IsNullOrEmpty(segment.Id)) segments.TryAdd(segment.Id, segment);
            CheckSpan(segment.Id, segment.Start, segment.End, segment.Text);
            CheckReview(segment.ReviewStatus, segment.Id);
            if (segment.Label is not null && !DocumentInventories.SegmentLabels.Contains(segment.Label, StringComparer.Ordinal))
                Error("invalid_segment_label", "Segment labels must belong to the separate TYM narrative-mode inventory.", segment.Id);
            if (segment.ReviewStatus == "reviewed" && segment.Label is null)
                Error("reviewed_segment_without_label", "A reviewed segment requires an explicit TYM label.", segment.Id);
            CheckProvenance(segment.Provenance, segment.Id, "start", "end", "text", "label", "review_status");
        }
        foreach (var mention in document.Mentions)
        {
            if (mention is null) { Error("null_annotation", "A mention cannot be null."); continue; }
            CheckUnique(mention.Id);
            if (!string.IsNullOrEmpty(mention.Id)) mentions.TryAdd(mention.Id, mention);
            CheckSpan(mention.Id, mention.Start, mention.End, mention.Text);
            CheckReview(mention.ReviewStatus, mention.Id);
            if (!DocumentInventories.MentionKinds.Contains(mention.Kind, StringComparer.Ordinal))
                Error("invalid_mention_kind", "Mention kind must be TIME, ACTOR, LOCATION or EVENT.", mention.Id);
            if (mention.Kind == "TIME" && mention.NormalizedValue is not null && document.DocumentDate is null
                && mention.Provenance?.GetValueOrDefault("normalized_value")?.Method == "rule_dct_anchor")
                Error("missing_document_date", "A rule-anchored TIME value requires an explicit document date.", mention.Id);
            CheckProvenance(mention.Provenance, mention.Id, "start", "end", "text", "kind", "normalized_value", "review_status");
        }
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in document.Relations)
        {
            if (relation is null) { Error("null_annotation", "A relation cannot be null."); continue; }
            CheckUnique(relation.Id);
            CheckReview(relation.ReviewStatus, relation.Id);
            CheckProvenance(relation.Provenance, relation.Id, "from_id", "to_id", "label", "review_status");
            var inventory = DocumentInventories.RelationLabels(relation.Family);
            if (inventory is null || !inventory.Contains(relation.Label, StringComparer.Ordinal))
            {
                Error("invalid_relation_label", "Relation family and label must match one of the separate inventories.", relation.Id);
                continue;
            }
            var endpointsValid = relation.Family switch
            {
                "TYM" => HasSegment(relation.FromId) && HasSegment(relation.ToId),
                "TLINK" => HasMention(relation.FromId, "EVENT", "TIME") && HasMention(relation.ToId, "EVENT", "TIME"),
                "SLINK" or "ALINK" => HasMention(relation.FromId, "EVENT") && HasMention(relation.ToId, "EVENT"),
                _ => false
            };
            if (!endpointsValid)
            {
                Error("invalid_relation_endpoint", "Relation endpoints are missing or do not match this family's allowed annotation types.", relation.Id);
                continue;
            }
            if (relation.FromId == relation.ToId)
            {
                Error("self_relation", "Relation endpoints must be distinct annotations.", relation.Id);
                continue;
            }
            if (!pairs.Add(relation.Family + "\0" + relation.FromId + "\0" + relation.ToId + "\0" + relation.Label))
                Error("duplicate_relation", "The same directed relation is present more than once.", relation.Id);
            if (relation.Family is "TYM" or "TLINK") graphRelations.Add(relation);
        }

        // Validate all authored constraints; draft constraints never become reviewed story order implicitly.
        CheckTemporalConsistency(graphRelations);
        return Finish();

        bool HasSegment(string? id) => id is not null && segments.ContainsKey(id);
        bool HasMention(string? id, params string[] kinds) => id is not null && mentions.TryGetValue(id, out var mention) && kinds.Contains(mention.Kind, StringComparer.Ordinal);
        void CheckId(string? id)
        {
            if (id is null || !Identifier.IsMatch(id)) Error("invalid_annotation_id", "IDs must start with an ASCII letter and contain at most 128 letters, digits, underscores, dots, colons or hyphens.", id);
        }
        void CheckUnique(string? id)
        {
            CheckId(id);
            if (id is not null && !ids.Add(id)) Error("duplicate_annotation_id", "Annotation IDs must be unique across segments, mentions and relations.", id);
        }
        void CheckReview(string? status, string? id)
        {
            if (status is not ("draft" or "reviewed")) Error("invalid_review_status", "Review status must be draft or reviewed; gold is not inferred.", id);
        }
        void CheckSpan(string? id, int start, int end, string? slice)
        {
            if (document.Text is null || start < 0 || end <= start || end > document.Text.Length)
            {
                Error("invalid_span", "Span must be nonempty and within the half-open UTF-16 source bounds.", id);
                return;
            }
            if (CutsSurrogate(document.Text, start) || CutsSurrogate(document.Text, end))
                Error("split_surrogate_pair", "Span boundaries cannot split a UTF-16 surrogate pair.", id);
            if (!string.Equals(slice, document.Text[start..end], StringComparison.Ordinal))
                Error("source_slice_mismatch", "Annotation text must exactly match the source substring at its offsets.", id);
        }
        void CheckProvenance(IReadOnlyDictionary<string, FieldProvenance>? provenance, string? id, params string[] required)
        {
            if (provenance is null || required.Any(field => !provenance.ContainsKey(field)))
            {
                Error("missing_field_provenance", "Required annotation fields must each retain provenance.", id);
                return;
            }
            foreach (var item in provenance)
            {
                if (item.Value is null || !DocumentInventories.ProvenanceMethods.Contains(item.Value.Method, StringComparer.Ordinal)
                    || string.IsNullOrWhiteSpace(item.Value.Evidence))
                    Error("invalid_field_provenance", "Provenance requires a supported origin method and nonblank evidence.", id);
            }
        }
        void CheckTemporalConsistency(IReadOnlyList<DocumentRelation> relations)
        {
            var equivalence = Equivalence(relations);
            var edges = StrictEdges(relations).ToArray();
            var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var edge in edges)
            {
                var from = equivalence.Find(edge.FromId);
                var to = equivalence.Find(edge.ToId);
                if (from == to)
                {
                    Error("simultaneous_precedence_contradiction", "Strict precedence contradicts supplied simultaneous/identity equivalence.", edge.RelationId);
                    continue;
                }
                if (!adjacency.TryGetValue(from, out var targets)) adjacency[from] = targets = new(StringComparer.Ordinal);
                targets.Add(to);
                adjacency.TryAdd(to, new(StringComparer.Ordinal));
            }
            // Iterative Kahn algorithm avoids call-stack dependence on submitted graph depth.
            var incoming = adjacency.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
            foreach (var targets in adjacency.Values) foreach (var target in targets) incoming[target]++;
            var ready = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            var visited = 0;
            while (ready.TryDequeue(out var node))
            {
                visited++;
                foreach (var target in adjacency[node]) if (--incoming[target] == 0) ready.Enqueue(target);
            }
            if (visited != adjacency.Count) Error("temporal_cycle", "Supplied strict temporal relations contain a cycle, including after equivalence merging.");
        }
        void Error(string code, string message, string? id = null) => diagnostics.Add(new("error", code, message, id));
        DocumentValidationResponse Finish()
        {
            var reviewed = graphRelations.Where(relation => relation.ReviewStatus == "reviewed").ToArray();
            var edges = StrictEdges(reviewed).ToArray();
            var equivalence = Equivalence(reviewed);
            var groups = reviewed.Where(IsSimultaneous).SelectMany(r => new[] { r.FromId, r.ToId }).Distinct(StringComparer.Ordinal)
                .GroupBy(equivalence.Find, StringComparer.Ordinal).Select(group => (IReadOnlyList<string>)group.Order(StringComparer.Ordinal).ToArray()).ToArray();
            var valid = !diagnostics.Any(d => d.Severity == "error");
            var status = !valid ? "inconsistent" : edges.Length > 0 || groups.Length > 0 ? "partial" : "unknown";
            return new(valid, diagnostics, new(status, edges, groups, ConsistencyScope), DocumentInventories.Relations, DocumentAnalyzer.ResearchLimit);
        }
    }

    private static bool CutsSurrogate(string source, int boundary) => boundary > 0 && boundary < source.Length
        && char.IsHighSurrogate(source[boundary - 1]) && char.IsLowSurrogate(source[boundary]);
    private static bool IsSimultaneous(DocumentRelation relation) => relation.Label is "SIMULTANEOUS" or "IDENTITY";
    private static IEnumerable<PrecedenceEdge> StrictEdges(IEnumerable<DocumentRelation> relations)
    {
        foreach (var relation in relations)
        {
            if (relation.Label is "BEFORE" or "IMMEDIATELY_BEFORE" or "IBEFORE")
                yield return new(relation.FromId, relation.ToId, relation.Family, relation.Id);
            else if (relation.Label is "AFTER" or "IMMEDIATELY_AFTER" or "IAFTER")
                yield return new(relation.ToId, relation.FromId, relation.Family, relation.Id);
        }
    }
    private static DisjointSets Equivalence(IEnumerable<DocumentRelation> relations)
    {
        var sets = new DisjointSets();
        foreach (var relation in relations.Where(IsSimultaneous)) sets.Join(relation.FromId, relation.ToId);
        return sets;
    }
    private sealed class DisjointSets
    {
        private readonly Dictionary<string, string> _parents = new(StringComparer.Ordinal);
        public string Find(string id)
        {
            _parents.TryAdd(id, id);
            var current = id;
            while (_parents[current] != current) current = _parents[current];
            while (_parents[id] != id) { var next = _parents[id]; _parents[id] = current; id = next; }
            return current;
        }
        public void Join(string left, string right) { var a = Find(left); var b = Find(right); if (a != b) _parents[b] = a; }
    }
}
