using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Tym.Corpus.Api;
using Xunit;
using Xunit.Abstractions;

namespace Tym.Corpus.Api.Tests;

/// <summary>Original authored examples record repeatable behavior of private full-data structured weights.
/// Expected labels are observations, not independent human-gold annotations or an accuracy benchmark.</summary>
public sealed class StructuredCorpusPredictionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "structured-free-text.json")));
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.GetProperty("model_id").GetString()!, row.GetProperty("id").GetString()!,
                row.GetProperty("text").GetString()!, row.GetProperty("expected_label").GetString()!];
    }

    [PrivateStructuredTheory]
    [MemberData(nameof(Cases))]
    public void Structured_weights_return_the_recorded_free_text_label(string modelId, string id, string text, string expected)
    {
        var service = new CorpusModelService(Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR"));
        var prediction = service.Predict(modelId, text);
        Assert.Equal(expected, prediction.PredictedLabel);
        Assert.Equal("structured_target_v1", prediction.InputFormat);
        Assert.Equal("verified", prediction.IntegrityStatus);
        Assert.Equal("verified", prediction.IdentityStatus);
        output.WriteLine($"{modelId} | {id} => {prediction.PredictedLabel}");
    }

    [PrivateStructuredFact]
    public void Malformed_task_fields_return_400_and_do_not_invalidate_ready_weights()
    {
        var service = new CorpusModelService(Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR"));
        const string modelId = "timebank_tlink_ro_structured";
        var result = PredictionEndpoint.Handle(new(modelId, "Missing target and endpoint fields"), service);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        Assert.True(service.Catalog().Single(model => model.ModelId == modelId).Available);
    }
}

public sealed class PrivateStructuredTheoryAttribute : TheoryAttribute
{
    public PrivateStructuredTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")))
            Skip = "Set TYM_PRIVATE_MODEL_DIR to run regression tests against locally trained private weights.";
    }
}

public sealed class PrivateStructuredFactAttribute : FactAttribute
{
    public PrivateStructuredFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")))
            Skip = "Set TYM_PRIVATE_MODEL_DIR to run regression tests against locally trained private weights.";
    }
}
