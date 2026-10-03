using System.Text.Json;
using Tym.NeuralBenchmark;
using Xunit;

namespace Tym.Research.Tests;

/// <summary>
/// Tiny original authored fixtures exercise native neural software contracts. Predictions
/// are deliberately not compared with semantic gold labels or reported as NLP accuracy.
/// </summary>
public sealed class NeuralAlgorithmStudyTests
{
    [Theory]
    [InlineData("mlp")]
    [InlineData("cnn")]
    [InlineData("rnn")]
    [InlineData("transformer")]
    public void Scratch_neural_family_scores_only_training_classes_and_reports_training_only_contracts(string algorithm)
    {
        var training = new[]
        {
            "SourceMention-arrived TargetMention-left Signal-before Context-river",
            "SourceMention-left TargetMention-arrived Signal-after Context-garden",
            "SourceMention-început TargetMention-terminat Signal-înainte Context-râu",
            "SourceMention-terminat TargetMention-început Signal-după Context-grădină",
            string.Join(" ", Enumerable.Repeat("Context-river", 70)) + " SourceMention-arrived"
        };
        var labels = new[] { "BEFORE", "AFTER", "BEFORE", "AFTER", "BEFORE" };
        var validation = new[]
        {
            "SourceMention-sailed TargetMention-returned Signal-before Context-estuary",
            string.Join(" ", Enumerable.Repeat("Context-unseen", 70))
        };

        var result = NeuralAlgorithms.FitPredict(algorithm, training, labels, validation, seed: 42, epochs: 1);

        Assert.Equal(validation.Length, result.Predictions.Length);
        Assert.All(result.Predictions, label => Assert.Contains(label, labels));
        AssertFiniteCost(result.TrainingSeconds);
        AssertFiniteCost(result.PredictionSeconds);
        Assert.True(result.ModelParameters > 0);
        Assert.True(result.SampledPeakWorkingSetBytes > 0);
        // Exactly sixteen distinct training tokens plus PAD/UNK. The novel validation
        // tokens must not enlarge the training vocabulary.
        Assert.Equal(18, result.TrainingVocabularySize);
        Assert.Equal(64, result.MaximumSequenceLength);

        using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(result.Metadata));
        var root = metadata.RootElement;
        Assert.Equal(algorithm, root.GetProperty("algorithm").GetString());
        Assert.False(root.GetProperty("pretrained_weights").GetBoolean());
        Assert.False(root.GetProperty("mlnet_native_trainer").GetBoolean());
        Assert.False(root.GetProperty("validation_labels_used").GetBoolean());
        Assert.False(root.GetProperty("early_stopping").GetBoolean());
        Assert.Equal("training labels only", root.GetProperty("label_inventory_fit").GetString());
        Assert.StartsWith("training texts only", root.GetProperty("vocabulary_fit").GetString());
        Assert.Equal(new[] { "AFTER", "BEFORE" }, root.GetProperty("label_inventory").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(1, root.GetProperty("epochs").GetInt32());
        Assert.Equal(1, root.GetProperty("cpu_threads").GetInt32());
        Assert.Equal(2048, root.GetProperty("maximum_vocabulary_size_including_pad_and_unknown").GetInt32());
        Assert.Contains("first 64 tokens", root.GetProperty("truncation").GetString());
        AssertRetention(root.GetProperty("training_token_retention"), rows: 5, original: 87, retained: 80);
        AssertRetention(root.GetProperty("validation_token_retention"), rows: 2, original: 74, retained: 68);
    }

    [Fact]
    public void Autoencoder_compresses_32_dimensions_to_16_and_returns_finite_heldout_reconstruction()
    {
        var training = Enumerable.Range(0, 8).Select(row => Enumerable.Range(0, 32)
            .Select(column => (float)Math.Sin(row + column * 0.1)).ToArray()).ToArray();
        var validation = Enumerable.Range(8, 2).Select(row => Enumerable.Range(0, 32)
            .Select(column => (float)Math.Sin(row + column * 0.1)).ToArray()).ToArray();

        var result = NeuralAlgorithms.FitAutoencoder(training, validation, seed: 42, epochs: 1);

        Assert.Equal(32, result.InputDimensions);
        Assert.Equal(16, result.BottleneckDimensions);
        AssertFiniteCost(result.ValidationMeanSquaredError);
        AssertFiniteCost(result.LastEpochTrainingMeanSquaredError);
        AssertFiniteCost(result.TrainingSeconds);
        AssertFiniteCost(result.PredictionSeconds);
        Assert.True(result.ModelParameters > 0);
        Assert.True(result.SampledPeakWorkingSetBytes > 0);
        Assert.Equal(validation.Length, result.EncodedValidation.Length);
        Assert.All(result.EncodedValidation, row =>
        {
            Assert.Equal(16, row.Length);
            Assert.All(row, value => Assert.True(float.IsFinite(value)));
        });
        using var metadata = JsonDocument.Parse(JsonSerializer.Serialize(result.Metadata));
        var root = metadata.RootElement;
        Assert.False(root.GetProperty("pretrained_weights").GetBoolean());
        Assert.False(root.GetProperty("validation_used_for_fit").GetBoolean());
        Assert.False(root.GetProperty("early_stopping").GetBoolean());
        Assert.Equal("not applicable; unlabeled reconstruction", root.GetProperty("classification_accuracy").GetString());
        Assert.Equal(1, root.GetProperty("epochs").GetInt32());
        Assert.Equal(8, root.GetProperty("training_rows").GetInt32());
        Assert.Equal(2, root.GetProperty("validation_rows").GetInt32());
        Assert.Contains("training rows only", root.GetProperty("upstream_transform_contract").GetString());
    }

    private static void AssertFiniteCost(double value) => Assert.True(double.IsFinite(value) && value >= 0);
    private static void AssertRetention(JsonElement retention, int rows, int original, int retained)
    {
        Assert.Equal(rows, retention.GetProperty("rows").GetInt32());
        Assert.Equal(1, retention.GetProperty("truncated_rows").GetInt32());
        Assert.Equal(original, retention.GetProperty("original_tokens").GetInt64());
        Assert.Equal(retained, retention.GetProperty("retained_tokens").GetInt64());
        Assert.Equal((double)retained / original, retention.GetProperty("token_retention_fraction").GetDouble(), precision: 10);
    }
}
