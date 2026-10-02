using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.ML;
using Microsoft.ML.Data;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        try
        {
            var command = args[0].ToLowerInvariant();
            var options = ParseOptions(args.Skip(1).ToArray());
            return command switch
            {
                "train" => Train(options),
                "evaluate" => Evaluate(options),
                "cluster" => Cluster(options),
                "predict" => Predict(options),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
            };
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidDataException
            or IOException
            or JsonException
            or FormatException
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
    }

    private static int Train(IReadOnlyDictionary<string, string> options)
    {
        var dataPath = Required(options, "data");
        var task = Required(options, "task");
        var language = Optional(options, "language", "en");
        var modelPath = Path.GetFullPath(Required(options, "model-out"));
        var rows = ReadJsonLines(dataPath);
        ValidateTrainingRows(rows);

        var selected = rows
            .Where(row => row.Task == task && row.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (options.TryGetValue("splits", out var splitsPath))
        {
            var manifest = ReadManifest(splitsPath);
            var split = Optional(options, "split", "train");
            if (split is not ("train" or "dev"))
            {
                throw new ArgumentException("Training may use the train or dev split. The test split is reserved for evaluation.");
            }

            var selectedGold = selected.Where(row => row.SourceType == "human_gold").ToList();
            var selectedSeeds = selected.Where(row => row.SourceType == "synthetic_seed").ToList();
            if (selected.Any(row => row.SourceType == "provided_annotation"))
            {
                throw new InvalidDataException(
                    "Provided annotations cannot use the gold split manifest until their source work is joined to the human-gold manifest. Train them without --splits.");
            }
            ValidateManifestCoverage(selectedGold, manifest);
            ValidateTrainSeedParents(selectedSeeds, selectedGold, manifest);
            selected = FilterBySplit(selected, manifest, split);
        }

        EnsureTrainable(selected, task, language);
        var mlContext = new MLContext(seed: Integer(options, "seed", 42));
        var trainingData = mlContext.Data.LoadFromEnumerable(selected.Select(row => new ModelInput
        {
            Text = row.Text,
            Label = row.Label
        }));
        var model = FitModel(mlContext, trainingData);

        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        mlContext.Model.Save(model, trainingData.Schema, modelPath);
        var trainedSynthetic = selected.Where(row => row.SourceType == "synthetic_seed").ToList();
        WriteJsonFile(modelPath + ".manifest.json", new
        {
            schema_version = 1,
            task,
            language,
            trainer = "SdcaMaximumEntropy",
            featurizer = "ML.NET FeaturizeText",
            seed = Integer(options, "seed", 42),
            training_rows = selected.Count,
            labels = selected.Select(row => row.Label).Distinct(StringComparer.Ordinal).Order().ToArray(),
            source_types = selected.Select(row => row.SourceType).Distinct(StringComparer.Ordinal).Order().ToArray(),
            source_groups = selected.Select(row => row.SourceGroup).Distinct(StringComparer.Ordinal).Order().ToArray(),
            document_count = selected.Where(row => !string.IsNullOrWhiteSpace(row.DocumentId))
                .Select(row => row.DocumentId).Distinct(StringComparer.Ordinal).Count(),
            research_limit = selected.Any(row => row.SourceType == "provided_annotation")
                ? "Exploratory training only: provided annotations have unknown adjudication status. No held-out generalization claim is supported."
                : null,
            synthetic_provenance = new
            {
                rows = trainedSynthetic.Count,
                rows_with_parent = trainedSynthetic.Count(row => !string.IsNullOrWhiteSpace(row.ParentId)),
                phenomenon_counts = CountOptional(trainedSynthetic.Select(row => row.Phenomenon)),
                transformation_counts = CountOptional(trainedSynthetic.Select(row => row.Transformation)),
                generator_version_counts = CountOptional(trainedSynthetic.Select(row => row.GeneratorVersion))
            },
            model_sha256 = HashFile(modelPath)
        });

        WriteOutput(new
        {
            status = "trained",
            task,
            language,
            training_rows = selected.Count,
            labels = selected.Select(row => row.Label).Distinct(StringComparer.Ordinal).Order().ToArray(),
            source_types = selected.Select(row => row.SourceType).Distinct(StringComparer.Ordinal).Order().ToArray(),
            model_path = modelPath,
            model_format = "ML.NET .zip"
        }, options);
        return 0;
    }

    private static int Evaluate(IReadOnlyDictionary<string, string> options)
    {
        var dataPath = Required(options, "data");
        var groupBy = Optional(options, "group-by", "chapter");
        if (groupBy is not ("chapter" or "document"))
        {
            throw new ArgumentException("--group-by must be chapter or document.");
        }

        var language = Optional(options, "language", "en");
        var rows = ReadJsonLines(dataPath);
        ValidateGoldRows(rows);
        var selected = rows
            .Where(row => row.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0)
        {
            throw new InvalidDataException($"No {language} gold rows were found in '{dataPath}'.");
        }

        var manifest = options.TryGetValue("splits", out var splitsPath)
            ? ReadManifest(splitsPath)
            : CreateManifest(
                selected,
                groupBy,
                Integer(options, "seed", 42),
                Number(options, "train-fraction", 0.70),
                Number(options, "dev-fraction", 0.15),
                Number(options, "test-fraction", 0.15));

        if (manifest.GroupBy != groupBy)
        {
            throw new InvalidDataException($"The split manifest groups by '{manifest.GroupBy}', but this run requested '{groupBy}'.");
        }

        ValidateManifestCoverage(selected, manifest);
        if (options.TryGetValue("splits-out", out var manifestOutput))
        {
            WriteJsonFile(manifestOutput, manifest);
        }

        var trainSeeds = options.TryGetValue("train-seeds", out var seedPath)
            ? ReadJsonLines(seedPath)
            : [];
        if (trainSeeds.Count > 0)
        {
            ValidateTrainingRows(trainSeeds);
            trainSeeds = trainSeeds
                .Where(row => row.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var goldIds = selected.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            var duplicateSeed = trainSeeds.FirstOrDefault(row => goldIds.Contains(row.Id));
            if (duplicateSeed is not null)
            {
                throw new InvalidDataException($"Training seed id '{duplicateSeed.Id}' duplicates a gold row id.");
            }

            ValidateTrainSeedParents(trainSeeds, selected, manifest);
        }

        var taskFilter = options.TryGetValue("task", out var taskValue) ? taskValue : null;
        var tasks = selected.Select(row => row.Task).Distinct(StringComparer.Ordinal).Order().ToArray();
        if (taskFilter is not null)
        {
            if (!tasks.Contains(taskFilter, StringComparer.Ordinal))
            {
                throw new ArgumentException($"No {language} gold examples were found for task '{taskFilter}'.");
            }

            tasks = [taskFilter];
        }

        var mlContext = new MLContext(seed: manifest.Seed);
        var taskReports = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var task in tasks)
        {
            var taskTrain = FilterBySplit(selected.Where(row => row.Task == task), manifest, "train");
            taskTrain.AddRange(trainSeeds.Where(row => row.Task == task));
            var taskTest = FilterBySplit(selected.Where(row => row.Task == task), manifest, "test");
            EnsureTrainable(taskTrain, task, language);
            if (taskTest.Count == 0)
            {
                throw new InvalidDataException($"The test split contains no {task} rows. Check the group assignment or data coverage.");
            }

            taskReports[task] = EvaluateTask(mlContext, task, language, taskTrain, taskTest);
        }

        var report = new
        {
            status = "evaluated",
            language,
            group_by = manifest.GroupBy,
            seed = manifest.Seed,
            data_scope = "adjudicated human gold only in dev/test; optional synthetic rows are appended to train only",
            split_summary = manifest.Summary,
            tasks = taskReports
        };
        WriteOutput(report, options);
        return 0;
    }

    private static int Cluster(IReadOnlyDictionary<string, string> options)
    {
        var dataPath = Required(options, "data");
        var modelPath = Path.GetFullPath(Required(options, "model-out"));
        var language = Optional(options, "language", "en");
        var clusterCount = Integer(options, "clusters", 8);
        if (clusterCount < 2)
        {
            throw new ArgumentException("--clusters must be at least 2.");
        }

        var rows = ReadUnlabeledJsonLines(dataPath)
            .Where(row => string.IsNullOrWhiteSpace(row.Language)
                || row.Language.Equals(language, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rows.Count < clusterCount)
        {
            throw new InvalidDataException($"Clustering needs at least {clusterCount} {language} text rows; found {rows.Count}.");
        }

        var mlContext = new MLContext(seed: Integer(options, "seed", 42));
        var input = mlContext.Data.LoadFromEnumerable(rows.Select(row => new ClusterInput
        {
            Text = row.Text
        }));
        var pipeline = mlContext.Transforms.Text.FeaturizeText("Features", nameof(ClusterInput.Text))
            .Append(mlContext.Clustering.Trainers.KMeans(new Microsoft.ML.Trainers.KMeansTrainer.Options
            {
                FeatureColumnName = "Features",
                NumberOfClusters = clusterCount,
                NumberOfThreads = 1
            }));
        var model = pipeline.Fit(input);
        var transformed = model.Transform(input);
        var metrics = mlContext.Clustering.Evaluate(
            transformed,
            scoreColumnName: "Score",
            featureColumnName: "Features");
        var assignments = mlContext.Data.CreateEnumerable<ClusterOutput>(transformed, reuseRowObject: false).ToList();
        var clusterStats = assignments
            .Select((assignment, index) => new { assignment.PredictedClusterId, assignment.Score, Row = rows[index] })
            .GroupBy(item => item.PredictedClusterId)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                cluster_id = group.Key,
                rows = group.Count(),
                average_distance = Math.Round(group.Average(item => item.Score?.Length > 0 ? item.Score.Min() : 0f), 4),
                examples = group.Take(3).Select(item => item.Row.Id).ToArray()
            })
            .ToArray();

        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        mlContext.Model.Save(model, input.Schema, modelPath);
        WriteJsonFile(modelPath + ".manifest.json", new
        {
            schema_version = 1,
            task = "exploratory_unlabeled_text_clustering",
            language,
            algorithm = "KMeans",
            featurizer = "ML.NET FeaturizeText",
            seed = Integer(options, "seed", 42),
            threads = 1,
            input_rows = rows.Count,
            clusters = clusterCount,
            document_count = rows.Where(row => !string.IsNullOrWhiteSpace(row.DocumentId))
                .Select(row => row.DocumentId).Distinct(StringComparer.Ordinal).Count(),
            source_groups = rows.Where(row => !string.IsNullOrWhiteSpace(row.SourceGroup))
                .Select(row => row.SourceGroup).Distinct(StringComparer.Ordinal).Order().ToArray(),
            model_sha256 = HashFile(modelPath)
        });
        if (options.TryGetValue("assignments-out", out var assignmentsPath))
        {
            var outputLines = assignments.Select((assignment, index) => JsonSerializer.Serialize(new
            {
                id = rows[index].Id,
                cluster_id = assignment.PredictedClusterId,
                nearest_centroid_distance = Math.Round(assignment.Score?.Length > 0 ? assignment.Score.Min() : 0f, 4)
            }));
            WriteTextFile(assignmentsPath, string.Join(Environment.NewLine, outputLines) + Environment.NewLine);
        }

        WriteOutput(new
        {
            status = "clustered",
            task = "exploratory_unlabeled_text_clustering",
            algorithm = "ML.NET K-Means over FeaturizeText n-gram features",
            language,
            rows = rows.Count,
            document_count = rows.Where(row => !string.IsNullOrWhiteSpace(row.DocumentId))
                .Select(row => row.DocumentId).Distinct(StringComparer.Ordinal).Count(),
            cluster_count = clusterCount,
            trainer_threads = 1,
            metrics = new
            {
                average_distance = Math.Round(metrics.AverageDistance, 4),
                davies_bouldin_index = Math.Round(metrics.DaviesBouldinIndex, 4)
            },
            clusters = clusterStats,
            model_path = modelPath,
            assignments_path = options.TryGetValue("assignments-out", out var outputPath) ? Path.GetFullPath(outputPath) : null,
            interpretation = "Clusters are unlabeled lexical groupings for analyst inspection. Cluster IDs are not TYM labels and are not accuracy results."
        }, options);
        return 0;
    }

    private static int Predict(IReadOnlyDictionary<string, string> options)
    {
        var dataPath = Required(options, "data");
        var modelPath = Path.GetFullPath(Required(options, "model"));
        var rows = ReadUnlabeledJsonLines(dataPath);
        using var predictor = new TextModelPredictor(modelPath);
        var predictions = rows.Select(row => new
        {
            row.Id,
            row.Text,
            predicted_label = predictor.Predict(row.Text).PredictedLabel
        }).ToArray();

        WriteOutput(new
        {
            status = "predicted",
            model_path = modelPath,
            input_rows = predictions.Length,
            predictions
        }, options);
        return 0;
    }

    private static object EvaluateTask(
        MLContext mlContext,
        string task,
        string language,
        IReadOnlyCollection<TrainingExample> trainRows,
        IReadOnlyCollection<TrainingExample> testRows)
    {
        var trainingView = mlContext.Data.LoadFromEnumerable(trainRows.Select(row => new ModelInput
        {
            Text = row.Text,
            Label = row.Label
        }));
        var testView = mlContext.Data.LoadFromEnumerable(testRows.Select(row => new ModelInput
        {
            Text = row.Text,
            Label = row.Label
        }));

        var model = FitModel(mlContext, trainingView);
        var predictions = model.Transform(testView);
        var mlMetrics = mlContext.MulticlassClassification.Evaluate(
            predictions,
            labelColumnName: "LabelKey",
            scoreColumnName: "Score",
            predictedLabelColumnName: "PredictedLabel");

        var scoredRows = mlContext.Data.CreateEnumerable<ScoredOutput>(predictions, reuseRowObject: false).ToList();
        var labels = scoredRows
            .SelectMany(row => new[] { row.Label, row.PredictedLabel })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var confusion = labels.ToDictionary(
            actual => actual,
            _ => labels.ToDictionary(predicted => predicted, _ => 0, StringComparer.Ordinal),
            StringComparer.Ordinal);

        foreach (var row in scoredRows)
        {
            confusion[row.Label][row.PredictedLabel]++;
        }

        var perClass = new Dictionary<string, object>(StringComparer.Ordinal);
        var f1Scores = new List<double>();
        foreach (var label in labels)
        {
            var truePositive = confusion[label][label];
            var support = confusion[label].Values.Sum();
            var predictedCount = labels.Sum(actual => confusion[actual][label]);
            var precision = predictedCount == 0 ? 0.0 : (double)truePositive / predictedCount;
            var recall = support == 0 ? 0.0 : (double)truePositive / support;
            var f1 = precision + recall == 0 ? 0.0 : 2 * precision * recall / (precision + recall);
            f1Scores.Add(f1);
            perClass[label] = new
            {
                precision = Math.Round(precision, 4),
                recall = Math.Round(recall, 4),
                f1 = Math.Round(f1, 4),
                support
            };
        }

        var accuracy = scoredRows.Count == 0
            ? 0.0
            : (double)scoredRows.Count(row => row.Label == row.PredictedLabel) / scoredRows.Count;
        var trainLabels = trainRows.Select(row => row.Label).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var unseenLabels = testRows.Select(row => row.Label).Distinct(StringComparer.Ordinal)
            .Where(label => !trainLabels.Contains(label)).Order(StringComparer.Ordinal).ToArray();

        return new
        {
            task,
            language,
            train_rows = trainRows.Count,
            test_rows = testRows.Count,
            train_labels = trainLabels.Order(StringComparer.Ordinal).ToArray(),
            test_labels_unseen_during_training = unseenLabels,
            metrics = new
            {
                accuracy = Math.Round(accuracy, 4),
                macro_f1 = Math.Round(f1Scores.Count == 0 ? 0.0 : f1Scores.Average(), 4),
                mlnet_micro_accuracy = Math.Round(mlMetrics.MicroAccuracy, 4),
                mlnet_macro_accuracy = Math.Round(mlMetrics.MacroAccuracy, 4),
                mlnet_log_loss = Math.Round(mlMetrics.LogLoss, 4),
                mlnet_log_loss_reduction = Math.Round(mlMetrics.LogLossReduction, 4)
            },
            per_class = perClass,
            confusion_matrix = confusion
        };
    }

    private static ITransformer FitModel(MLContext mlContext, IDataView trainingData)
    {
        var pipeline = mlContext.Transforms.Conversion.MapValueToKey("LabelKey", nameof(ModelInput.Label))
            .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(ModelInput.Text)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel"));
        return pipeline.Fit(trainingData);
    }

    private static SplitManifest CreateManifest(
        IReadOnlyCollection<TrainingExample> rows,
        string groupBy,
        int seed,
        double trainFraction,
        double devFraction,
        double testFraction)
    {
        var fractions = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["train"] = trainFraction,
            ["dev"] = devFraction,
            ["test"] = testFraction
        };
        if (fractions.Values.Any(value => value <= 0)
            || Math.Abs(fractions.Values.Sum() - 1.0) > 0.000001)
        {
            throw new ArgumentException("Train, dev, and test fractions must be positive and sum to 1.");
        }

        var groupedRows = rows
            .GroupBy(row => GroupDigest(row, groupBy), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (groupedRows.Count < 3)
        {
            throw new InvalidDataException("At least three document/chapter groups are required for train/dev/test evaluation.");
        }

        var random = new Random(seed);
        var tieOrder = groupedRows.Keys.Order(StringComparer.Ordinal).ToList();
        for (var index = tieOrder.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (tieOrder[index], tieOrder[swapIndex]) = (tieOrder[swapIndex], tieOrder[index]);
        }
        var randomRank = tieOrder.Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var groupIds = groupedRows.Keys
            .OrderByDescending(id => groupedRows[id].Length)
            .ThenBy(id => randomRank[id])
            .ToList();

        var splitNames = new[] { "train", "dev", "test" };
        var targetRows = fractions.ToDictionary(item => item.Key, item => rows.Count * item.Value, StringComparer.Ordinal);
        var assignedRows = splitNames.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < splitNames.Length; index++)
        {
            var groupId = groupIds[index];
            assignments[groupId] = splitNames[index];
            assignedRows[splitNames[index]] += groupedRows[groupId].Length;
        }

        foreach (var groupId in groupIds.Skip(splitNames.Length))
        {
            var count = groupedRows[groupId].Length;
            var chosen = splitNames.OrderBy(name => splitNames.Sum(split =>
                Math.Pow((assignedRows[split] + (split == name ? count : 0) - targetRows[split])
                    / Math.Max(targetRows[split], 1), 2))).First();
            assignments[groupId] = chosen;
            assignedRows[chosen] += count;
        }

        var groupCounts = assignments.Values.GroupBy(value => value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var rowCounts = rows.GroupBy(row => assignments[GroupDigest(row, groupBy)], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new SplitManifest
        {
            GroupBy = groupBy,
            Seed = seed,
            Fractions = fractions,
            GroupAssignments = assignments,
            Summary = new SplitSummary
            {
                Rows = rows.Count,
                Groups = groupedRows.Count,
                GroupsBySplit = groupCounts,
                RowsBySplit = rowCounts
            }
        };
    }

    private static List<TrainingExample> FilterBySplit(
        IEnumerable<TrainingExample> rows,
        SplitManifest manifest,
        string split)
    {
        if (split is not ("train" or "dev" or "test"))
        {
            throw new ArgumentException("Split must be train, dev, or test.");
        }

        var selected = new List<TrainingExample>();
        foreach (var row in rows)
        {
            if (row.SourceType == "synthetic_seed")
            {
                if (split == "train")
                {
                    selected.Add(row);
                }

                continue;
            }

            var groupId = GroupDigest(row, manifest.GroupBy);
            if (!manifest.GroupAssignments.TryGetValue(groupId, out var assignedSplit))
            {
                throw new InvalidDataException($"No split assignment found for row '{row.Id}'.");
            }

            if (assignedSplit == split)
            {
                selected.Add(row);
            }
        }

        return selected;
    }

    private static void ValidateTrainSeedParents(
        IReadOnlyCollection<TrainingExample> seeds,
        IReadOnlyCollection<TrainingExample> gold,
        SplitManifest manifest)
    {
        var goldById = gold.ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var seed in seeds.Where(row => !string.IsNullOrWhiteSpace(row.ParentId)))
        {
            if (!goldById.TryGetValue(seed.ParentId!, out var parent))
            {
                throw new InvalidDataException($"Seed '{seed.Id}' names unknown parent_id '{seed.ParentId}'.");
            }

            var parentSplit = manifest.GroupAssignments[GroupDigest(parent, manifest.GroupBy)];
            if (parentSplit != "train")
            {
                throw new InvalidDataException(
                    $"Seed '{seed.Id}' descends from a {parentSplit} item. Augmented examples must stay in the training split.");
            }
        }
    }

    private static void ValidateManifestCoverage(IReadOnlyCollection<TrainingExample> rows, SplitManifest manifest)
    {
        foreach (var row in rows)
        {
            var groupId = GroupDigest(row, manifest.GroupBy);
            if (!manifest.GroupAssignments.ContainsKey(groupId))
            {
                throw new InvalidDataException($"Split manifest does not cover row '{row.Id}'. Regenerate the manifest from this gold dataset.");
            }
        }
    }

    private static SplitManifest ReadManifest(string path)
    {
        var manifest = JsonSerializer.Deserialize<SplitManifest>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException($"Split manifest '{path}' is empty.");
        if (manifest.SchemaVersion != 1
            || manifest.GroupBy is not ("chapter" or "document")
            || manifest.GroupAssignments.Count == 0)
        {
            throw new InvalidDataException($"Split manifest '{path}' has an unsupported schema or no group assignments.");
        }

        return manifest;
    }

    private static string GroupDigest(TrainingExample row, string groupBy)
    {
        if (string.IsNullOrWhiteSpace(row.DocumentId))
        {
            throw new InvalidDataException($"Gold row '{row.Id}' must include document_id.");
        }

        string[] values;
        if (groupBy == "document")
        {
            values = [row.DocumentId];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(row.ChapterId))
            {
                throw new InvalidDataException($"Gold row '{row.Id}' must include chapter_id for chapter grouping.");
            }

            values = [row.DocumentId, row.ChapterId];
        }

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static List<TrainingExample> ReadJsonLines(string path)
    {
        var examples = new List<TrainingExample>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var example = JsonSerializer.Deserialize<TrainingExample>(line, JsonOptions)
                ?? throw new InvalidDataException($"{path}:{lineNumber}: empty JSON object.");
            var goldLabel = example.GoldLabel;
            if (string.IsNullOrWhiteSpace(example.Label) && !string.IsNullOrWhiteSpace(goldLabel))
            {
                example = example with { Label = goldLabel };
            }

            if (string.IsNullOrWhiteSpace(example.Id)
                || string.IsNullOrWhiteSpace(example.Task)
                || string.IsNullOrWhiteSpace(example.Text)
                || string.IsNullOrWhiteSpace(example.Label)
                || string.IsNullOrWhiteSpace(example.Language)
                || string.IsNullOrWhiteSpace(example.SourceType)
                || string.IsNullOrWhiteSpace(example.ReviewStatus))
            {
                throw new InvalidDataException($"{path}:{lineNumber}: missing required training fields.");
            }

            if (!ids.Add(example.Id))
            {
                throw new InvalidDataException($"{path}:{lineNumber}: duplicate id '{example.Id}'.");
            }

            examples.Add(example);
        }

        if (examples.Count == 0)
        {
            throw new InvalidDataException($"No JSONL records were found in '{path}'.");
        }

        return examples;
    }

    private static List<UnlabeledTextExample> ReadUnlabeledJsonLines(string path)
    {
        var rows = new List<UnlabeledTextExample>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var row = JsonSerializer.Deserialize<UnlabeledTextExample>(line, JsonOptions)
                ?? throw new InvalidDataException($"{path}:{lineNumber}: empty JSON object.");
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Text))
            {
                throw new InvalidDataException($"{path}:{lineNumber}: id and text are required for text input.");
            }

            if (!ids.Add(row.Id))
            {
                throw new InvalidDataException($"{path}:{lineNumber}: duplicate id '{row.Id}'.");
            }

            rows.Add(row);
        }

        return rows.Count > 0
            ? rows
            : throw new InvalidDataException($"No JSONL records were found in '{path}'.");
    }

    private static void ValidateGoldRows(IEnumerable<TrainingExample> rows)
    {
        foreach (var row in rows)
        {
            if (row.SourceType != "human_gold" || row.ReviewStatus != "adjudicated")
            {
                throw new InvalidDataException(
                    $"Evaluation accepts only human_gold rows marked adjudicated. Row '{row.Id}' is {row.SourceType}/{row.ReviewStatus}.");
            }
        }
    }

    private static void ValidateTrainingRows(IEnumerable<TrainingExample> rows)
    {
        foreach (var row in rows)
        {
            var isGold = row.SourceType == "human_gold" && row.ReviewStatus == "adjudicated";
            var isSeed = row.SourceType == "synthetic_seed" && row.ReviewStatus == "unreviewed";
            var isProvided = row.SourceType == "provided_annotation" && row.ReviewStatus == "adjudication_unknown";
            if (!isGold && !isSeed && !isProvided)
            {
                throw new InvalidDataException(
                    $"Training accepts adjudicated human_gold, explicitly marked synthetic_seed, or provided_annotation/adjudication_unknown rows. Row '{row.Id}' is {row.SourceType}/{row.ReviewStatus}.");
            }
        }
    }

    private static void EnsureTrainable(IReadOnlyCollection<TrainingExample> rows, string task, string language)
    {
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"No training rows for task '{task}' and language '{language}'.");
        }

        var labels = rows.Select(row => row.Label).Distinct(StringComparer.Ordinal).ToArray();
        if (labels.Length < 2)
        {
            throw new InvalidDataException($"Task '{task}' needs at least two training labels; found {labels.Length}.");
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException("Options must use --name value pairs.");
            }

            var key = args[index][2..];
            if (!options.TryAdd(key, args[index + 1]))
            {
                throw new ArgumentException($"Option --{key} was supplied more than once.");
            }
        }

        return options;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option --{name}.");
        }

        return value;
    }

    private static string Optional(IReadOnlyDictionary<string, string> options, string name, string fallback) =>
        options.TryGetValue(name, out var value) ? value : fallback;

    private static int Integer(IReadOnlyDictionary<string, string> options, string name, int fallback) =>
        options.TryGetValue(name, out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static double Number(IReadOnlyDictionary<string, string> options, string name, double fallback) =>
        options.TryGetValue(name, out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static SortedDictionary<string, int> CountOptional(IEnumerable<string?> values)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()))
        {
            counts[value] = counts.TryGetValue(value, out var count) ? count + 1 : 1;
        }

        return counts;
    }

    private static void WriteOutput(object value, IReadOnlyDictionary<string, string> options)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine;
        if (options.TryGetValue("report", out var reportPath))
        {
            WriteTextFile(reportPath, json);
        }
        else
        {
            Console.Write(json);
        }
    }

    private static void WriteJsonFile<T>(string path, T value) =>
        WriteTextFile(path, JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine);

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void WriteTextFile(string path, string text)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, text, Encoding.UTF8);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TYM ML.NET modeling tool");
        Console.WriteLine();
        Console.WriteLine("Train a model from labelled JSONL rows:");
        Console.WriteLine("  dotnet run --project tools/Tym.Modeling -- train --data data/seed-examples.jsonl --task segment_type --language en --model-out models/segment_type_en.zip");
        Console.WriteLine();
        Console.WriteLine("Evaluate on chapter- or document-held-out human gold:");
        Console.WriteLine("  dotnet run --project tools/Tym.Modeling -- evaluate --data C:/data/tym-gold.jsonl --group-by chapter --language en --splits-out C:/data/tym-splits.json --report C:/data/tym-metrics.json");
        Console.WriteLine();
        Console.WriteLine("Evaluation requires human_gold/adjudicated rows. Optional synthetic rows are train-only; provided_annotation/adjudication_unknown rows are accepted only by the training command.");
        Console.WriteLine();
        Console.WriteLine("Explore unlabeled text with ML.NET K-Means (cluster IDs are not TYM labels):");
        Console.WriteLine("  dotnet run --project tools/Tym.Modeling -- cluster --data C:/data/unlabeled.jsonl --clusters 8 --model-out models/unlabeled_clusters.zip --assignments-out C:/data/clusters.jsonl");
        Console.WriteLine();
        Console.WriteLine("Predict labels for free text with a trained model:");
        Console.WriteLine("  dotnet run --project tools/Tym.Modeling -- predict --model C:/private/models/segment_type_en_sdca.zip --data C:/data/free-texts.jsonl");
    }
}

internal sealed record TrainingExample(
    string Id,
    string Task,
    string Text,
    string Label,
    string Language,
    [property: JsonPropertyName("source_type")] string SourceType,
    [property: JsonPropertyName("source_group")] string SourceGroup,
    [property: JsonPropertyName("review_status")] string ReviewStatus,
    [property: JsonPropertyName("parent_id")] string? ParentId,
    [property: JsonPropertyName("document_id")] string? DocumentId,
    [property: JsonPropertyName("chapter_id")] string? ChapterId,
    [property: JsonPropertyName("gold_label")] string? GoldLabel = null,
    [property: JsonPropertyName("phenomenon")] string? Phenomenon = null,
    [property: JsonPropertyName("transformation")] string? Transformation = null,
    [property: JsonPropertyName("generator_version")] string? GeneratorVersion = null);

internal sealed class ModelInput
{
    public string Text { get; set; } = "";
    public string Label { get; set; } = "";
}

internal sealed record UnlabeledTextExample(
    string Id,
    string Text,
    string? Language = null,
    [property: JsonPropertyName("source_group")] string? SourceGroup = null,
    [property: JsonPropertyName("document_id")] string? DocumentId = null,
    [property: JsonPropertyName("chapter_id")] string? ChapterId = null);

internal sealed class ClusterInput
{
    public string Text { get; set; } = "";
}

internal sealed class ClusterOutput
{
    [ColumnName("PredictedLabel")]
    public uint PredictedClusterId { get; set; }

    [ColumnName("Score")]
    public float[]? Score { get; set; }
}

internal sealed class ScoredOutput
{
    public string Label { get; set; } = "";

    [ColumnName("PredictedLabelText")]
    public string PredictedLabel { get; set; } = "";
}

internal sealed class SplitManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("group_by")]
    public string GroupBy { get; init; } = "chapter";

    public int Seed { get; init; }
    public IReadOnlyDictionary<string, double> Fractions { get; init; } = new Dictionary<string, double>();

    [JsonPropertyName("group_assignments")]
    public IReadOnlyDictionary<string, string> GroupAssignments { get; init; } = new Dictionary<string, string>();

    public SplitSummary Summary { get; init; } = new();
}

internal sealed class SplitSummary
{
    public int Rows { get; init; }
    public int Groups { get; init; }
    public IReadOnlyDictionary<string, int> GroupsBySplit { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> RowsBySplit { get; init; } = new Dictionary<string, int>();
}
