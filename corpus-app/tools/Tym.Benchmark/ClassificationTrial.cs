using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Text;
using Tym.Corpus.Data;
using Tym.NeuralBenchmark;
using ModelInput = Tym.Corpus.Core.TextModelInput;

namespace Tym.Benchmark;

public sealed class TrialOutput
{
    [ColumnName("PredictedLabelText")] public string PredictedLabel { get; set; } = string.Empty;
}
public sealed class NumericFeatures { public VBuffer<float> Features { get; set; } }
public sealed record TrialResult(string[] Predictions, double TrainingSeconds, double PredictionSeconds, object Configuration,
    string[] Channels, int? FeatureDimensions, long BeforeBytes, long AfterBytes, long PeakBytes);

/// <summary>Runs one fixed classifier with training-only transforms and batched validation scoring.
/// Validation labels are never passed to custom or neural fitting code.</summary>
public static class ClassificationTrial
{
    private static readonly Regex NeuralWord = new(@"[\p{L}\p{M}\p{Nd}]+(?:['’\-][\p{L}\p{M}\p{Nd}]+)*", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly string[] Additional = ["structured_sdca", "linear_regression_ovr", "decision_tree", "knn", "mlp", "cnn", "rnn", "transformer"];
    public static IReadOnlyList<string> AllNames { get; } = AlgorithmCatalog.SupportedNames.Where(name => name != "fasttree_single_tree_ova")
        .Concat(Additional.Where(name => name != "structured_sdca")).ToArray();
    public static IReadOnlyList<string> ResolveNames(string names)
    {
        if (names == "all") return AllNames;
        var selected = names.Split(',').Select(name => name.Trim()).ToArray();
        if (selected.Any(name => !AlgorithmCatalog.SupportedNames.Contains(name) && !Additional.Contains(name))
            || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length) throw new ArgumentException("Unknown, empty or repeated classifier name.");
        return selected;
    }
    public static TrialResult Run(string algorithm, string task, CorpusRow[] train, CorpusRow[] validation, int seed, string representation, int epochs, int ngramLimit = 0)
    {
        if (ngramLimit < 0) throw new ArgumentOutOfRangeException(nameof(ngramLimit), "N-gram budget must be nonnegative; zero retains legacy defaults.");
        using var process = Process.GetCurrentProcess(); process.Refresh(); var before = process.WorkingSet64;
        var context = new MLContext(seed);
        var trainInputs = train.Select(row => StructuredFeatures.Input(row.Task, row.Text, row.Label)).ToArray();
        var testInputs = validation.Select(row => StructuredFeatures.Input(row.Task, row.Text, row.Label)).ToArray();
        var structured = representation == "structured" || algorithm == "structured_sdca";
        var channels = structured ? Tym.Corpus.Core.TaskInputParser.Channels(task)
            .Where(channel => trainInputs.Any(input => !string.IsNullOrWhiteSpace(StructuredFeatures.Channel(input, channel)))).ToArray()
            : new[] { nameof(ModelInput.Text) };
        if (channels.Length == 0) throw new InvalidDataException("No nonblank training feature channel.");
        string[] predictions; double fitSeconds, predictionSeconds; object configuration; int? dimensions = null;
        if (algorithm is "mlp" or "cnn" or "rnn" or "transformer")
        {
            // Put explicit mentions/signals before the bounded neural context window, retaining
            // directed endpoint roles even when a long sentence context must be truncated.
            var roleOrder = new[] { nameof(ModelInput.SourceMention), nameof(ModelInput.TargetMention), nameof(ModelInput.Signal), nameof(ModelInput.SourceContext), nameof(ModelInput.TargetContext) };
            var neuralChannels = channels.OrderBy(channel => Array.IndexOf(roleOrder, channel) is var index && index >= 0 ? index : roleOrder.Length).ToArray();
            // Prefix every word with its channel role. A bag-of-words MLP otherwise loses
            // direction when the same source/target words are swapped between fields.
            string FeatureText(ModelInput input) => structured ? string.Join(" ", neuralChannels.SelectMany(channel =>
                NeuralWord.Matches(StructuredFeatures.Channel(input, channel)).Select(match => channel + "-" + match.Value))) : input.Text;
            var result = NeuralAlgorithms.FitPredict(algorithm, trainInputs.Select(FeatureText).ToArray(), train.Select(row => row.Label).ToArray(),
                testInputs.Select(FeatureText).ToArray(), seed, epochs);
            predictions = result.Predictions; fitSeconds = result.TrainingSeconds; predictionSeconds = result.PredictionSeconds;
            configuration = new { result.ModelParameters, result.TrainingVocabularySize, result.MaximumSequenceLength, result.Metadata };
        }
        else
        {
            var trainView = context.Data.LoadFromEnumerable(trainInputs); var testView = context.Data.LoadFromEnumerable(testInputs);
            var features = new EstimatorChain<ITransformer>();
            foreach (var channel in channels)
            {
                // MaximumNgramsCount caps each order separately, not the combined dictionary.
                // Every classifier using numeric text vectors receives this same train-fit transform.
                if (ngramLimit == 0) features = features.Append(context.Transforms.Text.FeaturizeText(channel + "Features", channel));
                else features = features.Append(context.Transforms.Text.FeaturizeText(channel + "Features", new TextFeaturizingEstimator.Options
                {
                    WordFeatureExtractor = new WordBagEstimator.Options
                    {
                        NgramLength = 2, UseAllLengths = true, SkipLength = 0,
                        MaximumNgramsCount = [ngramLimit, ngramLimit]
                    },
                    CharFeatureExtractor = new WordBagEstimator.Options
                    {
                        NgramLength = 3, UseAllLengths = false, SkipLength = 0,
                        // With UseAllLengths=false the API requires one limit applying only
                        // to the selected trigram order; a three-entry array is rejected.
                        MaximumNgramsCount = [ngramLimit]
                    }
                }, channel));
            }
            object featurizerConfiguration = ngramLimit == 0
                ? new { name = "ML.NET Text.FeaturizeText", budget = "legacy library defaults", learned_dictionary = "training fold only", channels }
                : new
                {
                    name = "ML.NET Text.FeaturizeText", package_version = "5.0.0", learned_dictionary = "training fold only", channels,
                    maximum_ngrams_per_order_per_channel = ngramLimit,
                    word_ngram_lengths = new[] { 1, 2 }, character_ngram_lengths = new[] { 3 }, skip_length = 0,
                    maximum_features_per_channel = (long)ngramLimit * 3,
                    maximum_concatenated_features = (long)ngramLimit * 3 * channels.Length,
                    other_options = "pinned ML.NET 5.0 default normalization and term weighting"
                };
            var featurePipeline = features.Append(context.Transforms.Concatenate("Features", channels.Select(channel => channel + "Features").ToArray()));
            var fitWatch = Stopwatch.StartNew();
            if (algorithm is "linear_regression_ovr" or "decision_tree" or "knn")
            {
                var featureModel = featurePipeline.Fit(trainView);
                var trainVectors = context.Data.CreateEnumerable<NumericFeatures>(featureModel.Transform(trainView), false).Select(row => Copy(row.Features)).ToArray();
                var transformTrainingSeconds = fitWatch.Elapsed.TotalSeconds;
                var transformWatch = Stopwatch.StartNew();
                var validationVectors = context.Data.CreateEnumerable<NumericFeatures>(featureModel.Transform(testView), false).Select(row => Copy(row.Features)).ToArray();
                var validationTransformSeconds = transformWatch.Elapsed.TotalSeconds; dimensions = trainVectors[0].Length;
                if (algorithm == "linear_regression_ovr")
                {
                    var result = LinearRegressionClassifier.FitPredict(context, trainVectors, train.Select(row => row.Label).ToArray(), validationVectors);
                    predictions = result.Predictions; fitSeconds = transformTrainingSeconds + result.FitSeconds; predictionSeconds = validationTransformSeconds + result.PredictionSeconds;
                    configuration = new { trainer = result.Parameters, featurizer = featurizerConfiguration };
                }
                else
                {
                    var result = CustomClassifiers.FitPredict(algorithm, trainVectors, train.Select(row => row.Label).ToArray(), validationVectors, seed);
                    predictions = result.Predictions; fitSeconds = transformTrainingSeconds + result.FitSeconds; predictionSeconds = validationTransformSeconds + result.PredictionSeconds;
                    configuration = new { trainer = result.Parameters, featurizer = featurizerConfiguration };
                }
            }
            else
            {
                var nativeName = algorithm == "structured_sdca" ? "sdca" : algorithm;
                var model = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(ModelInput.Label)).Append(featurePipeline)
                    .AppendCacheCheckpoint(context).Append(AlgorithmCatalog.Create(context, nativeName, seed))
                    .Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel")).Fit(trainView);
                fitSeconds = fitWatch.Elapsed.TotalSeconds;
                dimensions = ((VectorDataViewType)model.GetOutputSchema(trainView.Schema)["Features"].Type).Size;
                _ = context.Data.CreateEnumerable<TrialOutput>(model.Transform(testView), false).First();
                var predictionWatch = Stopwatch.StartNew();
                predictions = context.Data.CreateEnumerable<TrialOutput>(model.Transform(testView), false).Select(row => row.PredictedLabel).ToArray();
                predictionSeconds = predictionWatch.Elapsed.TotalSeconds;
                configuration = new { trainer = AlgorithmCatalog.Describe(nativeName, seed), featurizer = featurizerConfiguration };
            }
        }
        process.Refresh();
        if (predictions.Length != validation.Length) throw new InvalidDataException("Prediction count mismatch.");
        return new(predictions, fitSeconds, predictionSeconds, configuration, channels, dimensions, before, process.WorkingSet64, process.PeakWorkingSet64);
    }
    private static VBuffer<float> Copy(VBuffer<float> vector) { var result = new VBuffer<float>(); vector.CopyTo(ref result); return result; }
}
