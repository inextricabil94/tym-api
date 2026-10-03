using System.Text.Json;
using Tym.Benchmark;
using Tym.Corpus.Data;
using Xunit;

namespace Tym.Research.Tests;

/// <summary>
/// Software-contract coverage using original authored, unlabeled miniature passages.
/// Finite metrics and artifact integrity do not estimate semantic accuracy on real books.
/// </summary>
public sealed class BookAlgorithmStudyTests
{
    [Fact]
    public void Five_book_algorithms_preserve_unlabeled_holdout_sample_and_artifact_contracts()
    {
        var temporaryParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetFullPath(Path.Combine(temporaryParent, "tym-book-algorithm-contract-" + Guid.NewGuid().ToString("N")));
        RequireOwnedTemporaryDirectory(directory, temporaryParent);
        Directory.CreateDirectory(directory);
        try
        {
            var rows = AuthoredPassages();
            var input = Path.Combine(directory, "authored-unlabeled.jsonl");
            var reportPath = Path.Combine(directory, "aggregate.json");
            var models = Path.Combine(directory, "private-models");
            CorpusFiles.WriteJsonLines(input, rows);

            BookExploration.Run(input, reportPath, models, seed: 42, clusters: 2, rank: 8);

            var reportText = File.ReadAllText(reportPath);
            using var report = JsonDocument.Parse(reportText);
            var root = report.RootElement;
            Assert.Equal("exploratory_unlabeled_books", root.GetProperty("status").GetString());
            Assert.Equal("en", root.GetProperty("language").GetString());
            Assert.Equal(rows.Length, root.GetProperty("total_rows").GetInt32());
            Assert.Equal(6, root.GetProperty("documents").GetInt32());
            Assert.Equal(6, root.GetProperty("independent_document_components").GetInt32());
            Assert.Equal("complete_document_component_holdout", root.GetProperty("split_kind").GetString());
            Assert.Equal(CorpusFiles.FileHash(input), root.GetProperty("input_sha256").GetString());
            var training = root.GetProperty("training_rows").GetInt32();
            var heldout = root.GetProperty("heldout_rows").GetInt32();
            Assert.Equal(rows.Length, training + heldout);
            Assert.True(training > 0 && heldout > 0);
            // Each source work contains eight passages; the split holds out whole works.
            Assert.Equal(0, training % 8);
            Assert.Equal(0, heldout % 8);
            Assert.Equal(6, root.GetProperty("training_documents").GetInt32() + root.GetProperty("heldout_documents").GetInt32());

            var algorithms = root.GetProperty("algorithms").EnumerateArray()
                .ToDictionary(value => value.GetProperty("algorithm").GetString()!, StringComparer.Ordinal);
            var expected = new[] { "mlnet_kmeans", "mlnet_pca_kmeans", "csharp_torchsharp_autoencoder",
                "csharp_average_linkage_hierarchical", "csharp_dbscan" };
            Assert.Equal(expected.Order(StringComparer.Ordinal), algorithms.Keys.Order(StringComparer.Ordinal));
            Assert.All(algorithms.Values, value => Assert.Equal(JsonValueKind.Null, value.GetProperty("semantic_accuracy").ValueKind));

            foreach (var name in new[] { "mlnet_kmeans", "mlnet_pca_kmeans" })
            {
                var algorithm = algorithms[name];
                Assert.Equal(training, algorithm.GetProperty("training_metrics").GetProperty("rows").GetInt32());
                Assert.Equal(heldout, algorithm.GetProperty("heldout_metrics").GetProperty("rows").GetInt32());
                AssertFiniteNonnegative(algorithm.GetProperty("fit_seconds_including_shared_featurizer"));
                AssertFiniteNonnegative(algorithm.GetProperty("assessment_seconds"));
                AssertFiniteNonnegative(algorithm.GetProperty("heldout_metrics").GetProperty("average_squared_centroid_distance"));
                var weight = Artifact(models, algorithm);
                using var manifest = JsonDocument.Parse(File.ReadAllText(weight + ".manifest.json"));
                var provenance = manifest.RootElement;
                Assert.Equal(name, provenance.GetProperty("algorithm").GetString());
                Assert.Equal("unlabeled_user_provided", provenance.GetProperty("source_type").GetString());
                Assert.Equal("exploratory_unlabeled_book_clustering", provenance.GetProperty("task").GetString());
                Assert.Equal(CorpusFiles.FileHash(weight), provenance.GetProperty("model_sha256").GetString());
                Assert.Equal(CorpusFiles.FileHash(input), provenance.GetProperty("input_sha256").GetString());
                Assert.Equal(training, provenance.GetProperty("fitted_rows").GetInt32());
                Assert.Equal(heldout, provenance.GetProperty("heldout_rows").GetInt32());
                Assert.Equal(42, provenance.GetProperty("seed").GetInt32());
                Assert.Equal(2, provenance.GetProperty("clusters").GetInt32());
            }

            var autoencoder = algorithms["csharp_torchsharp_autoencoder"];
            Assert.Equal(training, autoencoder.GetProperty("training_rows").GetInt32());
            Assert.Equal(heldout, autoencoder.GetProperty("heldout_rows").GetInt32());
            Assert.Equal(8, autoencoder.GetProperty("feature_dimensions").GetInt32());
            Assert.Equal(4, autoencoder.GetProperty("bottleneck_dimensions").GetInt32());
            Assert.False(autoencoder.GetProperty("model_exported").GetBoolean());
            foreach (var metric in new[] { "last_epoch_training_mean_squared_error", "heldout_reconstruction_mean_squared_error",
                "heldout_training_mean_reconstruction_mean_squared_error", "heldout_zero_reconstruction_mean_squared_error",
                "fit_seconds_algorithm_only", "prediction_seconds_algorithm_only" })
                AssertFiniteNonnegative(autoencoder.GetProperty(metric));
            Assert.False(autoencoder.GetProperty("metadata").GetProperty("validation_used_for_fit").GetBoolean());
            Assert.False(autoencoder.TryGetProperty("encoded_validation", out _));

            foreach (var name in new[] { "csharp_average_linkage_hierarchical", "csharp_dbscan" })
            {
                var algorithm = algorithms[name];
                Assert.Equal(JsonValueKind.Null, algorithm.GetProperty("heldout_metrics").ValueKind);
                Assert.Equal(256, algorithm.GetProperty("sample_limit").GetInt32());
                var sampleRows = algorithm.GetProperty("sample_rows").GetInt32();
                Assert.Equal(Math.Min(256, training), sampleRows);
                Assert.Contains("training passages", algorithm.GetProperty("sampling").GetString()!);
                var sample = algorithm.GetProperty("sample_metrics");
                Assert.Equal(sampleRows, sample.GetProperty("rows").GetInt32());
                var assigned = sample.GetProperty("cluster_counts").EnumerateArray().Sum(value => value.GetProperty("rows").GetInt32());
                Assert.Equal(sampleRows, assigned + sample.GetProperty("noise_rows").GetInt32());
                Assert.InRange(sample.GetProperty("noise_fraction").GetDouble(), 0, 1);
                var silhouette = sample.GetProperty("silhouette_excluding_noise");
                if (silhouette.ValueKind != JsonValueKind.Null)
                {
                    Assert.True(double.IsFinite(silhouette.GetDouble()));
                    Assert.InRange(silhouette.GetDouble(), -1, 1);
                }
                var artifact = Artifact(models, algorithm);
                using var privateParameters = JsonDocument.Parse(File.ReadAllText(artifact));
                Assert.Equal("private_transductive_training_sample_artifact_not_a_document_label_predictor",
                    privateParameters.RootElement.GetProperty("status").GetString());
                Assert.Equal(sampleRows, privateParameters.RootElement.GetProperty("points").GetArrayLength());
            }

            // Reports contain aggregate evidence; fixture passages and machine paths stay private.
            Assert.DoesNotContain(directory, reportText, StringComparison.OrdinalIgnoreCase);
            Assert.All(rows, row => Assert.DoesNotContain(row.Text, reportText, StringComparison.Ordinal));
            Assert.Throws<ArgumentException>(() => BookExploration.Run(input, reportPath, models, seed: 42, clusters: 2, rank: 8));
        }
        finally
        {
            // Validate the resolved destination before recursively deleting this test's own files.
            RequireOwnedTemporaryDirectory(directory, temporaryParent);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static string Artifact(string models, JsonElement algorithm)
    {
        var name = algorithm.GetProperty("model_file").GetString()!;
        Assert.False(Path.IsPathRooted(name));
        Assert.Equal(name, Path.GetFileName(name));
        var path = Path.Combine(models, name);
        Assert.True(File.Exists(path));
        Assert.Equal(CorpusFiles.FileHash(path), algorithm.GetProperty("model_sha256").GetString());
        return path;
    }

    private static void AssertFiniteNonnegative(JsonElement value)
    {
        var number = value.GetDouble();
        Assert.True(double.IsFinite(number));
        Assert.True(number >= 0);
    }

    private static void RequireOwnedTemporaryDirectory(string directory, string temporaryParent)
    {
        var resolved = Path.GetFullPath(directory);
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(resolved), temporaryParent)
            || !Path.GetFileName(resolved).StartsWith("tym-book-algorithm-contract-", StringComparison.Ordinal))
            throw new InvalidOperationException("Test cleanup must remain inside its explicitly named temporary directory.");
    }

    private static BookRow[] AuthoredPassages()
    {
        string[] names = ["Aria", "Boris", "Cora", "Diya", "Elias", "Farah"];
        string[] scenes =
        [
            "checked the small gate beside the greenhouse at dusk. A blue ribbon rested on the wooden bench while rain tapped the glass.",
            "followed the garden path toward a crooked apple tree. The evening breeze carried the scent of mint past a quiet fountain.",
            "arranged clay pots on the sunny balcony. A folded map and a silver watering can lay near the last yellow flower.",
            "opened the shed after breakfast and counted the seed packets. Somewhere beyond the hedge a bicycle bell interrupted the birds.",
            "watched a fishing boat cross the harbor at sunrise. A gull circled above the lighthouse while ropes creaked against the dock.",
            "carried an empty basket through the seaside market. Sailors discussed a distant island beside a stack of painted wooden crates.",
            "waited beside the ferry ticket booth during a sudden shower. The tide rose slowly beneath the stone steps and an orange buoy.",
            "climbed the coastal lookout before the next boat arrived. Beyond the bay, clouds gathered around the dark outline of a mountain."
        ];
        return names.SelectMany((name, document) => scenes.Select((scene, passage) => new BookRow(
            $"authored:{document}:{passage}", name + " " + scene, "en", "unlabeled_user_provided",
            "original-authored-software-fixtures", "authored-work-" + document, "chapter-1"))).ToArray();
    }
}
