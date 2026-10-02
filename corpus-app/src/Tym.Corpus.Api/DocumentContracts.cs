namespace Tym.Corpus.Api;

/// <summary>Optional document date is an explicitly supplied calendar date, never the server clock.</summary>
public sealed record DocumentAnalyzeRequest(string? Text, string? Language, string? DocumentDate = null);

/// <summary>Field-level evidence describes origin, not probability or adjudication.</summary>
public sealed record FieldProvenance(string Method, string Evidence, string? ModelId = null);

/// <summary>All spans use inclusive start and exclusive end in the unchanged .NET UTF-16 source string.</summary>
public sealed record DocumentSegment(string Id, int Start, int End, string Text, string? Label,
    string ReviewStatus, IReadOnlyDictionary<string, FieldProvenance> Provenance);

public sealed record DocumentMention(string Id, string Kind, int Start, int End, string Text,
    string? NormalizedValue, string ReviewStatus, IReadOnlyDictionary<string, FieldProvenance> Provenance);

/// <summary>Endpoints refer to annotation IDs. Families have separate inventories and endpoint types.</summary>
public sealed record DocumentRelation(string Id, string Family, string FromId, string ToId,
    string Label, string ReviewStatus, IReadOnlyDictionary<string, FieldProvenance> Provenance);

/// <summary>An export preserves source text, corrections and provenance. Reviewed does not mean gold.</summary>
public sealed record AnnotatedDocument(int SchemaVersion, string DocumentId, string Text,
    string TextSha256, string OffsetEncoding, string Language, string? DocumentDate, string ReviewStatus,
    IReadOnlyList<DocumentSegment> Segments, IReadOnlyList<DocumentMention> Mentions,
    IReadOnlyList<DocumentRelation> Relations);

public sealed record DocumentDiagnostic(string Severity, string Code, string Message, string? AnnotationId = null);

public sealed record RelationInventories(IReadOnlyList<string> Tym, IReadOnlyList<string> Tlink,
    IReadOnlyList<string> Slink, IReadOnlyList<string> Alink);

public sealed record DocumentAnalyzeResponse(AnnotatedDocument Document, bool SegmentModelAvailable,
    RelationInventories RelationInventories, IReadOnlyList<DocumentDiagnostic> Diagnostics, string ResearchLimit);

public sealed record PrecedenceEdge(string FromId, string ToId, string Family, string RelationId);

/// <summary>Edges represent a partial order. No rank is assigned to disconnected or unrelated annotations.</summary>
public sealed record StoryOrderResult(string Status, IReadOnlyList<PrecedenceEdge> PrecedenceEdges,
    IReadOnlyList<IReadOnlyList<string>> SimultaneousGroups, string Scope);

public sealed record DocumentValidationResponse(bool Valid, IReadOnlyList<DocumentDiagnostic> Diagnostics,
    StoryOrderResult StoryOrder, RelationInventories RelationInventories, string ResearchLimit);

/// <summary>Separate corpus inventories; TYM narrative modes are not TimeML grammatical tenses.</summary>
public static class DocumentInventories
{
    public static IReadOnlyList<string> SegmentLabels { get; } = Array.AsReadOnly(new[] { "NAR", "REM", "SUP", "GEN", "FIC" });
    public static IReadOnlyList<string> MentionKinds { get; } = Array.AsReadOnly(new[] { "TIME", "ACTOR", "LOCATION", "EVENT" });
    public static RelationInventories Relations { get; } = new(
        Array.AsReadOnly(new[] { "BEFORE", "IMMEDIATELY_BEFORE", "SIMULTANEOUS", "IMMEDIATELY_AFTER", "AFTER" }),
        Array.AsReadOnly(new[] { "AFTER", "BEFORE", "BEGINS", "BEGUN_BY", "DURING", "DURING_INV", "ENDED_BY", "ENDS", "IAFTER", "IBEFORE", "IDENTITY", "INCLUDES", "IS_INCLUDED", "SIMULTANEOUS" }),
        Array.AsReadOnly(new[] { "CONDITIONAL", "COUNTER_FACTIVE", "EVIDENTIAL", "FACTIVE", "MODAL", "NEG_EVIDENTIAL" }),
        Array.AsReadOnly(new[] { "CONTINUES", "CULMINATES", "INITIATES", "REINITIATES", "TERMINATES" }));

    public static IReadOnlyList<string> ProvenanceMethods { get; } = Array.AsReadOnly(new[]
    {
        "source_slice", "rule_sentence_boundary", "rule_time_lexicon", "rule_dct_anchor",
        "rule_capitalized_name", "rule_location_preposition", "rule_event_lexicon",
        "mlnet_model", "model_unavailable", "user_supplied", "human_corrected"
    });

    public static IReadOnlyList<string>? RelationLabels(string? family) => family switch
    {
        "TYM" => Relations.Tym, "TLINK" => Relations.Tlink,
        "SLINK" => Relations.Slink, "ALINK" => Relations.Alink, _ => null
    };
}
