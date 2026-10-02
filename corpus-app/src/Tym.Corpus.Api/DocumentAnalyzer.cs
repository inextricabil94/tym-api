using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tym.Corpus.Api;

/// <summary>
/// Produces source-grounded drafts using lexical rules and the separately trained segment classifier.
/// Rules do not resolve coreference, narrative reference time, event identity or causal relations.
/// </summary>
public sealed class DocumentAnalyzer(CorpusModelService models)
{
    public const int MaximumTextLength = 50_000;
    public const int MaximumSegments = 500;
    public const int MaximumMentions = 2_000;
    public const string ResearchLimit = "Draft sentence boundaries and lexical mention candidates require human review. "
        + "A segment classifier prediction is not an adjudicated annotation. No automatic coreference, causal inference or complete temporal reasoning is performed. Reviewed corrections are not automatically gold data.";
    private static readonly Regex Times = Rule(@"\b(?:\d{4}-\d{2}-\d{2}|(?:19|20)\d{2}|yesterday|today|tomorrow|last\s+week|next\s+week|ieri|azi|astăzi|mâine|săptămâna\s+trecută|săptămâna\s+viitoare)\b", ignoreCase: true);
    private static readonly Regex Places = Rule(@"\b(?i:in|at|to|from|în|la|spre|din)\s+(?<place>\p{Lu}[\p{L}\p{M}’'-]+(?:[ -]\p{Lu}[\p{L}\p{M}’'-]+){0,2})\b");
    private static readonly Regex Names = Rule(@"\b\p{Lu}[\p{L}\p{M}’'-]+\b");
    private static readonly Regex Events = Rule(@"\b(?:arrived|arrive|arrives|visited|visit|visits|walked|walk|walks|waited|wait|waits|left|leave|leaves|met|meet|meets|said|say|says|remembered|remember|remembers|imagined|imagine|imagines|read|reads|wrote|write|writes|returned|return|returns|ajuns|ajunge|venit|vine|plecat|pleacă|spus|spune|amintit|amintește|citit|citește|merge|mers|întors|întoarce|așteptat|așteaptă|vizitat|vizitează)\b", ignoreCase: true);
    private static readonly HashSet<string> NonNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Yesterday", "Today", "Tomorrow", "Now", "When", "While", "Then", "After", "Before", "Later",
        "He", "She", "They", "We", "It", "The", "This", "That", "Once", "In", "At", "From", "To",
        "Ieri", "Azi", "Astăzi", "Mâine", "Acum", "Când", "În", "La", "Din", "Spre", "El", "Ea", "Ei", "Ele", "Noi", "După", "Înainte", "Apoi"
    };

    public static ApiError? ValidateRequest(DocumentAnalyzeRequest? request)
    {
        if (request is null) return new ApiError("A document analysis request is required.", "invalid_request");
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaximumTextLength)
            return new ApiError($"Text must contain 1 to {MaximumTextLength} UTF-16 code units and cannot be blank.", "invalid_text");
        if (request.Language is not ("en" or "ro"))
            return new ApiError("Language must be en or ro.", "invalid_language");
        if (request.DocumentDate is not null && !TryDocumentDate(request.DocumentDate, out _))
            return new ApiError("Document date must be an explicit valid yyyy-MM-dd date.", "invalid_document_date");
        return null;
    }

    public DocumentAnalyzeResponse Analyze(DocumentAnalyzeRequest request)
    {
        var error = ValidateRequest(request);
        if (error is not null) throw new ArgumentException(error.Error, nameof(request));
        var text = request.Text!;
        var modelId = "segment_type_" + request.Language;
        var diagnostics = new List<DocumentDiagnostic>();
        var segments = new List<DocumentSegment>();
        var available = true;
        var allSpans = SentenceSpans(text).ToArray();
        var spans = allSpans.Take(MaximumSegments).ToArray();
        if (allSpans.Length > spans.Length)
            AddOnce(diagnostics, "segment_candidate_limit", "Only the first 500 sentence candidates were drafted; the complete source text is retained.");
        IReadOnlyList<CorpusPredictionResponse>? predictions = null;
        try { predictions = models.PredictMany(modelId, spans.Select(span => text[span.Start..span.End]).ToArray()); }
        catch (ModelUnavailableException)
        {
            available = false;
            AddOnce(diagnostics, "segment_model_unavailable", "No usable segment model is available. Sentence drafts and lexical mention candidates remain available.");
        }
        var segmentIndex = 0;
        foreach (var (start, end) in spans)
        {
            var slice = text[start..end];
            string? label = null;
            FieldProvenance labelOrigin;
            if (predictions is not null)
            {
                var result = predictions[segmentIndex];
                if (DocumentInventories.SegmentLabels.Contains(result.PredictedLabel, StringComparer.Ordinal))
                {
                    label = result.PredictedLabel;
                    labelOrigin = new("mlnet_model", result.Provenance + " " + result.ResearchLimit, modelId);
                }
                else
                {
                    available = false;
                    labelOrigin = new("model_unavailable", "The model label is incompatible with the TYM segment inventory.", modelId);
                    AddOnce(diagnostics, "incompatible_segment_model", "Segment model output does not match the TYM label inventory; no label was assigned.");
                }
            }
            else
            {
                available = false;
                labelOrigin = new("model_unavailable", "No usable segment model is available; no label was assigned.", modelId);
                AddOnce(diagnostics, "segment_model_unavailable", "No usable segment model is available. Sentence drafts and lexical mention candidates remain available.");
            }
            var provenance = SpanProvenance();
            provenance["boundaries"] = new("rule_sentence_boundary", "Punctuation/newline heuristic; this is not a learned TYM segment boundary.");
            provenance["label"] = labelOrigin;
            segments.Add(new("s" + (segments.Count + 1), start, end, slice, label, "draft", provenance));
            segmentIndex++;
        }

        var mentions = new List<DocumentMention>();
        DateOnly? dct = request.DocumentDate is not null && TryDocumentDate(request.DocumentDate, out var date) ? date : null;
        foreach (Match match in Times.Matches(text))
        {
            var absolute = NormalizeAbsoluteTime(match.Value);
            var normalized = absolute ?? (dct is null ? null : NormalizeTime(match.Value, dct.Value));
            AddMention("TIME", match.Index, match.Length, "rule_time_lexicon", normalized,
                absolute is not null ? new("rule_time_lexicon", "Explicit absolute calendar expression in source text.")
                : normalized is not null ? new("rule_dct_anchor", "Explicit document date: " + request.DocumentDate + "; lexical calendar anchor only.")
                    : new("rule_time_lexicon", dct is null ? "Unresolved: no document date was supplied." : "This expression could not be normalized by the lexical rule."));
        }
        foreach (Match match in Places.Matches(text))
        {
            var group = match.Groups["place"];
            AddMention("LOCATION", group.Index, group.Length, "rule_location_preposition");
        }
        foreach (Match match in Names.Matches(text))
        {
            if (!NonNames.Contains(match.Value) && !mentions.Any(m => m.Kind is "LOCATION" or "TIME" && match.Index < m.End && match.Index + match.Length > m.Start))
                AddMention("ACTOR", match.Index, match.Length, "rule_capitalized_name");
        }
        foreach (Match match in Events.Matches(text)) AddMention("EVENT", match.Index, match.Length, "rule_event_lexicon");
        var hash = SourceHash(text);
        var document = new AnnotatedDocument(1, "D-" + hash[..16], text, hash, "utf-16", request.Language!,
            request.DocumentDate, "draft", segments, mentions.OrderBy(m => m.Start).ThenBy(m => m.Kind, StringComparer.Ordinal).ToArray(), []);
        return new(document, available, DocumentInventories.Relations, diagnostics, ResearchLimit);

        void AddMention(string kind, int start, int length, string method, string? normalized = null, FieldProvenance? normalization = null)
        {
            if (mentions.Count >= MaximumMentions)
            {
                AddOnce(diagnostics, "mention_candidate_limit", "Only the first 2000 lexical mention candidates were drafted; the complete source text is retained.");
                return;
            }
            var origin = SpanProvenance();
            origin["kind"] = new(method, "Lexical candidate; human review is required.");
            origin["normalized_value"] = normalization ?? new(method, "No normalized identity or calendar value was inferred.");
            mentions.Add(new("m" + (mentions.Count + 1), kind, start, start + length, text.Substring(start, length), normalized, "draft", origin));
        }
    }

    public static string SourceHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    public static bool TryDocumentDate(string value, out DateOnly date) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static Dictionary<string, FieldProvenance> SpanProvenance() => new(StringComparer.Ordinal)
    {
        ["start"] = new("source_slice", "Inclusive UTF-16 source offset."),
        ["end"] = new("source_slice", "Exclusive UTF-16 source offset."),
        ["text"] = new("source_slice", "Exact unchanged source substring."),
        ["review_status"] = new("user_supplied", "Generated draft; no human review has been recorded.")
    };

    private static string? NormalizeTime(string value, DateOnly dct)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"\s+", " ");
        var delta = normalized switch
        {
            "yesterday" or "ieri" => -1, "today" or "azi" or "astăzi" => 0,
            "tomorrow" or "mâine" => 1, _ => (int?)null
        };
        if (delta is not null)
        {
            try { return dct.AddDays(delta.Value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return null;
    }

    private static string? NormalizeAbsoluteTime(string value) => TryDocumentDate(value, out var absolute)
        ? absolute.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : Regex.IsMatch(value, @"^(?:19|20)\d{2}$") ? value : null;

    private static Regex Rule(string pattern, bool ignoreCase = false) => new(pattern,
        RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), TimeSpan.FromSeconds(2));
    private static void AddOnce(List<DocumentDiagnostic> diagnostics, string code, string message)
    {
        if (!diagnostics.Any(d => d.Code == code)) diagnostics.Add(new("warning", code, message));
    }

    /// <summary>Trims span edges by moving offsets only; source text itself is never normalized.</summary>
    private static IEnumerable<(int Start, int End)> SentenceSpans(string text)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var newline = text[index] is '\r' or '\n';
            var end = index + 1;
            if (!newline && text[index] is '.' or '!' or '?')
            {
                while (end < text.Length && text[end] is '.' or '!' or '?' or '"' or '\'' or '”' or '’' or ')' or ']') end++;
                if (end < text.Length && !char.IsWhiteSpace(text[end])) continue;
            }
            else if (!newline) continue;
            var left = start;
            var right = newline ? index : end;
            while (left < right && char.IsWhiteSpace(text[left])) left++;
            while (right > left && char.IsWhiteSpace(text[right - 1])) right--;
            if (left < right) yield return (left, right);
            start = end;
            index = end - 1;
        }
        var finalStart = start;
        var finalEnd = text.Length;
        while (finalStart < finalEnd && char.IsWhiteSpace(text[finalStart])) finalStart++;
        while (finalEnd > finalStart && char.IsWhiteSpace(text[finalEnd - 1])) finalEnd--;
        if (finalStart < finalEnd) yield return (finalStart, finalEnd);
    }
}
