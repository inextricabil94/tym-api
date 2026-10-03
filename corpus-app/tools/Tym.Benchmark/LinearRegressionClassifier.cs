using System.Diagnostics;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;

namespace Tym.Benchmark;

/// <summary>Adapts least-squares linear regression to categorical classification by fitting
/// one independent 0/1 indicator target per training class, then ranking the resulting scores.
/// No arbitrary ordinal encoding of semantic classes is used. Scores are not probabilities.</summary>
public static class LinearRegressionClassifier
{
    public static LinearClassifierResult FitPredict(MLContext context, VBuffer<float>[] train,
        string[] trainLabels, VBuffer<float>[] validation)
    {
        if (train.Length == 0 || train.Length != trainLabels.Length || validation.Length == 0)
            throw new ArgumentException("Need matching training vectors/labels and validation vectors.");
        var dimension = train[0].Length;
        if (dimension == 0 || train.Concat(validation).Any(vector => vector.Length != dimension))
            throw new ArgumentException("Feature dimensions must agree.");
        var labels = trainLabels.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (labels.Length < 2) throw new ArgumentException("Need two training classes.");
        var schema = SchemaDefinition.Create(typeof(RegressionInput));
        schema[nameof(RegressionInput.Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, dimension);
        var watch = Stopwatch.StartNew();
        var models = new List<ITransformer>();
        foreach (var label in labels)
        {
            var view = context.Data.LoadFromEnumerable(train.Select((vector, index) => new RegressionInput
                { Features = vector, Target = trainLabels[index] == label ? 1 : 0 }), schema);
            models.Add(context.Regression.Trainers.Sdca(new SdcaRegressionTrainer.Options
            {
                LabelColumnName = nameof(RegressionInput.Target), FeatureColumnName = nameof(RegressionInput.Features),
                NumberOfThreads = 1, MaximumNumberOfIterations = 100, LossFunction = new SquaredLoss()
            }).Fit(view));
        }
        var fitSeconds = watch.Elapsed.TotalSeconds;
        var validationView = context.Data.LoadFromEnumerable(validation.Select(vector => new RegressionInput { Features = vector }), schema);
        // Warm-up is excluded from measured batched prediction time and never fits parameters.
        foreach (var model in models) _ = context.Data.CreateEnumerable<RegressionOutput>(model.Transform(validationView), false).First();
        watch.Restart();
        var scores = models.Select(model => context.Data.CreateEnumerable<RegressionOutput>(model.Transform(validationView), false)
            .Select(row => row.Score).ToArray()).ToArray();
        if (scores.Any(values => values.Length != validation.Length || values.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Linear classifier returned missing or nonfinite regression scores.");
        var predictions = Enumerable.Range(0, validation.Length).Select(row =>
            labels[Enumerable.Range(0, labels.Length).OrderByDescending(label => scores[label][row]).ThenBy(label => label).First()]).ToArray();
        return new(predictions, fitSeconds, watch.Elapsed.TotalSeconds, new
        {
            trainer = "ML.NET SdcaRegression / squared loss / one-vs-rest indicator targets",
            training_class_count = labels.Length, maximum_iterations = 100, threads = 1,
            target_encoding = "one 0/1 target per training class; never integer class ordinals",
            decision = "argmax unconstrained regression score; not calibrated probability",
            accuracy_scope = "categorical classification adaptation; not numerical temporal grounding"
        });
    }

    private sealed class RegressionInput
    {
        public VBuffer<float> Features { get; set; }
        public float Target { get; set; }
    }
    private sealed class RegressionOutput { public float Score { get; set; } }
}

public sealed record LinearClassifierResult(string[] Predictions, double FitSeconds, double PredictionSeconds, object Parameters);
