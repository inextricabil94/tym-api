using System.Collections.ObjectModel;
using Microsoft.ML;
using Microsoft.ML.Trainers;
using Microsoft.ML.Trainers.FastTree;
using Microsoft.ML.Trainers.LightGbm;

namespace Tym.Benchmark;

/// <summary>A serializable declaration of the trainer and its fixed experimental settings.</summary>
public sealed record AlgorithmDefinition(
    string Name,
    string Family,
    string Trainer,
    IReadOnlyDictionary<string, object> Parameters,
    string ScoreSemantics,
    IReadOnlyList<string> Limitations);

/// <summary>
/// ML.NET implementations of the classification families in the supplied algorithm chart.
/// The caller fits the returned estimator only on a training fold, after fitting the label map
/// and text featurizer on that same fold. No validation data enters this factory or calibration.
/// MLContext must also have a fixed seed; pass the same explicit seed to Create and Describe.
/// </summary>
public static class AlgorithmCatalog
{
    public const string LabelColumn = "LabelKey";
    public const string FeatureColumn = "Features";
    public const int DefaultSeed = 42;
    public const int LinearIterations = 100;
    public const int TreeLeaves = 16;
    public const int ForestTrees = 64;
    public const int BoostingTrees = 100;
    public const int MinimumLeafExamples = 5;
    public const int CalibrationExamples = 20_000;

    private static readonly string[] Names =
    [
        "sdca", "lbfgs", "naive_bayes", "linear_svm_ova", "fastforest_ova",
        "fasttree_ova", "lightgbm", "fasttree_single_tree_ova"
    ];

    /// <summary>Canonical names in a stable reporting order.</summary>
    public static IReadOnlyList<string> SupportedNames { get; } = Array.AsReadOnly(Names);

    /// <summary>
    /// Resolve an explicit comma-separated list, or all supported algorithms when absent.
    /// Unknown names, empty tokens, and repetitions are rejected instead of silently ignored.
    /// </summary>
    public static IReadOnlyList<string> ResolveNames(string? commaSeparated)
    {
        if (commaSeparated is null) return SupportedNames;
        var names = commaSeparated.Split(',').Select(name => name.Trim()).ToArray();
        if (names.Any(name => !Names.Contains(name, StringComparer.Ordinal)))
            throw new ArgumentException($"Expected algorithm names from: {string.Join(", ", Names)}.", nameof(commaSeparated));
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new ArgumentException("Algorithm names must not be repeated.", nameof(commaSeparated));
        return Array.AsReadOnly(names);
    }

    /// <summary>
    /// Return a bounded trainer consuming a key-valued LabelKey and a known-size float Features
    /// vector. Binary trainers are adapted to multiclass with one-versus-all; the single-tree
    /// variant is one boosted binary tree per class, not a general-purpose CART implementation.
    /// </summary>
    public static IEstimator<ITransformer> Create(MLContext context, string algorithm, int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateName(algorithm);
        return algorithm switch
        {
            "sdca" => context.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                new SdcaMaximumEntropyMulticlassTrainer.Options
                {
                    LabelColumnName = LabelColumn, FeatureColumnName = FeatureColumn,
                    NumberOfThreads = 1, MaximumNumberOfIterations = LinearIterations
                }),
            "lbfgs" => context.MulticlassClassification.Trainers.LbfgsMaximumEntropy(
                new LbfgsMaximumEntropyMulticlassTrainer.Options
                {
                    LabelColumnName = LabelColumn, FeatureColumnName = FeatureColumn,
                    NumberOfThreads = 1, MaximumNumberOfIterations = LinearIterations
                }),
            "naive_bayes" => context.MulticlassClassification.Trainers.NaiveBayes(LabelColumn, FeatureColumn),
            // Raw SVM margins are ranked directly; useProbabilities=false avoids a Platt fit.
            "linear_svm_ova" => context.MulticlassClassification.Trainers.OneVersusAll(
                context.BinaryClassification.Trainers.LinearSvm(new LinearSvmTrainer.Options
                {
                    LabelColumnName = LabelColumn, FeatureColumnName = FeatureColumn,
                    NumberOfIterations = LinearIterations, Shuffle = true
                }), labelColumnName: LabelColumn, imputeMissingLabelsAsNegative: false,
                useProbabilities: false),
            "fastforest_ova" => context.MulticlassClassification.Trainers.OneVersusAll(
                context.BinaryClassification.Trainers.FastForest(new FastForestBinaryTrainer.Options
                {
                    LabelColumnName = LabelColumn, FeatureColumnName = FeatureColumn,
                    NumberOfThreads = 1, NumberOfTrees = ForestTrees, NumberOfLeaves = TreeLeaves,
                    MinimumExampleCountPerLeaf = MinimumLeafExamples, Seed = seed,
                    FeatureSelectionSeed = seed, BaggingSize = 1, BaggingExampleFraction = 0.7,
                    FeatureFraction = 0.7, AllowEmptyTrees = true
                }), labelColumnName: LabelColumn, imputeMissingLabelsAsNegative: false,
                maximumCalibrationExampleCount: CalibrationExamples, useProbabilities: true),
            "fasttree_ova" => CreateBoostedTrees(context, BoostingTrees, seed),
            "fasttree_single_tree_ova" => CreateBoostedTrees(context, 1, seed),
            "lightgbm" => context.MulticlassClassification.Trainers.LightGbm(
                new LightGbmMulticlassTrainer.Options
                {
                    LabelColumnName = LabelColumn, FeatureColumnName = FeatureColumn,
                    NumberOfThreads = 1, NumberOfIterations = BoostingTrees, NumberOfLeaves = TreeLeaves,
                    MinimumExampleCountPerLeaf = MinimumLeafExamples, LearningRate = 0.1,
                    Seed = seed, Deterministic = true, ForceColumnWise = true
                }),
            _ => throw new ArgumentException("Unsupported algorithm.", nameof(algorithm))
        };
    }

    /// <summary>Expose settings independently of model execution for an auditable report.</summary>
    public static AlgorithmDefinition Describe(string algorithm, int seed = DefaultSeed)
    {
        ValidateName(algorithm);
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["label_column"] = LabelColumn, ["feature_column"] = FeatureColumn,
            ["ml_context_seed"] = seed, ["package_version"] = "5.0.0"
        };
        var limitations = new List<string>
        {
            "Fixed exploratory settings; no validation-driven hyperparameter search or independent selection holdout.",
            "Scores are not proof of calibrated confidence on new books or domains."
        };
        string family, trainer, scores;
        switch (algorithm)
        {
            case "sdca":
            case "lbfgs":
                family = "Logistic regression";
                trainer = algorithm == "sdca" ? nameof(SdcaMaximumEntropyMulticlassTrainer) : nameof(LbfgsMaximumEntropyMulticlassTrainer);
                parameters["maximum_iterations"] = LinearIterations;
                parameters["number_of_threads"] = 1;
                scores = "Multiclass maximum-entropy class scores.";
                break;
            case "naive_bayes":
                family = "Naive Bayes";
                trainer = nameof(NaiveBayesMulticlassTrainer);
                parameters["feature_interpretation"] = "presence: value > 0 is true; value <= 0 is false";
                parameters["parallel_training"] = false;
                scores = "Binary-feature class likelihood scores; not continuous-feature multinomial Naive Bayes.";
                limitations.Add("ML.NET uses feature presence, not TF-IDF magnitude; correlated n-grams violate the conditional independence assumption.");
                break;
            case "linear_svm_ova":
                family = "Support vector machine";
                trainer = "OneVersusAll(LinearSvmTrainer)";
                parameters["number_of_iterations"] = LinearIterations;
                parameters["shuffle"] = true;
                parameters["parallel_training"] = false;
                parameters["use_probabilities"] = false;
                scores = "Uncalibrated linear SVM margins; highest margin wins.";
                limitations.Add("Linear kernel only; one binary classifier per class, without probability calibration.");
                break;
            case "fastforest_ova":
                family = "Random forest";
                trainer = "OneVersusAll(FastForestBinaryTrainer)";
                AddTreeParameters(parameters, ForestTrees, seed);
                parameters["bagging_size"] = 1;
                parameters["bagging_example_fraction"] = 0.7;
                parameters["feature_fraction"] = 0.7;
                scores = "One-versus-all normalized binary probabilities; any needed Platt calibration uses only the training fold.";
                limitations.Add("Each class trains its own 64-tree forest, increasing multiclass training cost.");
                break;
            case "fasttree_ova":
            case "fasttree_single_tree_ova":
                family = algorithm == "fasttree_ova" ? "Gradient boosting" : "Single decision tree variant";
                trainer = "OneVersusAll(FastTreeBinaryTrainer)";
                AddTreeParameters(parameters, algorithm == "fasttree_ova" ? BoostingTrees : 1, seed);
                parameters["learning_rate"] = 0.1;
                scores = "One-versus-all normalized binary probabilities from FastTree; calibration receives no validation rows.";
                if (algorithm == "fasttree_single_tree_ova")
                    limitations.Add("One gradient-boosted binary tree per class; this is not an exact generic multiclass CART algorithm.");
                break;
            case "lightgbm":
                family = "Gradient boosting";
                trainer = nameof(LightGbmMulticlassTrainer);
                parameters["number_of_iterations"] = BoostingTrees;
                parameters["number_of_leaves"] = TreeLeaves;
                parameters["minimum_examples_per_leaf"] = MinimumLeafExamples;
                parameters["number_of_threads"] = 1;
                parameters["learning_rate"] = 0.1;
                parameters["seed"] = seed;
                parameters["deterministic"] = true;
                parameters["force_column_wise"] = true;
                scores = "Native multiclass LightGBM class scores.";
                limitations.Add("Requires the pinned platform-native LightGBM runtime; deterministic settings do not promise identical arithmetic across hardware.");
                break;
            default: throw new ArgumentException("Unsupported algorithm.", nameof(algorithm));
        }
        return new(algorithm, family, trainer, new ReadOnlyDictionary<string, object>(parameters),
            scores, limitations.AsReadOnly());
    }

    private static IEstimator<ITransformer> CreateBoostedTrees(MLContext context, int trees, int seed) =>
        context.MulticlassClassification.Trainers.OneVersusAll(
            context.BinaryClassification.Trainers.FastTree(new FastTreeBinaryTrainer.Options
            {
                LabelColumnName = LabelColumn, FeatureColumnName = FeatureColumn, NumberOfThreads = 1,
                NumberOfTrees = trees, NumberOfLeaves = TreeLeaves, MinimumExampleCountPerLeaf = MinimumLeafExamples,
                LearningRate = 0.1, Seed = seed, FeatureSelectionSeed = seed, AllowEmptyTrees = true
            }), labelColumnName: LabelColumn, imputeMissingLabelsAsNegative: false,
            maximumCalibrationExampleCount: CalibrationExamples, useProbabilities: true);

    private static void AddTreeParameters(IDictionary<string, object> parameters, int trees, int seed)
    {
        parameters["number_of_trees_per_class"] = trees;
        parameters["number_of_leaves"] = TreeLeaves;
        parameters["minimum_examples_per_leaf"] = MinimumLeafExamples;
        parameters["number_of_threads"] = 1;
        parameters["seed"] = seed;
        parameters["feature_selection_seed"] = seed;
        parameters["allow_empty_trees"] = true;
        parameters["use_probabilities"] = true;
        parameters["maximum_calibration_examples"] = CalibrationExamples;
        parameters["calibration_data"] = "training fold only, when binary trainer requires calibration";
    }

    private static void ValidateName(string algorithm)
    {
        if (!Names.Contains(algorithm, StringComparer.Ordinal))
            throw new ArgumentException($"Unsupported algorithm '{algorithm}'.", nameof(algorithm));
    }
}
