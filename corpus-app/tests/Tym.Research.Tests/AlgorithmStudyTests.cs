using Microsoft.ML;
using Microsoft.ML.Data;
using Tym.Benchmark;
using Tym.Corpus.Data;
using Xunit;

namespace Tym.Research.Tests;

/// <summary>Software/format regression checks with original authored fixtures.
/// These assertions are not estimates of semantic accuracy on supplied books.</summary>
public sealed class AlgorithmStudyTests
{
    [Fact]
    public void Complete_classifier_catalog_is_strict_and_has_fourteen_variants()
    {
        Assert.Equal(14, ClassificationTrial.ResolveNames("all").Count);
        Assert.Contains("linear_regression_ovr", ClassificationTrial.AllNames);
        Assert.Contains("decision_tree", ClassificationTrial.AllNames);
        Assert.Contains("transformer", ClassificationTrial.AllNames);
        Assert.Throws<ArgumentException>(() => ClassificationTrial.ResolveNames("sdca,sdca"));
        Assert.Throws<ArgumentException>(() => ClassificationTrial.ResolveNames("sdca,"));
        Assert.Throws<ArgumentException>(() => ClassificationTrial.ResolveNames("unknown"));
    }

    [Theory]
    [InlineData("sdca")]
    [InlineData("lbfgs")]
    [InlineData("naive_bayes")]
    [InlineData("linear_svm_ova")]
    [InlineData("fastforest_ova")]
    [InlineData("fasttree_ova")]
    [InlineData("lightgbm")]
    public void Native_families_return_predictions_with_explicit_training_metadata(string algorithm)
    {
        var training = Enumerable.Range(0, 40).Select(index => Row("walk" + index, "Mara [TARGET] walked [/TARGET] into the garden.", "walked", "OCCURRENCE"))
            .Concat(Enumerable.Range(0, 40).Select(index => Row("state" + index, "Ana [TARGET] knew [/TARGET] the road to the river.", "knew", "I_STATE"))).ToArray();
        var validation = new[] { Row("new-1", "Mara [TARGET] arrived [/TARGET] near the quiet garden.", "arrived", "OCCURRENCE"),
            Row("new-2", "Ana [TARGET] believed [/TARGET] that the road was safe.", "believed", "I_STATE") };
        var result = ClassificationTrial.Run(algorithm, "timebank_event_class", training, validation, 42, "structured", 1);
        Assert.Equal(validation.Length, result.Predictions.Length);
        Assert.All(result.Predictions, prediction => Assert.Contains(prediction, new[] { "OCCURRENCE", "I_STATE" }));
        Assert.True(double.IsFinite(result.TrainingSeconds) && result.TrainingSeconds >= 0);
        Assert.True(double.IsFinite(result.PredictionSeconds) && result.PredictionSeconds >= 0);
        Assert.Equal(algorithm, AlgorithmCatalog.Describe(algorithm).Name);
    }

    [Theory]
    [InlineData("knn")]
    [InlineData("decision_tree")]
    public void Custom_numeric_classifiers_preserve_train_labels_and_deterministic_results(string algorithm)
    {
        var training = Enumerable.Range(0, 10).Select(_ => Vector(1, 0)).Concat(Enumerable.Range(0, 10).Select(_ => Vector(0, 1))).ToArray();
        var labels = Enumerable.Repeat("A", 10).Concat(Enumerable.Repeat("B", 10)).ToArray();
        var validation = new[] { Vector(1, 0), Vector(0, 1) };
        var first = CustomClassifiers.FitPredict(algorithm, training, labels, validation, 42);
        var second = CustomClassifiers.FitPredict(algorithm, training, labels, validation, 42);
        Assert.Equal(new[] { "A", "B" }, first.Predictions);
        Assert.Equal(first.Predictions, second.Predictions);
        Assert.Throws<ArgumentException>(() => CustomClassifiers.FitPredict(algorithm, training, labels, [Vector(1)], 42));
    }

    [Fact]
    public void Regression_adaptation_fits_class_indicators_and_returns_only_training_classes()
    {
        var train = Enumerable.Range(0, 20).Select(_ => Vector(1, 0)).Concat(Enumerable.Range(0, 20).Select(_ => Vector(0, 1))).ToArray();
        var result = LinearRegressionClassifier.FitPredict(new MLContext(42), train,
            Enumerable.Repeat("earlier", 20).Concat(Enumerable.Repeat("later", 20)).ToArray(), [Vector(1, 0), Vector(0, 1)]);
        Assert.Equal(new[] { "earlier", "later" }, result.Predictions);
        Assert.True(double.IsFinite(result.PredictionSeconds));
    }

    private static VBuffer<float> Vector(params float[] values) => new(values.Length, values);
    private static CorpusRow Row(string id, string context, string mention, string label) => new(id, "timebank_event_class",
        "Context: " + context + "\nEvent: " + mention, label, "ro", "provided_annotation", "authored-software-fixture",
        "adjudication_unknown", null, id, id);
}
