using Microsoft.ML;
using Microsoft.ML.Data;

public sealed record SegmentationDecision(string EventId, string? PreviousEventId, string SegmentId,
    int SpanStart, bool IsBoundary, double? Score, IReadOnlyList<string> Evidence,
    string Basis = "temporal_continuity", string Frame = "narrative", string TimeReference = "");

public sealed record SegmentationInfo(string Algorithm, string ModelVersion, string ScoreKind,
    int TrainingExamples, string TrainingSource, IReadOnlyList<SegmentationDecision> Decisions,
    string Unit = "temporal_development");

internal static class TymSegmentation
{
    public const string Rules = "article_rules";
    public const string Logistic = "mlnet_logistic";
    public const string Perceptron = "mlnet_perceptron";
    private const string SeedVersion = "temporal-boundary-seed-v2";
    private static readonly string[] FeatureNames = ["time_anchor_change", "flashback_entry", "narrative_time_return",
        "forward_time_jump", "parallel_time_frame", "development_frame_change", "tense_frame_change", "same_time_reference"];
    private static readonly Lazy<BoundaryModel> LogisticModel = new(() => BoundaryModel.Train(Logistic));
    private static readonly Lazy<BoundaryModel> PerceptronModel = new(() => BoundaryModel.Train(Perceptron));

    public static bool IsSupported(string? algorithm) => algorithm is Rules or Logistic or Perceptron;

    public static SegmentationInfo Describe(string algorithm, IReadOnlyList<SegmentationDecision> decisions) => new(
        algorithm, algorithm == Rules ? "temporal-development-rules-v2" : SeedVersion,
        algorithm == Logistic ? "uncalibrated_probability" : algorithm == Perceptron ? "margin" : "rule",
        algorithm == Rules ? 0 : SeedPatterns.Length,
        algorithm == Rules ? "SYNASC 2016-inspired temporal/development continuity rules"
            : "authored temporal feature patterns; not a corpus-trained or validated model", decisions);

    public static SegmentationDecision Decide(MutableNarrativeEvent? previous,
        MutableNarrativeEvent current, TemporalDevelopmentContext context, string algorithm)
    {
        if (!IsSupported(algorithm)) throw new ArgumentException("Unknown segmentation algorithm.", nameof(algorithm));
        var features = context.Observe(current);
        if (previous is null) return new(current.Id, null, "", current.SpanStart, true, null, ["document_start"],
            "document_start", context.Frame, context.TimeReference);
        var evidence = FeatureNames.Where((_, index) => features[index] > 0).Concat(context.DevelopmentEvidence).ToArray();
        if (evidence.Length == 0) evidence = ["temporal_continuity"];
        var explicitShift = features.Take(6).Any(value => value > 0);
        var candidate = explicitShift || features[6] > 0 && features[7] == 0;
        var (boundary, score) = algorithm switch
        {
            Logistic => LogisticModel.Value.Predict(features),
            Perceptron => PerceptronModel.Value.Predict(features),
            _ => (candidate, (double?)null)
        };
        // A model cannot manufacture a temporal boundary from punctuation or a name change.
        boundary = explicitShift || candidate && boundary;
        return new(current.Id, previous.Id, "", current.SpanStart, boundary, score, evidence,
            explicitShift ? "explicit_context_shift" : candidate ? "temporal_candidate" : "temporal_continuity",
            context.Frame, context.TimeReference);
    }

    private static readonly (string Bits, bool Boundary)[] SeedPatterns = CreateSeedPatterns();

    private static (string Bits, bool Boundary)[] CreateSeedPatterns()
    {
        // Synthetic temporal patterns with explicit continuation weighting, not corpus annotations.
        var rows = new List<(string, bool)>();
        for (var feature = 0; feature < 6; feature++)
        for (var suffix = 0; suffix < 4; suffix++)
        {
            var bits = "00000000".ToCharArray(); bits[feature] = '1';
            bits[6] = (suffix & 2) == 0 ? '0' : '1'; bits[7] = (suffix & 1) == 0 ? '0' : '1';
            rows.Add((new string(bits), true));
        }
        rows.AddRange(Enumerable.Repeat(("00000010", true), 8));
        for (var index = 0; index < 4; index++)
            rows.AddRange(new[] { ("00000000", false), ("00000001", false), ("00000011", false), ("00000000", false) });
        return rows.ToArray();
    }

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
