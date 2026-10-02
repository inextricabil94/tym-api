using System.IO.Compression;
using System.Text;
using Tym.Benchmark;
using Tym.Corpus.Data;
using Xunit;

namespace Tym.Research.Tests;

public sealed class CorpusPreparationTests
{
    [Fact]
    public void Annotated_xml_preserves_tasks_and_provided_status()
    {
        const string xml = """
            <TAGS><TS ID="TS1" SPANS="0~20" TYPE="remembers" TEXT="Mara remembered home." />
            <TS ID="TS2" SPANS="21~35" TYPE="narration" TEXT="She opens it." />
            <TREL ID="R1" FROM="TS1" TO="TS2" REL="before" TRIGGER="before" />
            <TREL ID="R2" FROM="TS1" TO="missing" REL="after" /></TAGS>
            """;
        var result = TymXmlImporter.ImportText(xml, "en", "parallel-story");
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("REM", result.Rows[0].Label);
        Assert.Equal("BEFORE", result.Rows[2].Label);
        Assert.Contains("Relation cue: before", result.Rows[2].Text);
        Assert.All(result.Rows, row =>
        {
            Assert.Equal("provided_annotation", row.SourceType);
            Assert.Equal("adjudication_unknown", row.ReviewStatus);
            Assert.Equal("parallel-story", row.DocumentId);
        });
    }

    [Theory]
    [InlineData("<TAGS><TS ID='s1' TYPE='narration' SPANS='0~0' TEXT='hello'/></TAGS>")]
    [InlineData("<TAGS><TS ID='s1' TYPE='wrong' SPANS='0~5' TEXT='hello'/></TAGS>")]
    [InlineData("<TAGS><TS ID='s1' TYPE='narration' SPANS='0~5' TEXT='hello'/><TS ID='s1' TYPE='narration' SPANS='6~8' TEXT='hi'/></TAGS>")]
    public void Malformed_annotation_rows_are_rejected(string xml) => Assert.Throws<InvalidDataException>(() => TymXmlImporter.ImportText(xml, "en"));

    [Fact]
    public void External_entity_documents_are_rejected() => Assert.Throws<System.Xml.XmlException>(() =>
        TymXmlImporter.ImportText("<!DOCTYPE TAGS [<!ENTITY x SYSTEM 'file:///private'>]><TAGS>&x;</TAGS>", "en"));

    [Fact]
    public void Timebank_mentions_and_link_endpoints_are_target_marked()
    {
        const string xml = """
            <TimeML><s>Mara <EVENT eid="e1" eiid="ei1" class="OCCURRENCE" tense="PAST">a plecat</EVENT>
            <SIGNAL sid="sig1">înainte</SIGNAL> de <EVENT eid="e2" eiid="ei2" class="OCCURRENCE" tense="PAST">a ajunge</EVENT>
            <TIMEX3 tid="t1" type="DATE">ieri</TIMEX3>.</s>
            <TLINK lid="l1" eventInstanceID="ei1" relatedToEventInstance="ei2" signalID="sig1" relType="BEFORE" /></TimeML>
            """;
        var result = TimeBankImporter.ImportDocument(Encoding.UTF8.GetBytes(xml), "news-1", new Dictionary<string, string>());
        Assert.Equal(6, result.Rows.Count);
        Assert.All(result.Rows.Where(row => row.Task.StartsWith("timebank_event")), row => Assert.Contains("[TARGET]", row.Text));
        var link = Assert.Single(result.Rows, row => row.Task == "timebank_tlink");
        Assert.Equal("BEFORE", link.Label);
        Assert.Contains("From: a plecat", link.Text);
        Assert.Contains("To: a ajunge", link.Text);
        Assert.Equal(2, link.Text.Split("[TARGET]").Length - 1);
    }

    [Fact]
    public void Duplicate_signal_ids_skip_affected_links()
    {
        const string xml = """
            <TimeML><s><EVENT eid="e1" class="STATE">a</EVENT><EVENT eid="e2" class="STATE">b</EVENT>
            <SIGNAL sid="x">before</SIGNAL><SIGNAL sid="x">after</SIGNAL></s>
            <TLINK eventID="e1" relatedToEvent="e2" signalID="x" relType="BEFORE" /></TimeML>
            """;
        var result = TimeBankImporter.ImportDocument(Encoding.UTF8.GetBytes(xml), "n", new Dictionary<string, string>());
        Assert.Equal(1, result.AmbiguousSignals);
        Assert.DoesNotContain(result.Rows, row => row.Task == "timebank_tlink");
    }

    [Fact]
    public void Offline_entity_table_accepts_identical_definitions_and_rejects_conflicts()
    {
        var entities = TimeBankImporter.EntityTable("<!ENTITY abreve '&#x0103;'><!ENTITY abreve '&#x0103;'>");
        Assert.Equal("ă", entities["abreve"]);
        Assert.Throws<InvalidDataException>(() => TimeBankImporter.EntityTable("<!ENTITY a 'x'><!ENTITY a 'y'>"));
        Assert.Throws<InvalidDataException>(() => TimeBankImporter.EntityTable("<!ENTITY a '&b;'><!ENTITY b '&a;'>"));
    }

    [Fact]
    public void Book_encoding_and_document_groups_remain_explicit()
    {
        var decoded = BookImporter.Decode(Encoding.UTF8.GetBytes("Mara și-a amintit de grădină."), "Books/Novel/chapter1.txt", "Novel", "chapter1");
        Assert.Equal("novel", decoded.DocumentId);
        Assert.Equal("chapter1", decoded.ChapterId);
        Assert.Equal("utf-8-sig", decoded.Encoding);
        Assert.Contains("grădină", decoded.Text);
    }

    [Fact]
    public void Unlabeled_passages_are_deduplicated_and_bounded()
    {
        var text = string.Join(" ", Enumerable.Repeat("Mara opens the window and recalls the quiet garden.", 100));
        var source = BookImporter.Decode(Encoding.UTF8.GetBytes(text), "a.txt", "book-a", "chapter1");
        var prepared = BookImporter.Prepare([source, source with { Source = "copy.txt", ChapterId = "copy" }], "en");
        Assert.All(prepared.Rows, row =>
        {
            Assert.InRange(row.Text.Length, 300, 2400);
            Assert.Equal("unlabeled_user_provided", row.SourceType);
        });
        Assert.Equal(prepared.Rows.Count, prepared.Rows.Select(row => CorpusFiles.Hash(row.Text)).Distinct().Count());
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("Books\\outside.txt")]
    public void Zip_paths_are_rejected_without_extracting(string entryName)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open())) writer.Write("private test prose");
        stream.Position = 0;
        using var input = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Throws<InvalidDataException>(() => CorpusFiles.ValidateArchive(input));
    }

    [Fact]
    public void Duplicate_document_components_never_cross_folds()
    {
        var rows = new[] { Row("a", "d1", "same words"), Row("b", "d2", "same  words"), Row("c", "d3", "unique three"), Row("d", "d4", "unique four") };
        var split = GroupedSplit.Create(rows, 3);
        Assert.Equal(3, split.IndependentComponents);
        Assert.Equal(split.DocumentFolds["source:d1"], split.DocumentFolds["source:d2"]);
        Assert.Equal(rows.Length, split.FoldRows.Sum());
    }

    [Fact]
    public void Split_assignments_are_independent_of_input_row_order()
    {
        var rows = Enumerable.Range(0, 10).Select(index => Row("r" + index, "d" + index, "text" + index)).ToArray();
        var left = GroupedSplit.Create(rows);
        var right = GroupedSplit.Create(rows.Reverse().ToArray());
        Assert.Equal(left.DocumentFolds.OrderBy(row => row.Key), right.DocumentFolds.OrderBy(row => row.Key));
    }

    [Fact]
    public void A_single_parallel_work_cannot_supply_document_validation_folds() => Assert.Throws<InvalidDataException>(() =>
        GroupedSplit.Create([Row("a", "same-work", "Romanian passage"), Row("b", "same-work", "English passage")], 2));

    [Fact]
    public void Macro_f1_includes_unrecovered_rare_labels()
    {
        var metrics = Metrics.Calculate([("A", "A"), ("A", "A"), ("B", "A")], ["A", "B"]);
        Assert.Equal(2.0 / 3, metrics.Accuracy, 10);
        Assert.Equal(0.4, metrics.MacroF1, 10);
        Assert.Equal(0, metrics.PerClass.Single(row => row.Label == "B").Recall);
    }

    [Fact]
    public void Structured_link_features_preserve_endpoint_direction_without_using_labels()
    {
        const string text = "From: a plecat\nFrom context: Mara [TARGET] a plecat [/TARGET].\nSignal: înainte\nTo: a ajuns\nTo context: Ana [TARGET] a ajuns [/TARGET].";
        var features = StructuredFeatures.Input("timebank_tlink", text, "BEFORE");
        Assert.Equal("a plecat", features.SourceMention);
        Assert.Equal("a ajuns", features.TargetMention);
        Assert.Equal("înainte", features.Signal);
        Assert.DoesNotContain("BEFORE", features.SourceContext + features.TargetContext + features.Signal);
        Assert.Throws<ArgumentException>(() => StructuredFeatures.Input("timebank_tlink", "missing endpoints", "BEFORE"));
    }

    [Fact]
    public void Private_book_chunks_and_samples_keep_surrogate_pairs_intact()
    {
        var source = new string('x', 7) + "😀" + new string('z', 10);
        var chunks = BookImporter.Passages(source, target: 8, maximum: 8, minimum: 1);
        Assert.Equal(source, string.Concat(chunks));
        Assert.All(chunks, chunk => Assert.False(char.IsHighSurrogate(chunk[^1]) || char.IsLowSurrogate(chunk[0])));
        Assert.Equal(new string('x', 7), BookImporter.Prefix(source, 8));
    }

    private static CorpusRow Row(string id, string document, string text) => new(id, "event", text, "A", "ro", "provided_annotation", "source", "adjudication_unknown", null, document, document);
}
