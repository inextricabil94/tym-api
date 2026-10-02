using Microsoft.ML;
using Xunit;
using Xunit.Abstractions;

namespace Tym.Modeling.Tests;

/// <summary>
/// Component tests for loading and scoring an ML.NET text model. The original,
/// synthetic examples below are demo data, not corpus annotations or evidence
/// of accuracy on unseen natural language.
/// </summary>
public sealed class TextModelPredictorUnitTests(
    SyntheticTextClassifierFixture fixture,
    ITestOutputHelper output) : IClassFixture<SyntheticTextClassifierFixture>
{
    [Theory]
    [InlineData("Yesterday we visited the old harbor.", "PAST")]
    [InlineData("Now we visit the old harbor.", "PRESENT")]
    [InlineData("Tomorrow we will visit the old harbor.", "FUTURE")]
    public void Predict_loads_saved_model_and_scores_unlabeled_free_text(
        string text,
        string expectedLabel)
    {
        Assert.DoesNotContain(text, SyntheticTextClassifierFixture.TrainingTexts);
        using var predictor = new TextModelPredictor(fixture.ModelPath);

        // Only free text is supplied: the predictor must keep the training Label
        // column compatible without requiring a target label from the caller.
        var prediction = predictor.Predict(text);

        output.WriteLine($"Synthetic demo model | {text} => {prediction.PredictedLabel}");
        Assert.Equal(expectedLabel, prediction.PredictedLabel);
    }

    [Fact]
    public void Independently_loaded_models_return_the_same_prediction()
    {
        using var first = new TextModelPredictor(fixture.ModelPath);
        using var second = new TextModelPredictor(fixture.ModelPath);
        const string text = "Tomorrow we will visit the old harbor.";

        Assert.Equal(first.Predict(text), second.Predict(text));
        Assert.Equal("FUTURE", second.Predict(text).PredictedLabel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Predict_rejects_blank_text(string? text)
    {
        using var predictor = new TextModelPredictor(fixture.ModelPath);

        Assert.ThrowsAny<ArgumentException>(() => predictor.Predict(text!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Constructor_rejects_blank_model_path(string? modelPath)
    {
        Assert.ThrowsAny<ArgumentException>(() => new TextModelPredictor(modelPath!));
    }

    [Fact]
    public void Constructor_reports_missing_model_path()
    {
        var missingPath = Path.Combine(fixture.DirectoryPath, "does-not-exist.zip");

        var exception = Assert.Throws<FileNotFoundException>(
            () => new TextModelPredictor(missingPath));

        Assert.Equal(missingPath, exception.FileName);
    }
}

/// <summary>
/// Creates a real model entirely from small, original synthetic sentences.
/// Its temporary artifact is removed after the tests; private data and model
/// weights are never prerequisites for this fixture.
/// </summary>
public sealed class SyntheticTextClassifierFixture : IDisposable
{
    private static readonly DemoTrainingRow[] Rows =
    [
        new() { Text = "Yesterday we walked in the garden.", Label = "PAST" },
        new() { Text = "Yesterday they talked at the station.", Label = "PAST" },
        new() { Text = "Yesterday I read a letter.", Label = "PAST" },
        new() { Text = "Yesterday she worked in the office.", Label = "PAST" },
        new() { Text = "Yesterday he waited near the door.", Label = "PAST" },
        new() { Text = "Yesterday we visited a museum.", Label = "PAST" },
        new() { Text = "Now we walk in the garden.", Label = "PRESENT" },
        new() { Text = "Now they talk at the station.", Label = "PRESENT" },
        new() { Text = "Now I read a letter.", Label = "PRESENT" },
        new() { Text = "Now she works in the office.", Label = "PRESENT" },
        new() { Text = "Now he waits near the door.", Label = "PRESENT" },
        new() { Text = "Now we visit a museum.", Label = "PRESENT" },
        new() { Text = "Tomorrow we will walk in the garden.", Label = "FUTURE" },
        new() { Text = "Tomorrow they will talk at the station.", Label = "FUTURE" },
        new() { Text = "Tomorrow I will read a letter.", Label = "FUTURE" },
        new() { Text = "Tomorrow she will work in the office.", Label = "FUTURE" },
        new() { Text = "Tomorrow he will wait near the door.", Label = "FUTURE" },
        new() { Text = "Tomorrow we will visit a museum.", Label = "FUTURE" }
    ];

    public static IEnumerable<string> TrainingTexts => Rows.Select(row => row.Text);

    public string DirectoryPath { get; } = Path.Combine(
        Path.GetTempPath(), "tym-predictor-tests-" + Guid.NewGuid().ToString("N"));

    public string ModelPath => Path.Combine(DirectoryPath, "synthetic-demo.zip");

    public SyntheticTextClassifierFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        try
        {
            var mlContext = new MLContext(seed: 42);
            var trainingData = mlContext.Data.LoadFromEnumerable(Rows);
            var pipeline = mlContext.Transforms.Conversion.MapValueToKey("LabelKey", "Label")
                .Append(mlContext.Transforms.Text.FeaturizeText("Features", "Text"))
                .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "LabelKey",
                    featureColumnName: "Features",
                    maximumNumberOfIterations: 80))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue(
                    "PredictedLabelText", "PredictedLabel"));
            var model = pipeline.Fit(trainingData);
            mlContext.Model.Save(model, trainingData.Schema, ModelPath);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        var target = Path.GetFullPath(DirectoryPath);
        var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var name = Path.GetFileName(target);
        const string prefix = "tym-predictor-tests-";
        if (!target.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[prefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected temporary model directory.");
        }
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }

    public sealed class DemoTrainingRow
    {
        public string Text { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
