using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tym.Corpus.Api;
using Tym.Corpus.Core;
using Xunit;

namespace Tym.Corpus.Api.Tests;

/// <summary>Original synthetic weights verify artifact plumbing; copied demo classifiers do not measure any NLP task.</summary>
public sealed class ModelArtifactReadinessTests(SyntheticModelFixture fixture) : IClassFixture<SyntheticModelFixture>
{
    [Fact]
    public void Valid_digest_and_catalog_identity_are_verified_before_scoring()
    {
        using var directory = CopyFixture();
        RewriteManifest(directory.Path, manifest => manifest["model_sha256"] = Digest(Weights(directory.Path)).ToUpperInvariant());
        var service = new CorpusModelService(directory.Path);

        var model = Entry(service);
        Assert.True(model.Available);
        Assert.True(model.Installed);
        Assert.True(model.Loaded);
        Assert.True(model.Scorable);
        Assert.Equal("verified", model.IntegrityStatus);
        Assert.Equal("verified", model.IdentityStatus);
        Assert.Equal("ready_verified", model.ReadinessStatus);
        Assert.Equal("verified", service.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the harbor.").IntegrityStatus);
    }

    [Theory]
    [InlineData("task", "timebank_tlink")]
    [InlineData("language", "ro")]
    public void Declared_wrong_task_or_language_refuses_inference(string field, string value)
    {
        using var directory = CopyFixture();
        RewriteManifest(directory.Path, manifest => manifest[field] = value);
        var service = new CorpusModelService(directory.Path);

        var model = Entry(service);
        Assert.True(model.Installed);
        Assert.False(model.Available);
        Assert.False(model.MetadataAvailable);
        Assert.Equal("mismatch", model.IdentityStatus);
        Assert.Equal("identity_mismatch", model.ReadinessStatus);
        Assert.Throws<ModelUnavailableException>(() => service.Predict(SyntheticModelFixture.ModelId, "Original text."));
    }

    [Fact]
    public void Digest_mismatch_refuses_even_otherwise_loadable_weights()
    {
        using var directory = CopyFixture();
        RewriteManifest(directory.Path, manifest => manifest["model_sha256"] = new string('0', 64));
        var service = new CorpusModelService(directory.Path);

        var model = Entry(service);
        Assert.False(model.Available);
        Assert.False(model.Loaded);
        Assert.False(model.MetadataAvailable);
        Assert.Equal("mismatch", model.IntegrityStatus);
        Assert.Equal("integrity_mismatch", model.ReadinessStatus);
        Assert.Throws<ModelUnavailableException>(() => service.Predict(SyntheticModelFixture.ModelId, "Original text."));
    }

    [Fact]
    public void Malformed_declared_digest_is_rejected_rather_than_ignored()
    {
        using var directory = CopyFixture();
        RewriteManifest(directory.Path, manifest => manifest["model_sha256"] = "not-a-sha256");
        var model = Entry(new CorpusModelService(directory.Path));

        Assert.False(model.Available);
        Assert.Equal("invalid", model.IntegrityStatus);
        Assert.Equal("integrity_invalid", model.ReadinessStatus);
    }

    [Fact]
    public void Invalid_training_metadata_retains_verified_weights_without_fabricated_provenance()
    {
        using var directory = CopyFixture();
        RewriteManifest(directory.Path, manifest =>
        {
            manifest["training_rows"] = -1;
            manifest["model_sha256"] = Digest(Weights(directory.Path));
        });
        var service = new CorpusModelService(directory.Path);
        var prediction = service.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the harbor.");

        Assert.False(prediction.MetadataAvailable);
        Assert.Equal("invalid", prediction.MetadataStatus);
        Assert.Null(prediction.TrainingRows);
        Assert.Empty(prediction.SourceTypes);
        Assert.Contains("Training provenance unavailable", prediction.Provenance);
        Assert.Equal("verified", prediction.IntegrityStatus);
    }

    [Fact]
    public void Corrupt_weights_are_installed_but_not_available_or_ready()
    {
        using var directory = CopyFixture();
        File.WriteAllText(Weights(directory.Path), "Corrupt original test artifact.");
        var service = new CorpusModelService(directory.Path);

        var model = Entry(service);
        Assert.True(model.Installed);
        Assert.False(model.Loaded);
        Assert.False(model.Scorable);
        Assert.False(model.Available);
        Assert.Equal("load_failed", model.ReadinessStatus);
        Assert.False(service.Ready().Ready);
    }

    [Fact]
    public void Partial_catalog_readiness_reports_missing_models_and_unverified_hashes()
    {
        var service = new CorpusModelService(fixture.DirectoryPath);
        var readiness = service.Ready();

        Assert.False(readiness.Ready);
        Assert.False(readiness.AllIntegrityVerified);
        Assert.Equal(16, readiness.TotalModels);
        Assert.Equal(1, readiness.InstalledModels);
        Assert.Equal(1, readiness.LoadedModels);
        Assert.Equal(1, readiness.ScorableModels);
        Assert.Equal(1, readiness.ReadyModels);
        Assert.Equal("ready_unverified", Assert.Single(readiness.Models, model => model.Ready).ReadinessStatus);
    }

    [Fact]
    public void All_expected_original_demo_bundles_can_be_operationally_ready_without_accuracy_claims()
    {
        using var directory = new GuardedTemporaryDirectory();
        foreach (var definition in ModelCatalog.All)
        {
            var path = Path.Combine(directory.Path, definition.ModelId + "_sdca.zip");
            File.Copy(fixture.ModelPath, path);
            var manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
            manifest["task"] = definition.Task;
            manifest["language"] = definition.Language;
            manifest["model_sha256"] = Digest(path);
            File.WriteAllText(path + ".manifest.json", manifest.ToJsonString());
        }

        var readiness = new CorpusModelService(directory.Path).Ready();
        Assert.True(readiness.Ready);
        Assert.True(readiness.AllIntegrityVerified);
        Assert.Equal(16, readiness.ReadyModels);
        Assert.All(readiness.Models, model => Assert.Equal("ready_verified", model.ReadinessStatus));
    }

    [Fact]
    public void Successful_snapshot_reuses_loaded_weights_and_manifest_until_process_replacement()
    {
        using var directory = CopyFixture();
        var service = new CorpusModelService(directory.Path);
        var initial = service.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the harbor.");
        File.WriteAllText(Weights(directory.Path), "A later invalid disk edit.");
        File.WriteAllText(Weights(directory.Path) + ".manifest.json", "{ broken metadata }");

        var cached = service.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the harbor.");
        Assert.Equal(initial, cached);
        Assert.True(Entry(service).Available);
        Assert.False(Entry(new CorpusModelService(directory.Path)).Available);
    }

    [Fact]
    public void Failed_bundle_is_retried_after_bounded_delay_using_a_new_snapshot()
    {
        using var directory = CopyFixture();
        File.WriteAllText(Weights(directory.Path), "Invalid original artifact.");
        var clock = new ManualClock();
        var service = new CorpusModelService(directory.Path, clock);
        Assert.False(Entry(service).Available);
        File.Copy(fixture.ModelPath, Weights(directory.Path), overwrite: true);
        Assert.False(Entry(service).Available);

        clock.Advance(CorpusModelService.FailedBundleRetryDelay);
        Assert.True(Entry(service).Available);
        Assert.Equal("FUTURE", service.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the harbor.").PredictedLabel);
    }

    [Fact]
    public async Task Concurrent_document_batches_do_not_share_prediction_engines()
    {
        var service = new CorpusModelService(fixture.DirectoryPath);
        var texts = new[]
        {
            "Yesterday we visited the old harbor.", "Now we visit the old harbor.", "Tomorrow we will visit the old harbor."
        };
        var batches = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => service.PredictMany(SyntheticModelFixture.ModelId, texts))));

        Assert.All(batches, batch => Assert.Equal(new[] { "PAST", "PRESENT", "FUTURE" }, batch.Select(row => row.PredictedLabel)));
    }

    [Fact]
    public void Structured_request_format_errors_do_not_invalidate_a_loaded_bundle()
    {
        using var directory = new GuardedTemporaryDirectory();
        const string id = "timebank_event_class_ro_structured";
        var path = Path.Combine(directory.Path, id + "_sdca.zip");
        File.Copy(fixture.ModelPath, path);
        var manifest = JsonNode.Parse(File.ReadAllText(fixture.ManifestPath))!.AsObject();
        manifest["task"] = "timebank_event_class";
        manifest["language"] = "ro";
        manifest["input_format"] = TaskInputParser.StructuredFormat;
        File.WriteAllText(path + ".manifest.json", manifest.ToJsonString());
        var service = new CorpusModelService(directory.Path);

        Assert.Throws<ArgumentException>(() => service.Predict(id, "Plain text without the task fields."));
        var model = Assert.Single(service.Catalog(), model => model.ModelId == id);
        Assert.True(model.Available);
        Assert.Equal(TaskInputParser.StructuredFormat, model.InputFormat);
        Assert.NotEmpty(service.Predict(id, "Context: Ana [TARGET]citește[/TARGET] acum.\nEvent: citește").PredictedLabel);
    }

    private GuardedTemporaryDirectory CopyFixture()
    {
        var directory = new GuardedTemporaryDirectory();
        File.Copy(fixture.ModelPath, Weights(directory.Path));
        File.Copy(fixture.ManifestPath, Weights(directory.Path) + ".manifest.json");
        return directory;
    }

    private static string Weights(string directory) => Path.Combine(directory, SyntheticModelFixture.ModelId + "_sdca.zip");
    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static ModelCatalogEntry Entry(CorpusModelService service) => Assert.Single(service.Catalog(), model => model.ModelId == SyntheticModelFixture.ModelId);
    private static void RewriteManifest(string directory, Action<JsonObject> edit)
    {
        var path = Weights(directory) + ".manifest.json";
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        edit(manifest);
        File.WriteAllText(path, manifest.ToJsonString());
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan interval) => _now += interval;
    }
}
