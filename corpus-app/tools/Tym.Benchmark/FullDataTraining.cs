using System.Diagnostics;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Tym.Corpus.Core;
using Tym.Corpus.Data;

namespace Tym.Benchmark;

/// <summary>Fits conditional label classifiers on all provided annotations after diagnostic cross-validation.
/// These weights support inference. Fitting does not produce an accuracy estimate.</summary>
public static class FullDataTraining
{
    public static void Train(IReadOnlyList<CorpusRow> rows, string inputPath, string reportPath, string modelDirectory, int seed)
    {
        Directory.CreateDirectory(modelDirectory);
        var reports = new List<object>();
        var watch = Stopwatch.StartNew();
        foreach (var group in rows.GroupBy(row => (row.Task, row.Language)).OrderBy(group => group.Key.Task, StringComparer.Ordinal))
        {
            if (!group.Key.Task.StartsWith("timebank_", StringComparison.Ordinal))
                throw new ArgumentException("Structured training supports TimeBank target and ordered endpoint tasks only.");
            var selected = group.ToArray();
            var labels = selected.Select(row => row.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (labels.Length < 2) throw new InvalidDataException("Training requires at least two labels.");
            var modelId = group.Key.Task + "_" + group.Key.Language + "_structured";
            var artifactPath = Path.Combine(modelDirectory, modelId + "_sdca.zip");
            if (File.Exists(artifactPath) || File.Exists(artifactPath + ".manifest.json"))
                throw new IOException("Choose an output directory without existing structured model artifacts.");
            var trial = Stopwatch.StartNew();
            var context = new MLContext(seed);
            var inputs = selected.Select(row => TaskInputParser.Parse(row.Task, row.Text, row.Label)).ToArray();
            var channels = TaskInputParser.Channels(group.Key.Task)
                .Where(channel => inputs.Any(input => !string.IsNullOrWhiteSpace(TaskInputParser.Channel(input, channel)))).ToArray();
            var features = new EstimatorChain<ITransformer>();
            foreach (var channel in channels)
                features = features.Append(context.Transforms.Text.FeaturizeText(channel + "Features", channel));
            var training = context.Data.LoadFromEnumerable(inputs);
            var model = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(TextModelInput.Label))
                .Append(features.Append(context.Transforms.Concatenate("Features", channels.Select(channel => channel + "Features").ToArray())))
                .AppendCacheCheckpoint(context)
                .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy(new SdcaMaximumEntropyMulticlassTrainer.Options
                { LabelColumnName = "LabelKey", FeatureColumnName = "Features", NumberOfThreads = 1, MaximumNumberOfIterations = 100 }))
                .Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel")).Fit(training);
            context.Model.Save(model, training.Schema, artifactPath);
            var manifest = new
            {
                schema_version = 1, model_id = modelId, task = group.Key.Task, language = group.Key.Language,
                input_format = TaskInputParser.StructuredFormat, trainer = "SdcaMaximumEntropy",
                featurizer = "ML.NET FeaturizeText per structured channel, concatenated", feature_channels = channels,
                seed, maximum_iterations = 100, training_rows = selected.Length, labels,
                source_types = selected.Select(row => row.SourceType).Distinct().Order().ToArray(),
                source_groups = selected.Select(row => row.SourceGroup).Distinct().Order().ToArray(),
                document_count = selected.Select(GroupedSplit.DocumentKey).Distinct().Count(),
                input_sha256 = CorpusFiles.FileHash(inputPath), model_sha256 = CorpusFiles.FileHash(artifactPath),
                framework = "ML.NET 5.0.0 / .NET 10", trained_utc = DateTimeOffset.UtcNow,
                research_limit = "Full-data exploratory classifier conditional on supplied target mentions or ordered endpoints. Provided annotations have unknown adjudication status. No training-set accuracy or book-gold claim is made."
            };
            CorpusFiles.WriteJson(artifactPath + ".manifest.json", manifest);
            reports.Add(new { model_id = modelId, manifest, seconds = trial.Elapsed.TotalSeconds });
            Console.WriteLine($"{modelId}: trained {selected.Length} rows, {channels.Length} channels in {trial.Elapsed.TotalSeconds:F1}s");
        }
        CorpusFiles.WriteJson(reportPath, new
        {
            schema_version = 1, status = "full_data_inference_artifacts", completed_utc = DateTimeOffset.UtcNow,
            framework = "ML.NET 5.0.0 / .NET 10", input_sha256 = CorpusFiles.FileHash(inputPath),
            models = reports, seconds = watch.Elapsed.TotalSeconds,
            limitations = new[] { "The separate grouped cross-validation report supplies diagnostic scores; these full-data artifacts are not evaluated on their own training rows.",
                "Whole-text baseline models are retained with separate model IDs.", "Raw text and private book clustering weights are excluded from this aggregate report and deployment." }
        });
    }
}
