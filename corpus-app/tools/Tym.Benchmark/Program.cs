using System.Diagnostics;
using System.Text.Json;
using Tym.Benchmark;
using Tym.Corpus.Data;

try
{
    if (args.Length == 0 || args[0] is "--help" or "help")
    {
        Console.WriteLine("Grouped comparison: --data private.jsonl --out report.json [--folds 3] [--seed 42] [--task timebank_tlink] [--algorithms all|comma-separated-names] [--features text|structured] [--epochs 5]. Training: --mode train-structured --data private.jsonl --out report.json --models private-directory. Books: --mode books --data private-books.jsonl --out report.json --models new-private-directory [--clusters 8] [--rank 32].");
        Console.WriteLine("Classifiers: " + string.Join(", ", ClassificationTrial.AllNames));
        return 0;
    }
    if (args.Length % 2 != 0) throw new ArgumentException("Options require --name value pairs.");
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
        if (!args[i].StartsWith("--") || !options.TryAdd(args[i][2..], args[i + 1])) throw new ArgumentException("Malformed/repeated option.");
    var allowed = new[] { "data", "out", "folds", "seed", "task", "mode", "models", "algorithms", "features", "epochs", "clusters", "rank" };
    if (options.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal))) throw new ArgumentException("Unknown benchmark option.");
    var mode = options.GetValueOrDefault("mode", "compare");
    if (mode is not ("compare" or "train-structured" or "books")) throw new ArgumentException("Supported modes are compare, train-structured and books.");
    var input = options.GetValueOrDefault("data") ?? throw new ArgumentException("Missing --data.");
    var output = options.GetValueOrDefault("out") ?? throw new ArgumentException("Missing --out.");
    var seed = int.Parse(options.GetValueOrDefault("seed", "42"));
    if (mode == "books")
    {
        Reject(options, "folds", "task", "algorithms", "features", "epochs");
        BookExploration.Run(input, output, options.GetValueOrDefault("models") ?? throw new ArgumentException("Missing --models."),
            seed, int.Parse(options.GetValueOrDefault("clusters", "8")), int.Parse(options.GetValueOrDefault("rank", "32")));
        return 0;
    }
    Reject(options, "clusters", "rank");
    var rows = File.ReadLines(input).Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(line => JsonSerializer.Deserialize<CorpusRow>(line, CorpusFiles.Json) ?? throw new InvalidDataException("Empty input row.")).ToArray();
    if (rows.Length == 0 || rows.Any(row => string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Text)
        || string.IsNullOrWhiteSpace(row.Label) || string.IsNullOrWhiteSpace(row.DocumentId) || string.IsNullOrWhiteSpace(row.Task)
        || string.IsNullOrWhiteSpace(row.Language) || string.IsNullOrWhiteSpace(row.SourceGroup)
        || row.SourceType != "provided_annotation" || row.ReviewStatus != "adjudication_unknown")
        || rows.Select(row => row.Id).Distinct().Count() != rows.Length) throw new InvalidDataException("Expected unique provided-annotation rows with source document provenance.");
    if (mode == "train-structured")
    {
        Reject(options, "task", "folds", "algorithms", "features", "epochs");
        FullDataTraining.Train(rows, input, output, options.GetValueOrDefault("models") ?? throw new ArgumentException("Missing --models."), seed);
        return 0;
    }
    Reject(options, "models");
    var folds = int.Parse(options.GetValueOrDefault("folds", "3"));
    var epochs = int.Parse(options.GetValueOrDefault("epochs", "5"));
    if (epochs is < 1 or > 50) throw new ArgumentException("Use 1-50 fixed neural epochs.");
    var representation = options.GetValueOrDefault("features", "text");
    if (representation is not ("text" or "structured")) throw new ArgumentException("Features must be text or structured.");
    var names = ClassificationTrial.ResolveNames(options.GetValueOrDefault("algorithms", "sdca,lbfgs,structured_sdca"));
    if (options.TryGetValue("task", out var requestedTask) && !rows.Any(row => row.Task == requestedTask)) throw new ArgumentException("Requested task does not exist.");
    var watch = Stopwatch.StartNew();
    var plan = GroupedSplit.Create(rows, folds, seed);
    var taskReports = new List<object>();
    foreach (var group in rows.GroupBy(row => (row.Task, row.Language)).OrderBy(group => group.Key.Task, StringComparer.Ordinal))
    {
        if (options.TryGetValue("task", out var filter) && group.Key.Task != filter) continue;
        var selected = group.ToArray();
        var labels = selected.Select(row => row.Label).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var scored = names.Prepend("majority").ToDictionary(name => name, _ => new List<(string Actual, string Predicted)>(), StringComparer.Ordinal);
        var timings = names.Prepend("majority").ToDictionary(name => name, _ => new List<Timing>(), StringComparer.Ordinal);
        var foldReports = new List<object>();
        for (var fold = 0; fold < folds; fold++)
        {
            var train = selected.Where(row => plan.DocumentFolds[GroupedSplit.DocumentKey(row)] != fold).ToArray();
            var validation = selected.Where(row => plan.DocumentFolds[GroupedSplit.DocumentKey(row)] == fold).ToArray();
            if (train.Length == 0 || validation.Length == 0 || train.Select(row => row.Label).Distinct().Count() < 2) throw new InvalidDataException("A task fold cannot be evaluated.");
            var baselineWatch = Stopwatch.StartNew();
            var majority = train.GroupBy(row => row.Label).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
            var baselineFit = baselineWatch.Elapsed.TotalSeconds;
            baselineWatch.Restart();
            var majorityRows = validation.Select(row => (row.Label, majority)).ToArray();
            var baselinePredict = baselineWatch.Elapsed.TotalSeconds;
            scored["majority"].AddRange(majorityRows);
            timings["majority"].Add(new(baselineFit, baselinePredict, validation.Length));
            var algorithms = new Dictionary<string, object>(StringComparer.Ordinal)
            { ["majority"] = new { metrics = Metrics.Calculate(majorityRows, labels), training_seconds = baselineFit, prediction_seconds = baselinePredict } };
            foreach (var name in names)
            {
                var trial = ClassificationTrial.Run(name, group.Key.Task, train, validation, seed + fold, representation, epochs);
                var observations = validation.Select((row, index) => (row.Label, trial.Predictions[index])).ToArray();
                scored[name].AddRange(observations);
                timings[name].Add(new(trial.TrainingSeconds, trial.PredictionSeconds, validation.Length));
                algorithms[name] = new
                {
                    metrics = Metrics.Calculate(observations, labels), training_seconds = trial.TrainingSeconds, prediction_seconds = trial.PredictionSeconds,
                    prediction_ms_per_row = trial.PredictionSeconds * 1000 / validation.Length,
                    batched_rows_per_second = validation.Length / Math.Max(trial.PredictionSeconds, 1e-9),
                    feature_channels = trial.Channels, feature_dimensions = trial.FeatureDimensions, configuration = trial.Configuration,
                    process_working_set_before_mb = trial.BeforeBytes / 1048576.0, process_working_set_after_mb = trial.AfterBytes / 1048576.0,
                    process_peak_working_set_mb = trial.PeakBytes / 1048576.0
                };
                Console.WriteLine($"{group.Key.Task}/{group.Key.Language} fold {fold + 1}/{folds} {name}: {train.Length} train, {validation.Length} validation, fit {trial.TrainingSeconds:F2}s, predict {trial.PredictionSeconds:F2}s");
            }
            foldReports.Add(new
            {
                fold, train_rows = train.Length, validation_rows = validation.Length,
                train_documents = train.Select(GroupedSplit.DocumentKey).Distinct().Count(), validation_documents = validation.Select(GroupedSplit.DocumentKey).Distinct().Count(),
                validation_labels_absent_from_training = labels.Where(label => validation.Any(row => row.Label == label) && !train.Any(row => row.Label == label)).ToArray(),
                majority_label = majority, algorithms
            });
        }
        if (scored.Values.Any(items => items.Count != selected.Length)) throw new InvalidDataException("Not every row was scored once.");
        taskReports.Add(new
        {
            task = group.Key.Task, language = group.Key.Language, rows = selected.Length, labels,
            algorithms = scored.ToDictionary(item => item.Key, item => Metrics.Calculate(item.Value, labels)),
            performance = timings.ToDictionary(item => item.Key, item => new
            {
                total_training_seconds = item.Value.Sum(value => value.Fit), total_prediction_seconds = item.Value.Sum(value => value.Predict),
                prediction_rows = item.Value.Sum(value => value.Rows),
                prediction_ms_per_row = item.Value.Sum(value => value.Predict) * 1000 / item.Value.Sum(value => value.Rows),
                batched_rows_per_second = item.Value.Sum(value => value.Rows) / Math.Max(item.Value.Sum(value => value.Predict), 1e-9)
            }), folds = foldReports
        });
        WriteReport("running_completed_tasks_checkpoint");
    }
    WriteReport("diagnostic_provided_annotation_cross_validation");
    Console.WriteLine($"Completed {taskReports.Count} task comparisons over {plan.IndependentComponents} components in {watch.Elapsed.TotalSeconds:F1}s.");
    return 0;

    void WriteReport(string status) => CorpusFiles.WriteJson(output, new
    {
        schema_version = 2, status, completed_utc = DateTimeOffset.UtcNow, input_sha256 = CorpusFiles.FileHash(input), total_rows = rows.Length,
        seed, folds, algorithms = names, feature_representation = representation, neural_epochs = epochs, seconds = watch.Elapsed.TotalSeconds,
        framework = "ML.NET 5.0.0 / .NET 10; C# TorchSharp 0.107.0 scratch neural baselines", models_exported = false,
        runtime = new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), logical_processors = Environment.ProcessorCount, dotnet = Environment.Version.ToString(), trainer_threads = 1 },
        split = plan, tasks = taskReports,
        limitations = new[]
        {
            "Provided annotation/adjudication_unknown; grouped diagnostic scores are not independent human-gold evaluation.",
            "All tasks share document components; exact normalized task/language input duplicates join documents before splitting. Near duplicates and shared events can remain.",
            "Scores classify labels conditional on supplied mentions and directed endpoints; no end-to-end span or graph accuracy is measured.",
            "Fixed exploratory algorithms/settings, no independent model-selection holdout or significance estimate.",
            "Native trainers share the selected ML.NET text/structured featurizer. Custom CART selects bounded training features. Scratch neural models use a training-only word vocabulary; representations differ.",
            "Entirely blank channels are omitted using training inputs only. All label maps, feature transforms and token vocabularies fit training rows.",
            "Fixed full-inventory macro-F1 retains rare labels absent from training folds.",
            "Linear regression uses separate 0/1 class-indicator targets and argmax scores, not ordinal class codes or numerical calendar prediction.",
            "MLP/CNN/LSTM/Transformer are compact C# scratch-trained experiments, not pretrained BERT/GPT or a reproduction of the historical paper model.",
            "Timing is one local CPU run per fold; native and linear scoring has one-row warm-up, custom scoring includes first invocation. Batched ms/row is not interactive latency. Fit includes feature fitting/training vectorization; prediction includes validation vectorization. Reading/parsing is excluded.",
            "Working-set figures belong to a shared process and are not isolated per-model memory measurements.",
            "The single TYM parallel work cannot supply source-disjoint literary validation and is excluded; raw books have no supervised semantic accuracy.",
            "Comparison mode exports no weights or deployed replacements. Full-data inference artifacts remain separate from grouped validation."
        }
    });
}
catch (Exception error) when (error is ArgumentException or IOException or JsonException or FormatException or InvalidOperationException or NotSupportedException)
{
    Console.Error.WriteLine("error: " + error.Message);
    return 2;
}
static void Reject(IReadOnlyDictionary<string, string> options, params string[] names)
{
    if (names.Any(options.ContainsKey)) throw new ArgumentException("Options do not apply to selected mode: " + string.Join(", ", names.Where(options.ContainsKey)));
}
public sealed record Timing(double Fit, double Predict, int Rows);
