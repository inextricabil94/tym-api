using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Tym.Modeling.Tests;

public sealed class CorpusModelPredictionTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEnumerable<object[]> PredictionCases()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        var inputFiles = Directory.GetFiles(dataDirectory, "*.jsonl").Order(StringComparer.Ordinal).ToArray();
        foreach (var inputFile in inputFiles)
        {
            var modelFile = Path.GetFileNameWithoutExtension(inputFile) + "_sdca.zip";
            foreach (var line in File.ReadLines(inputFile).Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                var example = JsonSerializer.Deserialize<FreeTextExample>(line, JsonOptions)
                    ?? throw new InvalidDataException($"Invalid prediction fixture row in {inputFile}.");
                yield return [modelFile, example.Id, example.Text, example.ExpectedLabel];
            }
        }
    }

    [CorpusModelTheory]
    [MemberData(nameof(PredictionCases))]
    [Trait("Category", "CorpusPredictions")]
    public void Trained_model_returns_the_recorded_free_text_prediction(
        string modelFile, string id, string text, string expectedLabel)
    {
        var modelDirectory = Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")!;
        var modelPath = Path.Combine(modelDirectory, modelFile);
        Assert.True(File.Exists(modelPath), $"Expected private model artifact at {modelPath}");
        using var predictor = new TextModelPredictor(modelPath);
        var prediction = predictor.Predict(text).PredictedLabel;
        output.WriteLine($"{modelFile} | {id} => {prediction}");
        Assert.Equal(expectedLabel, prediction);
    }

    private sealed record FreeTextExample(
        string Id,
        string Text,
        [property: JsonPropertyName("expected_label")] string ExpectedLabel);
}

public sealed class CorpusModelTheoryAttribute : TheoryAttribute
{
    public CorpusModelTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TYM_PRIVATE_MODEL_DIR")))
        {
            Skip = "Set TYM_PRIVATE_MODEL_DIR to the local directory containing the private corpus-trained ML.NET models.";
        }
    }
}
