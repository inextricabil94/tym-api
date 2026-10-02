using Microsoft.AspNetCore.Http;
using Tym.Corpus.Api;
using Xunit;
using Xunit.Abstractions;

namespace Tym.Corpus.Api.Tests;

public sealed class CorpusModelServiceTests(
    SyntheticModelFixture fixture,
    ITestOutputHelper output) : IClassFixture<SyntheticModelFixture>
{
    [Theory]
    [InlineData("Yesterday we visited the old harbor.", "PAST")]
    [InlineData("Now we visit the old harbor.", "PRESENT")]
    [InlineData("Tomorrow we will visit the old harbor.", "FUTURE")]
    public void Saved_model_scores_original_unlabeled_free_text(string text, string expectedLabel)
    {
        var models = new CorpusModelService(fixture.DirectoryPath);

        var prediction = models.Predict(SyntheticModelFixture.ModelId, text);

        Assert.Equal(expectedLabel, prediction.PredictedLabel);
        Assert.Equal(SyntheticModelFixture.ModelId, prediction.ModelId);
        Assert.Equal("en", prediction.Language);
        Assert.Contains("synthetic_demo", prediction.SourceTypes);
        Assert.Contains("no corpus accuracy claim", prediction.ResearchLimit);
        output.WriteLine($"Synthetic component fixture | {text} => {prediction.PredictedLabel}");
    }

    [Fact]
    public async Task Concurrent_predictions_use_independent_engines()
    {
        var models = new CorpusModelService(fixture.DirectoryPath);

        var predictions = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(
            () => models.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the old harbor."))));

        Assert.All(predictions, result => Assert.Equal("FUTURE", result.PredictedLabel));
    }

    [Fact]
    public void Separately_loaded_services_return_identical_predictions()
    {
        var first = new CorpusModelService(fixture.DirectoryPath);
        var second = new CorpusModelService(fixture.DirectoryPath);

        Assert.Equal(
            first.Predict(SyntheticModelFixture.ModelId, "Yesterday we visited the old harbor.").PredictedLabel,
            second.Predict(SyntheticModelFixture.ModelId, "Yesterday we visited the old harbor.").PredictedLabel);
    }

    [Fact]
    public void Catalog_preserves_manifest_provenance_and_lists_all_sixteen_models()
    {
        var catalog = new CorpusModelService(fixture.DirectoryPath).Catalog();

        Assert.Equal(16, catalog.Count);
        var available = Assert.Single(catalog, model => model.Available);
        Assert.Equal(SyntheticModelFixture.ModelId, available.ModelId);
        Assert.True(available.MetadataAvailable);
        Assert.Equal("loaded", available.MetadataStatus);
        Assert.Equal(18, available.TrainingRows);
        Assert.Equal(1, available.DocumentCount);
        Assert.Equal(new[] { "PAST", "PRESENT", "FUTURE" }, available.Labels);
        Assert.Equal(new[] { "synthetic_demo" }, available.SourceTypes);
        Assert.Equal(new[] { "original-component-test-fixture" }, available.SourceGroups);
        Assert.Equal("SdcaMaximumEntropy", available.Trainer);
        Assert.Contains("reported by the supplied model manifest", available.Provenance);
        Assert.All(catalog, model => Assert.NotEmpty(model.ResearchLimit));
    }

    [Fact]
    public void Missing_configuration_is_visible_in_catalog_without_loading_any_model()
    {
        var service = new CorpusModelService(null);

        Assert.False(service.IsConfigured);
        Assert.All(service.Catalog(), model =>
        {
            Assert.False(model.Available);
            Assert.False(model.MetadataAvailable);
            Assert.Equal("missing", model.MetadataStatus);
            Assert.Null(model.TrainingRows);
            Assert.Empty(model.SourceTypes);
            Assert.Contains("unavailable", model.Provenance);
        });
    }

    [Fact]
    public void Missing_manifest_does_not_fabricate_training_provenance()
    {
        using var temporary = new GuardedTemporaryDirectory();
        File.Copy(fixture.ModelPath, Path.Combine(temporary.Path, SyntheticModelFixture.ModelId + "_sdca.zip"));
        var service = new CorpusModelService(temporary.Path);

        var prediction = service.Predict(SyntheticModelFixture.ModelId, "Tomorrow we will visit the old harbor.");

        Assert.Equal("FUTURE", prediction.PredictedLabel);
        Assert.False(prediction.MetadataAvailable);
        Assert.Equal("missing", prediction.MetadataStatus);
        Assert.Null(prediction.TrainingRows);
        Assert.Empty(prediction.SourceTypes);
        Assert.Contains("Training provenance unavailable", prediction.Provenance);
    }

    [Fact]
    public void Malformed_manifest_keeps_catalog_readable_and_marks_metadata_invalid()
    {
        using var temporary = new GuardedTemporaryDirectory();
        var target = Path.Combine(temporary.Path, SyntheticModelFixture.ModelId + "_sdca.zip");
        File.Copy(fixture.ModelPath, target);
        File.WriteAllText(target + ".manifest.json", "{ this is deliberately invalid JSON }");

        var model = Assert.Single(new CorpusModelService(temporary.Path).Catalog(), model => model.Available);

        Assert.True(model.Available);
        Assert.False(model.MetadataAvailable);
        Assert.Equal("invalid", model.MetadataStatus);
        Assert.Empty(model.Labels);
    }

    [Fact]
    public void Missing_weights_return_service_unavailable()
    {
        var result = PredictionEndpoint.Handle(
            new CorpusPredictionRequest(SyntheticModelFixture.ModelId, "Original free text."),
            new CorpusModelService(null));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var error = Assert.IsType<ApiError>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal("model_unavailable", error.Code);
    }

    [Fact]
    public void Invalid_weights_return_service_unavailable_without_exposing_paths()
    {
        using var temporary = new GuardedTemporaryDirectory();
        File.WriteAllText(Path.Combine(temporary.Path, SyntheticModelFixture.ModelId + "_sdca.zip"), "This is not an ML.NET model.");

        var result = PredictionEndpoint.Handle(
            new CorpusPredictionRequest(SyntheticModelFixture.ModelId, "Original free text."),
            new CorpusModelService(temporary.Path));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var error = Assert.IsType<ApiError>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.DoesNotContain(temporary.Path, error.Error);
    }

    [Fact]
    public void Prediction_endpoint_returns_typed_success_with_research_limit()
    {
        var result = PredictionEndpoint.Handle(
            new CorpusPredictionRequest(SyntheticModelFixture.ModelId, "Tomorrow we will visit the old harbor."),
            new CorpusModelService(fixture.DirectoryPath));

        Assert.Equal(StatusCodes.Status200OK, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var prediction = Assert.IsType<CorpusPredictionResponse>(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        Assert.Equal("FUTURE", prediction.PredictedLabel);
        Assert.NotEmpty(prediction.ResearchLimit);
    }
}
