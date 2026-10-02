using Microsoft.ML;
using Microsoft.ML.Data;
using Tym.Corpus.Data;
using Xunit;
using Xunit.Abstractions;

namespace Tym.Research.Tests;

/// <summary>Private archive integration tests assert model behavior and traceability, not literary accuracy.</summary>
public sealed class PrivateBookModelTests(ITestOutputHelper output)
{
    [PrivateBookFact]
    public void Attached_archive_has_the_recorded_book_and_chapter_groups()
    {
        var sources = BookImporter.ReadArchive(Environment.GetEnvironmentVariable("TYM_PRIVATE_BOOKS_ZIP")!);
        Assert.Equal(25, sources.Count);
        Assert.Equal(13, sources.Select(source => source.DocumentId).Distinct().Count());
        Assert.All(sources, source => Assert.False(string.IsNullOrWhiteSpace(source.Text)));
        output.WriteLine("Books.zip: 25 chapter/volume source files; 13 document groups; all private texts readable.");
    }

    [PrivateBookModelFact]
    public void Five_book_passages_receive_valid_narrative_model_labels()
    {
        var context = new MLContext(seed: 42);
        var directory = Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")!;
        var model = context.Model.Load(Path.Combine(directory, "segment_type_ro_sdca.zip"), out _);
        using var engine = context.Model.CreatePredictionEngine<TextInput, SegmentOutput>(model);
        var samples = Samples();
        var labels = new[] { "NAR", "REM", "GEN", "FIC" };
        foreach (var (document, text) in samples)
        {
            var prediction = engine.Predict(new() { Text = text });
            Assert.Contains(prediction.Label, labels);
            output.WriteLine($"Private book document {document}: segment label {prediction.Label} (unlabeled input; no correctness claim).");
        }
        Assert.Equal(5, samples.Count);
    }

    [PrivateBookModelFact]
    public void Five_book_passages_receive_reproducible_unlabeled_cluster_assignments()
    {
        var context = new MLContext(seed: 42);
        var directory = Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")!;
        var model = context.Model.Load(Path.Combine(directory, "books_ro_clusters_k8.zip"), out _);
        using var engine = context.Model.CreatePredictionEngine<TextInput, ClusterOutput>(model);
        foreach (var (document, text) in Samples())
        {
            var prediction = engine.Predict(new() { Text = text });
            Assert.InRange(prediction.Cluster, 1U, 8U);
            Assert.Equal(8, prediction.Score.Length);
            Assert.All(prediction.Score, score => Assert.True(float.IsFinite(score)));
            Assert.Equal(prediction.Cluster, engine.Predict(new() { Text = text }).Cluster);
            output.WriteLine($"Private book document {document}: lexical cluster {prediction.Cluster}; not a semantic class.");
        }
    }

    private static IReadOnlyList<(string Document, string Text)> Samples() => BookImporter.ReadArchive(Environment.GetEnvironmentVariable("TYM_PRIVATE_BOOKS_ZIP")!)
        .GroupBy(source => source.DocumentId).Take(5).Select(group => group.First())
        .Select(source => (source.DocumentId, BookImporter.Passages(source.Text).First())).ToArray();

    public sealed class TextInput
    {
        public string Text { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
    public sealed class SegmentOutput
    {
        [ColumnName("PredictedLabelText")]
        public string Label { get; set; } = string.Empty;
    }
    public sealed class ClusterOutput
    {
        [ColumnName("PredictedLabel")]
        public uint Cluster { get; set; }
        public float[] Score { get; set; } = [];
    }
}

public sealed class PrivateBookFactAttribute : FactAttribute
{
    public PrivateBookFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYM_PRIVATE_BOOKS_ZIP")))
            Skip = "Set TYM_PRIVATE_BOOKS_ZIP to the supplied private Books.zip archive.";
    }
}
public sealed class PrivateBookModelFactAttribute : FactAttribute
{
    public PrivateBookModelFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYM_PRIVATE_BOOKS_ZIP"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")))
            Skip = "Set TYM_PRIVATE_BOOKS_ZIP and TYM_PRIVATE_MODEL_DIR for private book-model integration tests.";
    }
}
