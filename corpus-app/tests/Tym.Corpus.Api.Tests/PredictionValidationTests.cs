using Microsoft.AspNetCore.Http;
using Tym.Corpus.Api;
using Xunit;

namespace Tym.Corpus.Api.Tests;

public sealed class PredictionValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-model")]
    [InlineData("../segment_type_en")]
    [InlineData("SEGMENT_TYPE_EN")]
    public void Unknown_or_unsafe_model_ids_are_rejected(string? modelId)
    {
        var error = PredictionValidation.Validate(new CorpusPredictionRequest(modelId, "Some original text."));

        Assert.Equal("invalid_model_id", error?.Code);
        Assert.Throws<ArgumentException>(() => new CorpusModelService(null).Predict(modelId!, "Some original text."));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Blank_text_is_rejected(string? text)
    {
        var error = PredictionValidation.Validate(new CorpusPredictionRequest("segment_type_en", text));

        Assert.Equal("invalid_text", error?.Code);
    }

    [Fact]
    public void Text_length_boundary_is_enforced()
    {
        Assert.Null(PredictionValidation.Validate(new CorpusPredictionRequest("segment_type_en", new string('x', 50_000))));
        Assert.Equal("invalid_text", PredictionValidation.Validate(
            new CorpusPredictionRequest("segment_type_en", new string('x', 50_001)))?.Code);
    }

    [Fact]
    public void Null_request_body_is_rejected()
    {
        Assert.Equal("invalid_request", PredictionValidation.Validate(null)?.Code);
    }

    [Fact]
    public void Invalid_endpoint_request_returns_bad_request_before_loading_models()
    {
        var result = PredictionEndpoint.Handle(new CorpusPredictionRequest("../private-model", "Text"),
            new CorpusModelService(null));

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.Equal("invalid_model_id", Assert.IsType<ApiError>(
            Assert.IsAssignableFrom<IValueHttpResult>(result).Value).Code);
    }

    [Fact]
    public void Model_catalog_has_exact_existing_artifact_ids_and_training_format_hints()
    {
        Assert.Equal(new[]
        {
            "segment_type_en", "segment_type_ro", "temporal_relation_en", "temporal_relation_ro",
            "timebank_event_class_ro", "timebank_event_tense_ro", "timebank_timex_type_ro",
            "timebank_tlink_ro", "timebank_slink_ro", "timebank_alink_ro",
            "timebank_event_class_ro_structured", "timebank_event_tense_ro_structured", "timebank_timex_type_ro_structured",
            "timebank_tlink_ro_structured", "timebank_slink_ro_structured", "timebank_alink_ro_structured"
        }, ModelCatalog.All.Select(model => model.ModelId));
        Assert.Equal(16, ModelCatalog.All.Select(model => model.ModelId).Distinct().Count());
        Assert.All(ModelCatalog.All, model => Assert.NotEmpty(model.InputHint));
        Assert.Contains("[TARGET]", ModelCatalog.Get("timebank_event_class_ro").InputHint);
        Assert.Contains("From context:", ModelCatalog.Get("timebank_tlink_ro").InputHint);
    }
}
