using Tym.Corpus.Api;
using Xunit;

namespace Tym.Corpus.Api.Tests;

/// <summary>Book-like prose is original test text. Assertions concern source spans and reviewed graph behavior.</summary>
public sealed class DocumentWorkbenchTests
{
    private static readonly DocumentAnalyzer Drafts = new(new CorpusModelService(null));

    [Theory]
    [InlineData("en", "Yesterday Mara arrived in London. She remembered the garden. Tomorrow she will return.")]
    [InlineData("ro", "Ieri Mara a ajuns în București. Și-a amintit de grădină. Mâine va pleca.")]
    [InlineData("ro", "  📚 Ana a citit în Brașov.\r\n\r\nMara și-a amintit de Iași.  ")]
    [InlineData("en", "<script>alert('book')</script> Mara read the letter. She imagined a different ending.")]
    public void Drafts_preserve_immutable_source_and_half_open_spans(string language, string text)
    {
        var result = Drafts.Analyze(new(text, language));
        Assert.Equal(text, result.Document.Text);
        Assert.Equal(DocumentAnalyzer.SourceHash(text), result.Document.TextSha256);
        Assert.Equal("utf-16", result.Document.OffsetEncoding);
        Assert.NotEmpty(result.Document.Segments);
        Assert.All(result.Document.Segments, segment =>
        {
            Assert.Equal(text[segment.Start..segment.End], segment.Text);
            Assert.Equal("draft", segment.ReviewStatus);
            Assert.Null(segment.Label);
        });
        Assert.All(result.Document.Mentions, mention => Assert.Equal(text[mention.Start..mention.End], mention.Text));
        Assert.Empty(result.Document.Relations);
        Assert.True(DocumentValidator.Validate(result.Document).Valid);
        Assert.Equal("unknown", DocumentValidator.Validate(result.Document).StoryOrder.Status);
    }

    [Theory]
    [InlineData("en", "Yesterday Mara arrived. Tomorrow she leaves.")]
    [InlineData("ro", "Ieri Mara a ajuns. Mâine va pleca.")]
    public void Relative_dates_require_explicit_document_date(string language, string text)
    {
        var unknown = Drafts.Analyze(new(text, language));
        Assert.All(unknown.Document.Mentions.Where(mention => mention.Kind == "TIME"), mention => Assert.Null(mention.NormalizedValue));
        var anchored = Drafts.Analyze(new(text, language, "2026-10-02"));
        var times = anchored.Document.Mentions.Where(mention => mention.Kind == "TIME").ToArray();
        Assert.Equal("2026-10-01", times[0].NormalizedValue);
        Assert.Equal("2026-10-03", times[1].NormalizedValue);
        Assert.All(times, mention => Assert.Equal("rule_dct_anchor", mention.Provenance["normalized_value"].Method));
    }

    [Fact]
    public void Absolute_dates_do_not_require_a_document_date()
    {
        var result = Drafts.Analyze(new("In 1998 Mara arrived. On 2026-10-02 she returned.", "en"));
        var times = result.Document.Mentions.Where(mention => mention.Kind == "TIME").ToArray();
        Assert.Equal("1998", times[0].NormalizedValue);
        Assert.Equal("2026-10-02", times[1].NormalizedValue);
        Assert.All(times, mention => Assert.Equal("rule_time_lexicon", mention.Provenance["normalized_value"].Method));
    }

    [Theory]
    [InlineData(null, "en", null)]
    [InlineData(" ", "en", null)]
    [InlineData("book", "fr", null)]
    [InlineData("book", "en", "2026-02-30")]
    [InlineData("book", "en", "today")]
    public void Invalid_document_requests_are_rejected(string? text, string? language, string? date) =>
        Assert.NotNull(DocumentAnalyzer.ValidateRequest(new(text, language, date)));

    [Fact]
    public void Draft_relations_remain_outside_reviewed_story_order()
    {
        var document = ThreeSegments();
        document = document with { Relations = [Relation("r1", "s1", "s2", "BEFORE", "draft")] };
        var result = DocumentValidator.Validate(document);
        Assert.True(result.Valid);
        Assert.Empty(result.StoryOrder.PrecedenceEdges);
        Assert.Equal("unknown", result.StoryOrder.Status);
    }

    [Fact]
    public void Reviewed_after_is_oriented_and_unrelated_segments_are_not_ordered()
    {
        var document = ThreeSegments() with { Relations = [Relation("r1", "s1", "s2", "AFTER")] };
        var result = DocumentValidator.Validate(document);
        Assert.True(result.Valid);
        var edge = Assert.Single(result.StoryOrder.PrecedenceEdges);
        Assert.Equal("s2", edge.FromId);
        Assert.Equal("s1", edge.ToId);
        Assert.DoesNotContain(result.StoryOrder.PrecedenceEdges, item => item.FromId == "s3" || item.ToId == "s3");
        Assert.Equal("partial", result.StoryOrder.Status);
    }

    [Fact]
    public void Cyclic_story_order_is_diagnosed()
    {
        var document = ThreeSegments() with { Relations = [Relation("r1", "s1", "s2", "BEFORE"), Relation("r2", "s2", "s3", "BEFORE"), Relation("r3", "s3", "s1", "BEFORE")] };
        var result = DocumentValidator.Validate(document);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "temporal_cycle");
    }

    [Fact]
    public void Simultaneous_equivalence_cannot_also_have_strict_precedence()
    {
        var document = ThreeSegments() with { Relations = [Relation("r1", "s1", "s2", "SIMULTANEOUS"), Relation("r2", "s2", "s1", "BEFORE")] };
        var result = DocumentValidator.Validate(document);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "simultaneous_precedence_contradiction");
    }

    [Fact]
    public void Timebank_links_cannot_use_narrative_segment_ids()
    {
        var document = ThreeSegments() with { Relations = [Relation("r1", "s1", "s2", "BEFORE") with { Family = "TLINK" }] };
        var result = DocumentValidator.Validate(document);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "invalid_relation_endpoint");
    }

    [Fact]
    public void Tampered_source_and_offsets_are_diagnosed()
    {
        var document = ThreeSegments();
        document = document with { Text = document.Text + " Changed.", Segments = [document.Segments[0] with { End = 4, Text = "wrong" }] };
        var result = DocumentValidator.Validate(document);
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "source_hash_mismatch");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "source_slice_mismatch");
    }

    [Fact]
    public void Span_boundaries_cannot_split_an_emoji()
    {
        var document = Drafts.Analyze(new("📚 Mara read.", "en")).Document;
        var segment = document.Segments[0] with { Start = 1, Text = document.Text[1..] };
        var result = DocumentValidator.Validate(document with { Segments = [segment] });
        Assert.False(result.Valid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "split_surrogate_pair");
    }

    [Fact]
    public void Candidate_limits_retain_the_full_source_text()
    {
        var text = string.Join(" ", Enumerable.Repeat("A.", 550));
        var result = Drafts.Analyze(new(text, "en"));
        Assert.Equal(500, result.Document.Segments.Count);
        Assert.Equal(text, result.Document.Text);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "segment_candidate_limit");
    }

    private static AnnotatedDocument ThreeSegments() => Drafts.Analyze(new("Mara arrived. Ana read. Elian waited.", "en")).Document;
    private static DocumentRelation Relation(string id, string from, string to, string label, string status = "reviewed") =>
        new(id, "TYM", from, to, label, status, new Dictionary<string, FieldProvenance>
        {
            ["from_id"] = new("human_corrected", "Explicit reviewer endpoint selection."),
            ["to_id"] = new("human_corrected", "Explicit reviewer endpoint selection."),
            ["label"] = new("human_corrected", "Explicit reviewer relation label."),
            ["review_status"] = new("user_supplied", "Reviewer chose this status; no independent adjudication implied.")
        });
}
