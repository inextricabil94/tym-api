using System.Diagnostics;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Tym.Benchmark;
using Tym.Corpus.Data;
using Xunit;

namespace Tym.Research.Tests;

/// <summary>
/// Ten integration cases exercise the five real book algorithms in each supplied language.
/// The collection fixture trains fresh models once per language; it never substitutes an
/// old benchmark report for a run. Passing assertions describe software and provenance,
/// not semantic accuracy on the unannotated books.
/// </summary>
[Collection(PrivateBookWorkflowCollection.Name)]
public sealed class PrivateBookWorkflowIntegrationTests(PrivateBookWorkflowFixture fixture)
{
    [PrivateBookWorkflowTheory]
    [InlineData("ro", "mlnet_kmeans")]
    [InlineData("en", "mlnet_kmeans")]
    [InlineData("ro", "mlnet_pca_kmeans")]
    [InlineData("en", "mlnet_pca_kmeans")]
    [InlineData("ro", "csharp_average_linkage_hierarchical")]
    [InlineData("en", "csharp_average_linkage_hierarchical")]
    [InlineData("ro", "csharp_dbscan")]
    [InlineData("en", "csharp_dbscan")]
    [InlineData("ro", "csharp_torchsharp_autoencoder")]
    [InlineData("en", "csharp_torchsharp_autoencoder")]
    public void Supplied_book_workflow_fits_fresh_and_preserves_its_scientific_contract(string language, string algorithm)
    {
        var run = fixture.GetRun(language);
        var root = run.Report;
        var measurement = root.GetProperty("algorithms").EnumerateArray()
            .Single(value => value.GetProperty("algorithm").GetString() == algorithm);
        Assert.Equal(JsonValueKind.Null, measurement.GetProperty("semantic_accuracy").ValueKind);
        Assert.Equal("unlabeled_user_provided", run.SourceType);
        Assert.Equal(run.InputHash, root.GetProperty("input_sha256").GetString());
        Assert.Equal(run.SplitHash, root.GetProperty("split_assignment_sha256").GetString());
        Assert.Equal(32, root.GetProperty("effective_pca_rank").GetInt32());
        AssertSpace(measurement.GetProperty("space"));

        var checks = new List<string>
        {
            "The full supplied prepared corpus was read without labels and its SHA256 remained unchanged.",
            "Seed-42 duplicate-aware split was independently reproduced; normalized duplicate passages do not cross the holdout.",
            "The report records semantic accuracy as unavailable for unlabeled book passages.",
            "Memory values are finite shared-process snapshots, not isolated model-memory measurements."
        };
        object? probe = null;
        string? artifactHash = null;
        long? artifactBytes = null;
        var dimensionality = measurement.GetProperty("feature_dimensions").GetInt32();
        Assert.True(dimensionality > 0);
        if (algorithm is "mlnet_kmeans" or "mlnet_pca_kmeans")
        {
            var artifact = RequireArtifact(run.ModelsDirectory, measurement);
            artifactHash = CorpusFiles.FileHash(artifact);
            artifactBytes = new FileInfo(artifact).Length;
            Assert.Equal(artifactBytes, measurement.GetProperty("space").GetProperty("artifact_bytes").GetInt64());
            using var manifest = JsonDocument.Parse(File.ReadAllText(artifact + ".manifest.json"));
            var provenance = manifest.RootElement;
            Assert.Equal(algorithm, provenance.GetProperty("algorithm").GetString());
            Assert.Equal(language, provenance.GetProperty("language").GetString());
            Assert.Equal("unlabeled_user_provided", provenance.GetProperty("source_type").GetString());
            Assert.Equal("exploratory_unlabeled_book_clustering", provenance.GetProperty("task").GetString());
            Assert.Equal(run.InputHash, provenance.GetProperty("input_sha256").GetString());
            Assert.Equal(artifactHash, provenance.GetProperty("model_sha256").GetString());
            Assert.Equal(run.TrainingRows, provenance.GetProperty("fitted_rows").GetInt32());
            Assert.Equal(run.HeldoutRows, provenance.GetProperty("heldout_rows").GetInt32());
            Assert.Equal(root.GetProperty("split_kind").GetString(), provenance.GetProperty("split").GetString());
            Assert.Equal(42, provenance.GetProperty("seed").GetInt32());
            Assert.Equal(8, provenance.GetProperty("clusters").GetInt32());
            Assert.Equal(dimensionality, provenance.GetProperty("features").GetInt32());
            Assert.Equal(8, measurement.GetProperty("clusters").GetInt32());
            Assert.Equal(100, measurement.GetProperty("maximum_iterations").GetInt32());
            Assert.Equal(1, measurement.GetProperty("threads").GetInt32());
            AssertClusterMetrics(measurement.GetProperty("training_metrics"), run.TrainingRows);
            AssertClusterMetrics(measurement.GetProperty("heldout_metrics"), run.HeldoutRows);
            AssertFiniteNonnegative(measurement.GetProperty("fit_seconds_including_shared_featurizer"));
            AssertFiniteNonnegative(measurement.GetProperty("assessment_seconds"));
            Assert.Equal(run.TotalRows, measurement.GetProperty("assessment_rows").GetInt32());
            Assert.Equal(algorithm == "mlnet_pca_kmeans" ? 32 : run.TextFeatureDimensions, dimensionality);

            // A newly reloaded ML.NET pipeline must preserve the separately fitted training
            // vocabulary. The probe is original authored prose, never a passage from a book.
            var context = new MLContext(seed: 42);
            var model = context.Model.Load(artifact, out _);
            var input = context.Data.LoadFromEnumerable(new[] { new BookExploration.BookInput { Text = run.AuthoredQuery } });
            var first = context.Data.CreateEnumerable<ReloadedClusterRow>(model.Transform(input), false).Single();
            var secondModel = context.Model.Load(artifact, out _);
            var second = context.Data.CreateEnumerable<ReloadedClusterRow>(secondModel.Transform(input), false).Single();
            Assert.InRange(first.PredictedLabel, 1U, 8U);
            Assert.Equal(8, first.Score.Length);
            Assert.All(first.Score, value => Assert.True(float.IsFinite(value) && value >= 0));
            Assert.Equal(first.PredictedLabel, second.PredictedLabel);
            Assert.Equal(first.Score, second.Score);
            Assert.Equal(dimensionality, first.Features.Length);
            Assert.All(first.Features, value => Assert.True(float.IsFinite(value)));
            Assert.Equal(run.TextFeatureDimensions, first.TextFeatures.Length);
            Assert.Equal(run.AuthoredTextFeatures.Length, first.TextFeatures.Length);
            for (var column = 0; column < first.TextFeatures.Length; column++)
                Assert.InRange(Math.Abs(first.TextFeatures[column] - run.AuthoredTextFeatures[column]), 0f, 0.000001f);
            probe = new { input_kind = "original_authored_query", predicted_cluster = first.PredictedLabel,
                cluster_inventory = 8, score_count = first.Score.Length, score = first.Score,
                features = dimensionality, training_text_features_match = true, reload_reproducible = true };
            checks.Add("Private saved model and manifest SHA256 match, including exact training/heldout provenance.");
            checks.Add("Reloaded model scores an original authored query into one of eight arbitrary lexical clusters with finite distances.");
            checks.Add("Query text features match an independently fitted training-only vocabulary; reloading preserves scores.");
        }
        else if (algorithm is "csharp_average_linkage_hierarchical" or "csharp_dbscan")
        {
            Assert.Equal(32, dimensionality);
            Assert.Equal(256, measurement.GetProperty("sample_limit").GetInt32());
            var sampleRows = Math.Min(256, run.TrainingRows);
            Assert.Equal(sampleRows, measurement.GetProperty("sample_rows").GetInt32());
            Assert.Equal(JsonValueKind.Null, measurement.GetProperty("heldout_metrics").ValueKind);
            Assert.Contains("no heldout input used", measurement.GetProperty("sampling").GetString()!);
            AssertFiniteNonnegative(measurement.GetProperty("fit_seconds_algorithm_only"));
            AssertFiniteNonnegative(measurement.GetProperty("assessment_seconds"));
            var artifact = RequireArtifact(run.ModelsDirectory, measurement);
            artifactHash = CorpusFiles.FileHash(artifact);
            artifactBytes = new FileInfo(artifact).Length;
            Assert.Equal(artifactBytes, measurement.GetProperty("space").GetProperty("artifact_bytes").GetInt64());
            using var saved = JsonDocument.Parse(File.ReadAllText(artifact));
            var savedRoot = saved.RootElement;
            Assert.Equal("private_transductive_training_sample_artifact_not_a_document_label_predictor", savedRoot.GetProperty("status").GetString());
            Assert.Equal("training_fitted_pca_l2_normalized", savedRoot.GetProperty("feature_space").GetString());
            Assert.Equal(run.SampleHash, savedRoot.GetProperty("sample_ids_sha256").GetString());
            Assert.Equal(sampleRows, savedRoot.GetProperty("points").GetArrayLength());
            var savedPoints = savedRoot.GetProperty("points").EnumerateArray().ToArray();
            var sampleVectors = savedPoints.Select(point =>
            {
                var features = point.GetProperty("features").EnumerateArray().Select(value => value.GetDouble()).ToArray();
                Assert.Equal(32, features.Length);
                Assert.All(features, value => Assert.True(double.IsFinite(value)));
                return features;
            }).ToArray();
            var assignments = savedPoints.Select(point => point.GetProperty("cluster").GetInt32()).ToArray();
            var metrics = measurement.GetProperty("sample_metrics");
            Assert.Equal(sampleRows, metrics.GetProperty("rows").GetInt32());
            var noise = assignments.Count(value => value == -1);
            Assert.Equal(noise, metrics.GetProperty("noise_rows").GetInt32());
            Assert.Equal(noise / (double)sampleRows, metrics.GetProperty("noise_fraction").GetDouble(), 12);
            Assert.InRange(metrics.GetProperty("noise_fraction").GetDouble(), 0, 1);
            var clusterCount = assignments.Where(value => value > 0).Distinct().Count();
            Assert.Equal(clusterCount, metrics.GetProperty("clusters").GetInt32());
            Assert.Equal(sampleRows - noise, metrics.GetProperty("cluster_counts").EnumerateArray().Sum(value => value.GetProperty("rows").GetInt32()));
            Assert.All(metrics.GetProperty("cluster_counts").EnumerateArray(), group =>
                Assert.Equal(assignments.Count(value => value == group.GetProperty("cluster").GetInt32()), group.GetProperty("rows").GetInt32()));
            var silhouette = metrics.GetProperty("silhouette_excluding_noise");
            if (silhouette.ValueKind != JsonValueKind.Null)
            {
                Assert.True(double.IsFinite(silhouette.GetDouble()));
                Assert.InRange(silhouette.GetDouble(), -1, 1);
            }
            if (algorithm == "csharp_average_linkage_hierarchical")
            {
                Assert.Equal(8, measurement.GetProperty("requested_clusters").GetInt32());
                Assert.Equal(8, clusterCount);
                Assert.Equal(0, noise);
                Assert.All(assignments, value => Assert.InRange(value, 1, 8));
            }
            else
            {
                Assert.Equal(0.45, measurement.GetProperty("epsilon").GetDouble());
                Assert.Equal(5, measurement.GetProperty("minimum_points_including_self").GetInt32());
                Assert.All(assignments, value => Assert.True(value == -1 || value > 0));
                // Border points can already belong to a previously expanded cluster, so
                // minPoints is a neighborhood rule, not a minimum final cluster size.
                foreach (var cluster in assignments.Where(value => value > 0).Distinct())
                    Assert.Contains(Enumerable.Range(0, sampleRows).Where(index => assignments[index] == cluster),
                        index => sampleVectors.Count(vector => Math.Sqrt(vector.Select((value, column) =>
                            Math.Pow(value - sampleVectors[index][column], 2)).Sum()) <= 0.45) >= 5);
            }
            probe = new { input_kind = "deterministic_training_only_PCA_sample", sample_rows = sampleRows,
                features = 32, clusters = clusterCount, noise_rows = noise, sample_selection_sha256_match = true };
            checks.Add("All at-most-256 private sample vectors have 32 finite PCA coordinates; sample selection SHA256 independently matches training rows.");
            checks.Add("Artifact assignments agree with aggregate cluster/noise counts and fixed algorithm settings.");
            checks.Add("Sample silhouette/noise describe transductive training geometry; no heldout accuracy is asserted.");
        }
        else
        {
            Assert.Equal("csharp_torchsharp_autoencoder", algorithm);
            Assert.Equal(32, dimensionality);
            Assert.Equal(16, measurement.GetProperty("bottleneck_dimensions").GetInt32());
            Assert.Equal(6320, measurement.GetProperty("model_parameters").GetInt64());
            Assert.Equal(6320, measurement.GetProperty("space").GetProperty("neural_parameter_count").GetInt64());
            Assert.Equal(25280, measurement.GetProperty("space").GetProperty("float32_parameter_bytes").GetInt64());
            Assert.Equal(JsonValueKind.Null, measurement.GetProperty("space").GetProperty("artifact_bytes").ValueKind);
            Assert.False(measurement.GetProperty("model_exported").GetBoolean());
            Assert.Equal(5, measurement.GetProperty("epochs").GetInt32());
            Assert.Equal(run.TrainingRows, measurement.GetProperty("training_rows").GetInt32());
            Assert.Equal(run.HeldoutRows, measurement.GetProperty("heldout_rows").GetInt32());
            foreach (var metric in new[] { "last_epoch_training_mean_squared_error", "heldout_reconstruction_mean_squared_error",
                "heldout_training_mean_reconstruction_mean_squared_error", "heldout_zero_reconstruction_mean_squared_error",
                "fit_seconds_algorithm_only", "prediction_seconds_algorithm_only" })
                AssertFiniteNonnegative(measurement.GetProperty(metric));
            Assert.True(measurement.GetProperty("sampled_peak_process_working_set_bytes").GetInt64() > 0);
            var metadata = measurement.GetProperty("metadata");
            Assert.False(metadata.GetProperty("validation_used_for_fit").GetBoolean());
            Assert.False(metadata.GetProperty("early_stopping").GetBoolean());
            Assert.False(metadata.GetProperty("pretrained_weights").GetBoolean());
            Assert.False(metadata.GetProperty("model_exported").GetBoolean());
            Assert.Equal(run.TrainingRows, metadata.GetProperty("training_rows").GetInt32());
            Assert.Equal(run.HeldoutRows, metadata.GetProperty("validation_rows").GetInt32());
            Assert.Equal(5, metadata.GetProperty("epoch_training_losses").GetArrayLength());
            Assert.All(metadata.GetProperty("epoch_training_losses").EnumerateArray(), AssertFiniteNonnegative);
            Assert.Contains("training rows only", metadata.GetProperty("upstream_transform_contract").GetString()!);
            Assert.False(measurement.TryGetProperty("encoded_validation", out _));
            probe = new { input_kind = "heldout_numeric_PCA_vectors", input_dimensions = 32, bottleneck_dimensions = 16,
                parameters = 6320, model_exported = false, heldout_rows = run.HeldoutRows };
            checks.Add("Scratch TorchSharp model fits five epochs using training-only PCA vectors with 32 input and 16 bottleneck dimensions.");
            checks.Add("Heldout reconstruction and constant-zero/training-mean baseline MSE are finite; no learned quality threshold or prose decoding is claimed.");
            checks.Add("The 6,320 parameters are measured; model weights and heldout encodings are not exported.");
        }

        // Evidence is written only after every assertion succeeds. It contains aggregate
        // counts, hashes, metrics and an authored-query result; raw corpus data stay private.
        fixture.WriteEvidence(language, algorithm, new
        {
            schema_version = 1, suite = "book_experiments", workflow_id = $"book_{language}_{algorithm}",
            language, algorithm, status = "passed", generated_utc = DateTimeOffset.UtcNow,
            fresh_training_run = true, seed = 42,
            source_type = run.SourceType, input_sha256 = run.InputHash, report_sha256 = run.ReportHash,
            total_rows = run.TotalRows, documents = root.GetProperty("documents").GetInt32(),
            training_rows = run.TrainingRows, heldout_rows = run.HeldoutRows,
            training_documents = root.GetProperty("training_documents").GetInt32(),
            heldout_documents = root.GetProperty("heldout_documents").GetInt32(),
            split_kind = root.GetProperty("split_kind").GetString(), split_assignment_sha256 = run.SplitHash,
            training_only_representation_verified = true, semantic_accuracy = (double?)null,
            artifact_sha256 = artifactHash, artifact_bytes = artifactBytes, probe,
            measurement = measurement.Clone(), assertions = checks,
            integration_suite_language_seconds = run.LanguageRunSeconds,
            timing_scope = "Fresh integration run: one shared five-algorithm fit per language plus independent split/vocabulary verification; not a controlled performance benchmark.",
            accuracy_scope = "Books are unlabeled. These assertions test integration, traceability and finite geometry/reconstruction, not semantic prediction correctness."
        });
    }

    private static string RequireArtifact(string directory, JsonElement measurement)
    {
        var filename = measurement.GetProperty("model_file").GetString()!;
        Assert.False(Path.IsPathRooted(filename));
        Assert.Equal(filename, Path.GetFileName(filename));
        var path = Path.Combine(directory, filename);
        Assert.True(File.Exists(path));
        Assert.Equal(measurement.GetProperty("model_sha256").GetString(), CorpusFiles.FileHash(path));
        return path;
    }

    private static void AssertClusterMetrics(JsonElement metrics, int expectedRows)
    {
        Assert.Equal(expectedRows, metrics.GetProperty("rows").GetInt32());
        AssertFiniteNonnegative(metrics.GetProperty("average_squared_centroid_distance"));
        var daviesBouldin = metrics.GetProperty("davies_bouldin_index");
        if (daviesBouldin.ValueKind == JsonValueKind.Null)
            Assert.Equal("undefined_degenerate_clusters", metrics.GetProperty("davies_bouldin_status").GetString());
        else AssertFiniteNonnegative(daviesBouldin);
        var counts = metrics.GetProperty("cluster_counts").EnumerateArray().ToArray();
        Assert.Equal(expectedRows, counts.Sum(value => value.GetProperty("rows").GetInt32()));
        Assert.All(counts, value =>
        {
            Assert.InRange(value.GetProperty("cluster").GetUInt32(), 1U, 8U);
            Assert.True(value.GetProperty("rows").GetInt32() > 0);
        });
    }

    private static void AssertSpace(JsonElement space)
    {
        AssertFiniteNonnegative(space.GetProperty("process_working_set_mb"));
        AssertFiniteNonnegative(space.GetProperty("process_lifetime_peak_working_set_mb"));
        Assert.True(space.GetProperty("process_working_set_mb").GetDouble() > 0);
        Assert.True(space.GetProperty("process_lifetime_peak_working_set_mb").GetDouble() >= space.GetProperty("process_working_set_mb").GetDouble());
        Assert.Contains("Shared process snapshot", space.GetProperty("scope").GetString()!);
    }

    private static void AssertFiniteNonnegative(JsonElement value)
    {
        Assert.True(double.IsFinite(value.GetDouble()));
        Assert.True(value.GetDouble() >= 0);
    }

    public sealed class ReloadedClusterRow
    {
        public uint PredictedLabel { get; set; }
        public float[] Score { get; set; } = [];
        public float[] Features { get; set; } = [];
        public float[] TextFeatures { get; set; } = [];
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PrivateBookWorkflowCollection : ICollectionFixture<PrivateBookWorkflowFixture>
{
    public const string Name = "Fresh private book workflow integration";
}

/// <summary>Private corpus tests are explicit opt-in and never silently replace missing data with fixtures.</summary>
public sealed class PrivateBookWorkflowTheoryAttribute : TheoryAttribute
{
    public PrivateBookWorkflowTheoryAttribute()
    {
        if (new[] { "TYM_PRIVATE_BOOK_RO_JSONL", "TYM_PRIVATE_BOOK_EN_JSONL", "TYM_WORKFLOW_EVIDENCE_DIR" }
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Set TYM_PRIVATE_BOOK_RO_JSONL, TYM_PRIVATE_BOOK_EN_JSONL and a new TYM_WORKFLOW_EVIDENCE_DIR to run full private book workflows.";
    }
}

/// <summary>
/// Owns new temporary weights and keeps each language's five fresh algorithm fits together.
/// Only aggregate evidence persists. Cleanup is constrained to this fixture's resolved temp directory.
/// </summary>
public sealed class PrivateBookWorkflowFixture : IDisposable
{
    private readonly string temporaryParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private readonly string directory;
    private readonly Dictionary<string, Lazy<BookWorkflowRun>> runs;

    public PrivateBookWorkflowFixture()
    {
        directory = Path.GetFullPath(Path.Combine(temporaryParent, "tym-private-book-workflows-" + Guid.NewGuid().ToString("N")));
        RequireOwnedTemporaryDirectory();
        runs = new[] { "ro", "en" }.ToDictionary(language => language,
            language => new Lazy<BookWorkflowRun>(() => FitAndVerify(language)), StringComparer.Ordinal);
    }

    public BookWorkflowRun GetRun(string language) => runs.TryGetValue(language, out var run)
        ? run.Value : throw new ArgumentException("Only ro/en book workflows are supported.", nameof(language));

    public void WriteEvidence(string language, string algorithm, object evidence)
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("TYM_WORKFLOW_EVIDENCE_DIR")
            ?? throw new InvalidOperationException("The private workflow evidence directory is required."));
        var destination = Path.Combine(root, "books", $"book_{language}_{algorithm}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, evidence, new JsonSerializerOptions(CorpusFiles.Json) { WriteIndented = true });
    }

    private BookWorkflowRun FitAndVerify(string language)
    {
        var watch = Stopwatch.StartNew();
        var input = Path.GetFullPath(Environment.GetEnvironmentVariable(language == "ro" ? "TYM_PRIVATE_BOOK_RO_JSONL" : "TYM_PRIVATE_BOOK_EN_JSONL")
            ?? throw new InvalidOperationException("The supplied prepared private book input is required."));
        var inputHash = CorpusFiles.FileHash(input);
        var expectedHash = language == "ro" ? "9722782f589deca0fda5985f95fd91891aa11e65df0c4cfd46816e7505332d1b"
            : "4e2538420a491d9f5913cace9efa29eede899d5a5c150580192664cef8d50c51";
        Assert.Equal(expectedHash, inputHash);
        var rows = File.ReadLines(input).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line =>
        {
            using var json = JsonDocument.Parse(line);
            Assert.False(json.RootElement.TryGetProperty("label", out _));
            Assert.False(json.RootElement.TryGetProperty("task", out _));
            return JsonSerializer.Deserialize<BookRow>(line, CorpusFiles.Json)!;
        }).ToArray();
        Assert.Equal(language == "ro" ? 5213 : 446, rows.Length);
        Assert.All(rows, row =>
        {
            Assert.Equal(language, row.Language);
            Assert.Equal("unlabeled_user_provided", row.SourceType);
            Assert.False(string.IsNullOrWhiteSpace(row.Text));
        });
        Assert.Equal(rows.Length, rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count());
        var split = ReproduceSplit(rows);
        var training = rows.Where((_, index) => !split.Heldout[index]).ToArray();
        var heldout = rows.Where((_, index) => split.Heldout[index]).ToArray();
        Assert.Equal(language == "ro" ? 4037 : 356, training.Length);
        Assert.Equal(language == "ro" ? 1176 : 90, heldout.Length);
        Assert.Equal(language == "ro" ? 14 : 1, rows.Select(DocumentKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(training.Select(PassageKey).Intersect(heldout.Select(PassageKey), StringComparer.Ordinal));
        if (language == "ro") Assert.Empty(training.Select(DocumentKey).Intersect(heldout.Select(DocumentKey), StringComparer.Ordinal));
        else Assert.Single(training.Select(DocumentKey).Intersect(heldout.Select(DocumentKey), StringComparer.Ordinal));
        var splitHash = CorpusFiles.Hash(string.Join('\n', rows.Select((row, index) => row.Id + "|" + split.Heldout[index])));
        var sampleHash = CorpusFiles.Hash(string.Join('\n', training.OrderBy(row => CorpusFiles.Hash("42|sample|" + row.Id), StringComparer.Ordinal)
            .Take(256).Select(row => row.Id)));
        var languageDirectory = Path.Combine(directory, language);
        var reportPath = Path.Combine(languageDirectory, "aggregate.json");
        var models = Path.Combine(languageDirectory, "private-models");
        Directory.CreateDirectory(languageDirectory);
        BookExploration.Run(input, reportPath, models, seed: 42, clusters: 8, rank: 32);
        Assert.Equal(inputHash, CorpusFiles.FileHash(input));
        using var reportDocument = JsonDocument.Parse(File.ReadAllText(reportPath));
        var report = reportDocument.RootElement.Clone();
        Assert.Equal("exploratory_unlabeled_books", report.GetProperty("status").GetString());
        Assert.Equal(language, report.GetProperty("language").GetString());
        Assert.Equal(42, report.GetProperty("seed").GetInt32());
        Assert.Equal(rows.Length, report.GetProperty("total_rows").GetInt32());
        Assert.Equal(training.Length, report.GetProperty("training_rows").GetInt32());
        Assert.Equal(heldout.Length, report.GetProperty("heldout_rows").GetInt32());
        Assert.Equal(language == "ro" ? "complete_document_component_holdout" : "within_source_work_normalized_passage_holdout",
            report.GetProperty("split_kind").GetString());
        Assert.Equal(language == "ro" ? 14 : 1, report.GetProperty("independent_document_components").GetInt32());
        Assert.Equal(language == "ro" ? 12 : 1, report.GetProperty("training_documents").GetInt32());
        Assert.Equal(language == "ro" ? 2 : 1, report.GetProperty("heldout_documents").GetInt32());
        Assert.Equal(splitHash, report.GetProperty("split_assignment_sha256").GetString());
        Assert.Equal(5, report.GetProperty("algorithms").GetArrayLength());
        Assert.False(File.ReadAllText(reportPath).Contains(directory, StringComparison.OrdinalIgnoreCase));

        // Independently fit only the reproduced training rows. Comparing a fresh model's
        // authored-query text vector catches accidental full-corpus vocabulary fitting.
        var context = new MLContext(seed: 42);
        var trainView = context.Data.LoadFromEnumerable(training.Select(row => new BookExploration.BookInput { Text = row.Text }));
        var featurizer = context.Transforms.Text.FeaturizeText("RawTextFeatures", nameof(BookExploration.BookInput.Text))
            .Append(context.Transforms.NormalizeLpNorm("TextFeatures", "RawTextFeatures")).Fit(trainView);
        var query = language == "ro" ? "Elena a ajuns la grădină dimineața. Seara, barca a trecut pe lângă far."
            : "Elena reached the garden in the morning. In the evening, the boat passed the lighthouse.";
        var queryView = context.Data.LoadFromEnumerable(new[] { new BookExploration.BookInput { Text = query } });
        var transformed = featurizer.Transform(queryView);
        var dimensions = ((VectorDataViewType)transformed.Schema["TextFeatures"].Type).Size;
        var expectedFeatures = context.Data.CreateEnumerable<TextFeatureRow>(transformed, false).Single().TextFeatures;
        Assert.Equal(dimensions, expectedFeatures.Length);
        return new(report, models, inputHash, CorpusFiles.FileHash(reportPath), splitHash, sampleHash,
            rows.Length, training.Length, heldout.Length, dimensions, query, expectedFeatures,
            "unlabeled_user_provided", watch.Elapsed.TotalSeconds);
    }

    private static (bool[] Heldout, int Components) ReproduceSplit(BookRow[] rows)
    {
        var documentKeys = rows.Select(DocumentKey).Distinct(StringComparer.Ordinal).ToArray();
        var parents = documentKeys.ToDictionary(key => key, key => key, StringComparer.Ordinal);
        string Find(string key) => parents[key] == key ? key : parents[key] = Find(parents[key]);
        foreach (var duplicate in rows.GroupBy(PassageKey, StringComparer.Ordinal))
        {
            var linked = duplicate.Select(DocumentKey).Distinct(StringComparer.Ordinal).Select(Find).Order(StringComparer.Ordinal).ToArray();
            foreach (var key in linked.Skip(1)) parents[Find(key)] = Find(linked[0]);
        }
        var componentCount = documentKeys.Select(Find).Distinct(StringComparer.Ordinal).Count();
        var keys = rows.Select(row => componentCount > 1 ? Find(DocumentKey(row)) : PassageKey(row)).ToArray();
        var groups = keys.GroupBy(key => key, StringComparer.Ordinal)
            .OrderBy(group => CorpusFiles.Hash("42|split|" + group.Key), StringComparer.Ordinal).ToArray();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var selectedRows = 0;
        foreach (var group in groups.Take(groups.Length - 1))
        {
            selected.Add(group.Key);
            selectedRows += group.Count();
            if (selectedRows >= Math.Ceiling(rows.Length * 0.2)) break;
        }
        return (keys.Select(selected.Contains).ToArray(), componentCount);
    }

    private static string DocumentKey(BookRow row) => row.SourceGroup + "|" + row.DocumentId;
    private static string PassageKey(BookRow row) => CorpusFiles.Hash(CorpusFiles.NormalizeSpaces(row.Text));

    public void Dispose()
    {
        RequireOwnedTemporaryDirectory();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private void RequireOwnedTemporaryDirectory()
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(Path.GetFullPath(directory)), temporaryParent)
            || !Path.GetFileName(directory).StartsWith("tym-private-book-workflows-", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture cleanup must remain inside its owned temporary directory.");
    }

    public sealed class TextFeatureRow { public float[] TextFeatures { get; set; } = []; }
}

public sealed record BookWorkflowRun(JsonElement Report, string ModelsDirectory, string InputHash,
    string ReportHash, string SplitHash, string SampleHash, int TotalRows, int TrainingRows, int HeldoutRows,
    int TextFeatureDimensions, string AuthoredQuery, float[] AuthoredTextFeatures, string SourceType, double LanguageRunSeconds);
