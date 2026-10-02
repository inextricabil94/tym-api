using System.Diagnostics;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Tym.Benchmark;
using Tym.Corpus.Data;
using ModelInput = Tym.Corpus.Core.TextModelInput;

try
{
    if (args.Length == 0 || args[0] is "--help" or "help")
    {
        Console.WriteLine("ML.NET diagnostic comparison: --data private.jsonl --out aggregate-report.json [--folds 3] [--seed 42] [--task timebank_tlink]. Full-data structured training: --mode train-structured --data private.jsonl --out aggregate-report.json --models private-model-directory.");
        return 0;
    }
    if (args.Length % 2 != 0) throw new ArgumentException("Options require --name value pairs.");
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index += 2)
        if (!args[index].StartsWith("--") || !options.TryAdd(args[index][2..], args[index + 1])) throw new ArgumentException("Malformed/repeated option.");
    var allowedOptions = new[] { "data", "out", "folds", "seed", "task", "mode", "models" };
    if (options.Keys.Any(key => !allowedOptions.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("Unknown benchmark option.");
    if (options.TryGetValue("mode", out var mode) && mode != "train-structured") throw new ArgumentException("The supported optional mode is train-structured.");
    if (mode == "train-structured" && (options.ContainsKey("task") || options.ContainsKey("folds")))
        throw new ArgumentException("Full-data structured training trains all supplied tasks; omit --task and --folds.");
    var input = options.GetValueOrDefault("data") ?? throw new ArgumentException("Missing --data.");
    var output = options.GetValueOrDefault("out") ?? throw new ArgumentException("Missing --out.");
    var rows = File.ReadLines(input).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => JsonSerializer.Deserialize<CorpusRow>(line, CorpusFiles.Json)
        ?? throw new InvalidDataException("Empty input row.")).ToArray();
    if (rows.Length == 0 || rows.Any(row => string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Text)
        || string.IsNullOrWhiteSpace(row.Label) || string.IsNullOrWhiteSpace(row.DocumentId)
        || string.IsNullOrWhiteSpace(row.Task) || string.IsNullOrWhiteSpace(row.Language)
        || string.IsNullOrWhiteSpace(row.SourceGroup)
        || row.SourceType != "provided_annotation" || row.ReviewStatus != "adjudication_unknown")
        || rows.Select(row => row.Id).Distinct().Count() != rows.Length) throw new InvalidDataException("Expected unique provided-annotation rows with source document provenance.");
    var folds = int.Parse(options.GetValueOrDefault("folds", "3"));
    var seed = int.Parse(options.GetValueOrDefault("seed", "42"));
    if (options.TryGetValue("task", out var requestedTask) && !rows.Any(row => row.Task == requestedTask))
        throw new ArgumentException("The requested --task does not exist in the supplied input.");
    if (options.GetValueOrDefault("mode") == "train-structured")
    {
        FullDataTraining.Train(rows, input, output, options.GetValueOrDefault("models")
            ?? throw new ArgumentException("Missing --models."), seed);
        return 0;
    }
    var watch = Stopwatch.StartNew();
    var plan = GroupedSplit.Create(rows, folds, seed);
    var taskReports = new List<object>();
    var tasks = rows.GroupBy(row => (row.Task, row.Language)).OrderBy(group => group.Key.Task, StringComparer.Ordinal);
    foreach (var group in tasks)
    {
        if (options.TryGetValue("task", out var filter) && group.Key.Task != filter) continue;
        var selected = group.ToArray();
        var labels = selected.Select(row => row.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var scored = new Dictionary<string, List<(string Actual, string Predicted)>>(StringComparer.Ordinal)
        { ["majority"] = [], ["sdca"] = [], ["lbfgs"] = [], ["structured_sdca"] = [] };
        var foldReports = new List<object>();
        for (var fold = 0; fold < folds; fold++)
        {
            var train = selected.Where(row => plan.DocumentFolds[GroupedSplit.DocumentKey(row)] != fold).ToArray();
            var test = selected.Where(row => plan.DocumentFolds[GroupedSplit.DocumentKey(row)] == fold).ToArray();
            if (train.Length == 0 || test.Length == 0 || train.Select(row => row.Label).Distinct().Count() < 2)
                throw new InvalidDataException("A task fold cannot be evaluated with the assigned groups.");
            var majority = train.GroupBy(row => row.Label).OrderByDescending(label => label.Count()).ThenBy(label => label.Key, StringComparer.Ordinal).First().Key;
            var majorityRows = test.Select(row => (row.Label, majority)).ToArray();
            scored["majority"].AddRange(majorityRows);
            var algorithms = new Dictionary<string, object>(StringComparer.Ordinal);
            algorithms["majority"] = Metrics.Calculate(majorityRows, labels);
            foreach (var algorithm in new[] { "sdca", "lbfgs", "structured_sdca" })
            {
                var trial = Stopwatch.StartNew();
                var context = new MLContext(seed: seed + fold);
                var trainInputs = train.Select(row => StructuredFeatures.Input(row.Task, row.Text, row.Label)).ToArray();
                var testInputs = test.Select(row => StructuredFeatures.Input(row.Task, row.Text, row.Label)).ToArray();
                var trainView = context.Data.LoadFromEnumerable(trainInputs);
                var testView = context.Data.LoadFromEnumerable(testInputs);
                var channels = group.Key.Task is "timebank_tlink" or "timebank_slink" or "timebank_alink"
                    ? new[] { nameof(ModelInput.SourceContext), nameof(ModelInput.TargetContext), nameof(ModelInput.SourceMention), nameof(ModelInput.TargetMention), nameof(ModelInput.Signal) }
                    : new[] { nameof(ModelInput.TargetContext), nameof(ModelInput.TargetMention) };
                channels = channels.Where(channel => trainInputs.Any(input => !string.IsNullOrWhiteSpace(StructuredFeatures.Channel(input, channel)))).ToArray();
                IEstimator<ITransformer> featurizer;
                if (algorithm == "structured_sdca")
                {
                    var estimator = context.Transforms.Text.FeaturizeText(channels[0] + "Features", channels[0]);
                    var structuredPipeline = new EstimatorChain<ITransformer>().Append(estimator);
                    foreach (var channel in channels.Skip(1)) structuredPipeline = structuredPipeline.Append(context.Transforms.Text.FeaturizeText(channel + "Features", channel));
                    featurizer = structuredPipeline.Append(context.Transforms.Concatenate("Features", channels.Select(channel => channel + "Features").ToArray()));
                }
                else featurizer = context.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Text));
                var basePipeline = context.Transforms.Conversion.MapValueToKey("LabelKey", nameof(ModelInput.Label))
                    .Append(featurizer).AppendCacheCheckpoint(context);
                IEstimator<ITransformer> trainer = algorithm != "lbfgs"
                    ? context.MulticlassClassification.Trainers.SdcaMaximumEntropy(new SdcaMaximumEntropyMulticlassTrainer.Options
                    { LabelColumnName = "LabelKey", FeatureColumnName = "Features", NumberOfThreads = 1, MaximumNumberOfIterations = 100 })
                    : context.MulticlassClassification.Trainers.LbfgsMaximumEntropy(new LbfgsMaximumEntropyMulticlassTrainer.Options
                    { LabelColumnName = "LabelKey", FeatureColumnName = "Features", NumberOfThreads = 1, MaximumNumberOfIterations = 100 });
                var model = basePipeline.Append(trainer).Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel")).Fit(trainView);
                var predictions = context.Data.CreateEnumerable<ModelOutput>(model.Transform(testView), reuseRowObject: false)
                    .Select(row => (row.Label, row.PredictedLabel)).ToArray();
                if (predictions.Length != test.Length) throw new InvalidDataException("Prediction count mismatch.");
                scored[algorithm].AddRange(predictions);
                algorithms[algorithm] = new { metrics = Metrics.Calculate(predictions, labels), seconds = trial.Elapsed.TotalSeconds,
                    feature_channels = algorithm == "structured_sdca" ? channels : [nameof(ModelInput.Text)] };
                Console.WriteLine($"{group.Key.Task}/{group.Key.Language} fold {fold + 1}/{folds} {algorithm}: {train.Length} train, {test.Length} validation, {trial.Elapsed.TotalSeconds:F1}s");
            }
            foldReports.Add(new
            {
                fold, train_rows = train.Length, validation_rows = test.Length,
                train_documents = train.Select(GroupedSplit.DocumentKey).Distinct().Count(),
                validation_documents = test.Select(GroupedSplit.DocumentKey).Distinct().Count(),
                validation_labels_absent_from_training = labels.Where(label => test.Any(row => row.Label == label) && !train.Any(row => row.Label == label)).ToArray(),
                majority_label = majority, algorithms
            });
        }
        if (scored.Values.Any(predictions => predictions.Count != selected.Length)) throw new InvalidDataException("Not every row was scored once.");
        taskReports.Add(new
        {
            task = group.Key.Task, language = group.Key.Language, rows = selected.Length, labels,
            algorithms = scored.ToDictionary(item => item.Key, item => Metrics.Calculate(item.Value, labels)), folds = foldReports
        });
    }
    CorpusFiles.WriteJson(output, new
    {
        schema_version = 1, status = "diagnostic_provided_annotation_cross_validation", completed_utc = DateTimeOffset.UtcNow,
        input_sha256 = CorpusFiles.FileHash(input), total_rows = rows.Length, seed, folds, seconds = watch.Elapsed.TotalSeconds,
        framework = "ML.NET 5.0.0 / .NET 10", models_exported = false,
        split = plan, tasks = taskReports,
        limitations = new[]
        {
            "Provided annotation/adjudication_unknown; retrospective grouped validation is not independent human-gold evaluation.",
            "All tasks share document components; exact normalized task/language feature duplicates join documents before splitting.",
            "These scores classify labels conditional on supplied gold-like mention boundaries and relation endpoints; they do not measure end-to-end span or graph extraction.",
            "Algorithms and 100-iteration settings fixed before inspecting validation scores; diagnostic comparisons have no separate model-selection holdout.",
            "Structured SDCA was added after the initial whole-text comparison to test endpoint-direction and target-mention channels; this hypothesis is exploratory and needs independent replication.",
            "A structured channel with no nonblank training values is omitted using training inputs only; no validation labels are inspected for that decision.",
            "Rare labels may be absent from training folds; fixed-inventory macro-F1 includes them with zero F1 when unrecovered.",
            "The single parallel TYM work cannot support source-disjoint literary validation; it is excluded from this benchmark.",
            "The full-data deployed models are separate artifacts and are not evaluated by scoring their own training rows."
        }
    });
    Console.WriteLine($"Completed {taskReports.Count} task comparisons over {plan.IndependentComponents} document components in {watch.Elapsed.TotalSeconds:F1}s. Aggregate report saved without corpus prose.");
    return 0;
}
catch (Exception error) when (error is ArgumentException or IOException or JsonException or FormatException or InvalidOperationException)
{
    Console.Error.WriteLine("error: " + error.Message);
    return 2;
}

public sealed class ModelOutput
{
    public string Label { get; set; } = string.Empty;
    [ColumnName("PredictedLabelText")]
    public string PredictedLabel { get; set; } = string.Empty;
}
