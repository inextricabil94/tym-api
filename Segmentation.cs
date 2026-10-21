using System.Text.RegularExpressions;
using Microsoft.ML;
using Microsoft.ML.Data;

public sealed record SegmentationDecision(string EventId, string? PreviousEventId, string SegmentId,
    int SpanStart, bool IsBoundary, double? Score, IReadOnlyList<string> Evidence);

public sealed record SegmentationInfo(string Algorithm, string ModelVersion, string ScoreKind,
    int TrainingExamples, string TrainingSource, IReadOnlyList<SegmentationDecision> Decisions);

internal static class TymSegmentation
{
    public const string Rules = "article_rules";
    public const string Logistic = "mlnet_logistic";
    public const string Perceptron = "mlnet_perceptron";
    private const string SeedVersion = "boundary-feature-seed-v1";
    private static readonly string[] FeatureNames = ["actor_change", "temporal_category_change", "explicit_temporal_cue",
        "conflicting_time_anchor", "location_change", "paragraph_break", "missing_actors", "same_sentence"];
    private static readonly Lazy<BoundaryModel> LogisticModel = new(() => BoundaryModel.Train(Logistic));
    private static readonly Lazy<BoundaryModel> PerceptronModel = new(() => BoundaryModel.Train(Perceptron));

    public static bool IsSupported(string? algorithm) => algorithm is Rules or Logistic or Perceptron;

    public static SegmentationInfo Describe(string algorithm, IReadOnlyList<SegmentationDecision> decisions) => new(
        algorithm, algorithm == Rules ? "article-boundary-rules-v1" : SeedVersion,
        algorithm == Logistic ? "uncalibrated_probability" : algorithm == Perceptron ? "margin" : "rule",
        algorithm == Rules ? 0 : SeedPatterns.Length,
        algorithm == Rules ? "article-guided rules" : "authored feature patterns; not a corpus-trained or validated model", decisions);

    public static SegmentationDecision Decide(string source, MutableNarrativeEvent? previous,
        MutableNarrativeEvent current, bool ruleBoundary, string algorithm)
    {
        if (!IsSupported(algorithm)) throw new ArgumentException("Unknown segmentation algorithm.", nameof(algorithm));
        if (previous is null) return new(current.Id, null, "", current.SpanStart, true, null, ["document_start"]);
        var features = Features(source, previous, current);
        var evidence = FeatureNames.Where((_, index) => features[index] > 0).ToArray();
        if (evidence.Length == 0) evidence = ["continuity_features"];
        var (boundary, score) = algorithm switch
        {
            Logistic => LogisticModel.Value.Predict(features),
            Perceptron => PerceptronModel.Value.Predict(features),
            _ => (ruleBoundary, (double?)null)
        };
        return new(current.Id, previous.Id, "", current.SpanStart, boundary, score, evidence);
    }

    private static float[] Features(string source, MutableNarrativeEvent previous, MutableNarrativeEvent current)
    {
        var gap = source[previous.SpanEnd..current.SpanStart];
        var actorsChanged = !new HashSet<string>(previous.Actors, StringComparer.OrdinalIgnoreCase).SetEquals(current.Actors);
        static bool KnownPlace(string value) => value.Length > 0 && value is not ("unknown" or "necunoscut");
        return [
            actorsChanged ? 1 : 0,
            previous.TemporalCategory != current.TemporalCategory ? 1 : 0,
            current.RelationCue.Length > 0 && current.RelationToPrevious is "BEFORE" or "AFTER" or "SIMULTANEOUS" ? 1 : 0,
            previous.TemporalAnchor.Length > 0 && current.TemporalAnchor.Length > 0
                && !previous.TemporalAnchor.Equals(current.TemporalAnchor, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            KnownPlace(previous.Location) && KnownPlace(current.Location)
                && !previous.Location.Equals(current.Location, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
            Regex.IsMatch(gap, @"\r?\n[\t ]*\r?\n", RegexOptions.CultureInvariant) ? 1 : 0,
            previous.Actors.Count == 0 || current.Actors.Count == 0 ? 1 : 0,
            !previous.Text.TrimEnd().EndsWith('.') && !previous.Text.TrimEnd().EndsWith('!')
                && !previous.Text.TrimEnd().EndsWith('?') && !gap.Contains('\n') ? 1 : 0
        ];
    }

    // Explicitly authored boundary/continuation examples over the eight feature columns above.
    // These seed patterns are not narrative annotations or an evaluation corpus.
    private static readonly (string Bits, bool Boundary)[] SeedPatterns = [
        ("00000000", false), ("00000001", false), ("00000010", false), ("00000011", false),
        ("00001001", false), ("00001011", false), ("00000101", false), ("00000111", false),
        ("10000001", false), ("10000011", false), ("10001001", false), ("10001011", false),
        ("00001000", true), ("00001100", true), ("00001110", true), ("00000100", true),
        ("00000110", true), ("00001010", true), ("10000000", true), ("10000010", true),
        ("10001000", true), ("10000100", true), ("10001100", true), ("10001110", true),
        ("01000000", true), ("01000001", true), ("01000010", true), ("01001000", true),
        ("00100000", true), ("00100001", true), ("00100010", true), ("00101000", true),
        ("00010000", true), ("00010001", true), ("00010010", true), ("00011000", true),
        ("11000000", true), ("10100000", true), ("10010000", true), ("01100000", true),
        ("00000000", false), ("00000001", false), ("00000010", false), ("00000011", false),
        ("00001001", false), ("00001011", false), ("00000101", false), ("00000111", false)
    ];

    private sealed class BoundaryRow
    {
        [VectorType(8)] public float[] Features { get; set; } = new float[8];
        public bool Label { get; set; }
    }
    private sealed class MarginPrediction
    {
        public bool PredictedLabel { get; set; }
        public float Score { get; set; }
    }
    private sealed class ProbabilityPrediction
    {
        public bool PredictedLabel { get; set; }
        public float Probability { get; set; }
    }

    private sealed class BoundaryModel
    {
        private readonly object gate = new();
        private PredictionEngine<BoundaryRow, ProbabilityPrediction>? probability;
        private PredictionEngine<BoundaryRow, MarginPrediction>? margin;

        public static BoundaryModel Train(string algorithm)
        {
            var ml = new MLContext(seed: 42);
            var data = ml.Data.LoadFromEnumerable(SeedPatterns.Select(pattern => new BoundaryRow
            {
                Features = pattern.Bits.Select(bit => bit == '1' ? 1f : 0f).ToArray(), Label = pattern.Boundary
            }));
            var result = new BoundaryModel();
            if (algorithm == Logistic)
            {
                var model = ml.BinaryClassification.Trainers.SdcaLogisticRegression(new Microsoft.ML.Trainers.SdcaLogisticRegressionBinaryTrainer.Options
                { NumberOfThreads = 1, MaximumNumberOfIterations = 100, L1Regularization = 0, L2Regularization = 0.0001f }).Fit(data);
                result.probability = ml.Model.CreatePredictionEngine<BoundaryRow, ProbabilityPrediction>(model);
            }
            else
            {
                var model = ml.BinaryClassification.Trainers.AveragedPerceptron(numberOfIterations: 100).Fit(data);
                result.margin = ml.Model.CreatePredictionEngine<BoundaryRow, MarginPrediction>(model);
            }
            return result;
        }

        public (bool Boundary, double? Score) Predict(float[] features)
        {
            lock (gate)
            {
                var row = new BoundaryRow { Features = features };
                if (probability is not null)
                {
                    var prediction = probability.Predict(row);
                    if (!float.IsFinite(prediction.Probability)) throw new InvalidOperationException("Invalid boundary probability.");
                    return (prediction.PredictedLabel, Math.Round(prediction.Probability, 6));
                }
                var scored = margin!.Predict(row);
                if (!float.IsFinite(scored.Score)) throw new InvalidOperationException("Invalid boundary margin.");
                return (scored.PredictedLabel, Math.Round(scored.Score, 6));
            }
        }
    }
}
