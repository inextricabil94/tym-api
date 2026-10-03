using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using Tym.Corpus.Data;

namespace Tym.Benchmark;

/// <summary>
/// Unlabeled book exploration. Text vocabulary, normalization, PCA, and centroids are fitted on
/// training passages only. Aggregate reports contain no prose or absolute machine paths.
/// </summary>
public static class BookExploration
{
    private const int MaximumSample = 256;
    private const double DbscanEpsilon = 0.45;
    private const int DbscanMinimumPoints = 5;

    /// <summary>
    /// Compares ML.NET KMeans with PCA+KMeans and a TorchSharp numeric autoencoder on a
    /// reproducible holdout, then runs bounded average-linkage and DBSCAN exploration.
    /// No supervised accuracy can be computed because book passages have no labels.
    /// </summary>
    public static void Run(string inputPath, string outputPath, string modelDirectory, int seed,
        int clusters = 8, int rank = 32)
    {
        if (clusters is < 2 or > 64 || rank is < 2 or > 256)
            throw new ArgumentException("Book exploration requires 2-64 clusters and PCA rank 2-256.");
        var input = Path.GetFullPath(inputPath);
        var output = Path.GetFullPath(outputPath);
        var modelRoot = Path.GetFullPath(modelDirectory);
        if (StringComparer.OrdinalIgnoreCase.Equals(input, output) || File.Exists(output))
            throw new ArgumentException("The aggregate output must be a new file distinct from the input.");
        var rows = ReadRows(input);
        var language = rows[0].Language;
        var names = new[] { $"books_{language}_kmeans_holdout.zip", $"books_{language}_pca_kmeans_holdout.zip",
            $"books_{language}_hierarchical_sample.json", $"books_{language}_dbscan_sample.json" };
        foreach (var name in names)
        {
            var path = Path.Combine(modelRoot, name);
            if (File.Exists(path) || File.Exists(path + ".manifest.json")
                || StringComparer.OrdinalIgnoreCase.Equals(path, output))
                throw new ArgumentException("A book exploration artifact already exists; choose a new output directory.");
        }
        var watch = Stopwatch.StartNew();
        using var process = Process.GetCurrentProcess();
        object SpaceSnapshot(string? artifact = null, long? parameterCount = null)
        {
            process.Refresh();
            return new
            {
                process_working_set_mb = process.WorkingSet64 / 1048576.0,
                process_lifetime_peak_working_set_mb = process.PeakWorkingSet64 / 1048576.0,
                artifact_bytes = artifact is null ? (long?)null : new FileInfo(artifact).Length,
                neural_parameter_count = parameterCount,
                float32_parameter_bytes = parameterCount is null ? (long?)null : checked(parameterCount.Value * 4),
                scope = "Shared process snapshot after assessment; lifetime peak includes previous pipelines/native state. Artifact bytes are compressed ML.NET pipelines or transductive sample JSON; parameter bytes exclude gradients, optimizer and activations."
            };
        }
        var split = CreateSplit(rows, seed);
        var training = rows.Where((_, index) => !split.Heldout[index]).ToArray();
        var heldout = rows.Where((_, index) => split.Heldout[index]).ToArray();
        if (training.Length < Math.Max(clusters, 3) || heldout.Length < 2)
            throw new InvalidDataException("Too few training or heldout passages for this clustering configuration.");

        var context = new MLContext(seed: seed);
        var trainView = context.Data.LoadFromEnumerable(training.Select(row => new BookInput { Text = row.Text }));
        var testView = context.Data.LoadFromEnumerable(heldout.Select(row => new BookInput { Text = row.Text }));
        var commonWatch = Stopwatch.StartNew();
        var common = context.Transforms.Text.FeaturizeText("RawTextFeatures", nameof(BookInput.Text))
            .Append(context.Transforms.NormalizeLpNorm("TextFeatures", "RawTextFeatures")).Fit(trainView);
        var commonSeconds = commonWatch.Elapsed.TotalSeconds;
        var fittedTrain = common.Transform(trainView);
        var featureDimensions = ((VectorDataViewType)fittedTrain.Schema["TextFeatures"].Type).Size;
        var effectiveRank = Math.Min(rank, Math.Min(featureDimensions, training.Length - 1));
        if (effectiveRank < 2) throw new InvalidDataException("The fitted training vocabulary cannot support PCA rank two.");
        Directory.CreateDirectory(modelRoot);
        var inputHash = CorpusFiles.FileHash(input);
        var algorithms = new List<object>();
        ITransformer? pcaFeatures = null;
        double pcaSeconds = 0;
        foreach (var usePca in new[] { false, true })
        {
            var fitWatch = Stopwatch.StartNew();
            ITransformer representation;
            if (usePca)
            {
                var projectionWatch = Stopwatch.StartNew();
                pcaFeatures = context.Transforms.ProjectToPrincipalComponents("PcaFeatures", "TextFeatures",
                    rank: effectiveRank, overSampling: Math.Min(8, featureDimensions - effectiveRank),
                    ensureZeroMean: true, seed: seed)
                    .Append(context.Transforms.NormalizeLpNorm("Features", "PcaFeatures")).Fit(fittedTrain);
                pcaSeconds = projectionWatch.Elapsed.TotalSeconds;
                representation = common.Append(pcaFeatures);
            }
            else representation = common.Append(context.Transforms.CopyColumns("Features", "TextFeatures").Fit(fittedTrain));
            var clusterModel = context.Clustering.Trainers.KMeans(new KMeansTrainer.Options
            {
                FeatureColumnName = "Features", NumberOfClusters = clusters, NumberOfThreads = 1,
                MaximumNumberOfIterations = 100
            }).Fit(representation.Transform(trainView));
            var model = representation.Append(clusterModel);
            var fitSeconds = fitWatch.Elapsed.TotalSeconds;
            var inferenceWatch = Stopwatch.StartNew();
            var trainScored = model.Transform(trainView);
            var testScored = model.Transform(testView);
            var trainingMetrics = Summarize(context, trainScored, training.Length);
            var holdoutMetrics = Summarize(context, testScored, heldout.Length);
            var inferenceSeconds = inferenceWatch.Elapsed.TotalSeconds;
            var filename = names[usePca ? 1 : 0];
            var modelPath = Path.Combine(modelRoot, filename);
            context.Model.Save(model, trainView.Schema, modelPath);
            var algorithm = usePca ? "mlnet_pca_kmeans" : "mlnet_kmeans";
            CorpusFiles.WriteJson(modelPath + ".manifest.json", new
            {
                schema_version = 1, task = "exploratory_unlabeled_book_clustering", language, algorithm,
                source_type = "unlabeled_user_provided", input_sha256 = inputHash,
                model_sha256 = CorpusFiles.FileHash(modelPath), seed, clusters, fitted_rows = training.Length,
                heldout_rows = heldout.Length, split = split.Kind, features = usePca ? effectiveRank : featureDimensions,
                interpretation = "Training-only exploratory weights; cluster IDs are not temporal or semantic labels."
            });
            algorithms.Add(new
            {
                algorithm, implementation = "ML.NET 5.0.0", model_file = filename,
                model_sha256 = CorpusFiles.FileHash(modelPath), feature_dimensions = usePca ? effectiveRank : featureDimensions,
                clusters, threads = 1, maximum_iterations = 100,
                fit_seconds_including_shared_featurizer = commonSeconds + fitSeconds,
                fit_seconds_after_shared_featurizer = fitSeconds, assessment_seconds = inferenceSeconds,
                assessment_rows = training.Length + heldout.Length,
                training_metrics = trainingMetrics, heldout_metrics = holdoutMetrics,
                semantic_accuracy = (double?)null, space = SpaceSnapshot(modelPath),
                accuracy_status = "unavailable_unlabeled_passages"
            });
            Console.WriteLine($"Book {language} {algorithm}: {training.Length} fit, {heldout.Length} heldout; {fitSeconds:F2}s after shared features.");
        }

        // A genuine C# neural reconstruction experiment consumes the same training-fitted PCA
        // representation, never validation-derived vocabulary, basis, scaling, or stopping rules.
        var pcaRepresentation = common.Append(pcaFeatures!);
        var trainProjectionWatch = Stopwatch.StartNew();
        var autoencoderTrain = context.Data.CreateEnumerable<BookVector>(pcaRepresentation.Transform(trainView),
            reuseRowObject: false).Select(row => row.Features).ToArray();
        var trainingProjectionSeconds = trainProjectionWatch.Elapsed.TotalSeconds;
        var holdoutProjectionWatch = Stopwatch.StartNew();
        var autoencoderHoldout = context.Data.CreateEnumerable<BookVector>(pcaRepresentation.Transform(testView),
            reuseRowObject: false).Select(row => row.Features).ToArray();
        var heldoutProjectionSeconds = holdoutProjectionWatch.Elapsed.TotalSeconds;
        if (autoencoderTrain.Length != training.Length || autoencoderHoldout.Length != heldout.Length)
            throw new InvalidDataException("Autoencoder feature row count does not match the shared split.");
        var autoencoder = Tym.NeuralBenchmark.NeuralAlgorithms.FitAutoencoder(autoencoderTrain, autoencoderHoldout, seed, epochs: 5);
        var trainingMean = Enumerable.Range(0, effectiveRank)
            .Select(column => autoencoderTrain.Average(vector => (double)vector[column])).ToArray();
        var meanReconstructionError = autoencoderHoldout.Sum(vector => Enumerable.Range(0, effectiveRank)
            .Sum(column => Math.Pow(vector[column] - trainingMean[column], 2))) / (autoencoderHoldout.Length * (double)effectiveRank);
        var zeroReconstructionError = autoencoderHoldout.Sum(vector => vector.Sum(value => (double)value * value))
            / (autoencoderHoldout.Length * (double)effectiveRank);
        algorithms.Add(new
        {
            algorithm = "csharp_torchsharp_autoencoder", implementation = "TorchSharp CPU neural model in C#",
            representation = "Training-fitted ML.NET text features -> L2 -> centered PCA -> L2; no prose decoder",
            feature_dimensions = autoencoder.InputDimensions, bottleneck_dimensions = autoencoder.BottleneckDimensions,
            space = SpaceSnapshot(parameterCount: autoencoder.ModelParameters),
            epochs = 5, training_rows = training.Length, heldout_rows = heldout.Length,
            model_parameters = autoencoder.ModelParameters, model_exported = false,
            shared_text_featurizer_fit_seconds = commonSeconds, shared_pca_fit_seconds = pcaSeconds,
            training_feature_transform_seconds = trainingProjectionSeconds,
            heldout_feature_transform_seconds = heldoutProjectionSeconds,
            fit_seconds_algorithm_only = autoencoder.TrainingSeconds,
            fit_seconds_including_shared_representation = commonSeconds + pcaSeconds + trainingProjectionSeconds + autoencoder.TrainingSeconds,
            prediction_seconds_algorithm_only = autoencoder.PredictionSeconds,
            prediction_seconds_including_heldout_transform = heldoutProjectionSeconds + autoencoder.PredictionSeconds,
            sampled_peak_process_working_set_bytes = autoencoder.SampledPeakWorkingSetBytes,
            last_epoch_training_mean_squared_error = autoencoder.LastEpochTrainingMeanSquaredError,
            heldout_reconstruction_mean_squared_error = autoencoder.ValidationMeanSquaredError,
            heldout_training_mean_reconstruction_mean_squared_error = meanReconstructionError,
            heldout_zero_reconstruction_mean_squared_error = zeroReconstructionError,
            semantic_accuracy = (double?)null, accuracy_status = "unavailable_unlabeled_numeric_reconstruction",
            metadata = autoencoder.Metadata
        });
        Console.WriteLine($"Book {language} autoencoder: {training.Length} fit, {heldout.Length} heldout; "
            + $"{autoencoder.InputDimensions}->{autoencoder.BottleneckDimensions} dimensions, {autoencoder.TrainingSeconds:F2}s fit.");

        // These algorithms have no fitted inductive classifier. Report the bounded sample's
        // structure directly, without describing resubstitution summaries as heldout performance.
        var sample = training.OrderBy(row => CorpusFiles.Hash(seed + "|sample|" + row.Id), StringComparer.Ordinal)
            .Take(MaximumSample).ToArray();
        var sampleView = context.Data.LoadFromEnumerable(sample.Select(row => new BookInput { Text = row.Text }));
        var sampleProjectionWatch = Stopwatch.StartNew();
        var vectors = context.Data.CreateEnumerable<BookVector>(pcaRepresentation
            .Transform(sampleView), reuseRowObject: false).Select(row => row.Features).ToArray();
        if (vectors.Length != sample.Length || vectors.Any(vector => vector.Length != effectiveRank
            || vector.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Invalid training sample PCA vectors.");
        var sampleProjectionSeconds = sampleProjectionWatch.Elapsed.TotalSeconds;
        foreach (var densityBased in new[] { false, true })
        {
            var sampleWatch = Stopwatch.StartNew();
            var assignments = densityBased ? Dbscan(vectors, DbscanEpsilon, DbscanMinimumPoints) : Hierarchical(vectors, clusters);
            var sampleFitSeconds = sampleWatch.Elapsed.TotalSeconds;
            var assessmentWatch = Stopwatch.StartNew();
            var sampleMetrics = SampleMetrics(vectors, assignments);
            var assessmentSeconds = assessmentWatch.Elapsed.TotalSeconds;
            var filename = names[densityBased ? 3 : 2];
            var artifactPath = Path.Combine(modelRoot, filename);
            CorpusFiles.WriteJson(artifactPath, new
            {
                schema_version = 1, feature_space = "training_fitted_pca_l2_normalized", pca_model_file = names[1],
                sample_ids_sha256 = CorpusFiles.Hash(string.Join('\n', sample.Select(row => row.Id))),
                points = vectors.Select((vector, index) => new { features = vector, cluster = assignments[index] }).ToArray(),
                algorithm = densityBased ? "dbscan" : "average_linkage_hierarchical",
                epsilon = densityBased ? DbscanEpsilon : (double?)null,
                minimum_points_including_self = densityBased ? DbscanMinimumPoints : (int?)null,
                requested_clusters = densityBased ? (int?)null : clusters,
                status = "private_transductive_training_sample_artifact_not_a_document_label_predictor"
            });
            algorithms.Add(new
            {
                algorithm = densityBased ? "csharp_dbscan" : "csharp_average_linkage_hierarchical",
                implementation = "Documented bounded C# algorithm over ML.NET PCA vectors",
                model_file = filename, model_sha256 = CorpusFiles.FileHash(artifactPath),
                feature_dimensions = effectiveRank, sample_rows = sample.Length, sample_limit = MaximumSample,
                fit_seconds_algorithm_only = sampleFitSeconds,
                shared_text_featurizer_fit_seconds = commonSeconds, shared_pca_fit_seconds = pcaSeconds,
                sample_feature_transform_seconds = sampleProjectionSeconds,
                assessment_seconds = assessmentSeconds, sample_metrics = sampleMetrics,
                epsilon = densityBased ? DbscanEpsilon : (double?)null,
                minimum_points_including_self = densityBased ? DbscanMinimumPoints : (int?)null,
                requested_clusters = densityBased ? (int?)null : clusters,
                heldout_metrics = (object?)null, semantic_accuracy = (double?)null, space = SpaceSnapshot(artifactPath),
                accuracy_status = "unavailable_unlabeled_transductive_training_sample",
                sampling = "At most 256 training passages ranked by SHA256(seed|sample|id); no heldout input used."
            });
            Console.WriteLine($"Book {language} {(densityBased ? "DBSCAN" : "hierarchical")}: {sample.Length} training-sample rows; {sampleFitSeconds:F2}s.");
        }
        CorpusFiles.WriteJson(output, new
        {
            schema_version = 1, status = "exploratory_unlabeled_books", completed_utc = DateTimeOffset.UtcNow,
            framework = "ML.NET 5.0.0 / .NET 10; TorchSharp CPU autoencoder; bounded custom C# hierarchical and DBSCAN",
            input_sha256 = inputHash, language, seed, total_rows = rows.Length,
            documents = rows.Select(DocumentKey).Distinct(StringComparer.Ordinal).Count(),
            source_groups = rows.Select(row => row.SourceGroup).Distinct(StringComparer.Ordinal).Count(),
            training_rows = training.Length, heldout_rows = heldout.Length,
            training_documents = training.Select(DocumentKey).Distinct(StringComparer.Ordinal).Count(),
            heldout_documents = heldout.Select(DocumentKey).Distinct(StringComparer.Ordinal).Count(),
            split_kind = split.Kind, independent_document_components = split.Components,
            cross_document_normalized_duplicates = split.CrossDocumentDuplicates,
            split_assignment_sha256 = CorpusFiles.Hash(string.Join('\n', rows.Select((row, index) => row.Id + "|" + split.Heldout[index]))),
            requested_pca_rank = rank, effective_pca_rank = effectiveRank,
            common_text_featurizer_fit_seconds = commonSeconds, seconds = watch.Elapsed.TotalSeconds,
            algorithms,
            limitations = new[]
            {
                "Book passages are unlabeled. Accuracy, F1, temporal extraction accuracy, and semantic cluster correctness are unavailable.",
                "KMeans and PCA+KMeans use identical splits. Vocabulary, PCA basis, and centroids fit training rows only; no heldout passages influence fitting.",
                "With multiple supplied document components, complete documents are held out; exact NFC/whitespace-normalized duplicate inputs connect documents before splitting.",
                "With only one independent document component, normalized passage groups are held out within that source work. This does not measure generalization to other books.",
                "Supplied document IDs may represent editions rather than independently authored works; normalized duplicates do not detect paraphrases or unknown shared provenance.",
                "Distance and Davies-Bouldin values depend on the feature space; raw-text and PCA geometry cannot establish that one representation is semantically better.",
                "Hierarchical and DBSCAN operate on at most 256 deterministic training-only PCA vectors. Their silhouette and noise summaries describe this sample, not heldout classification.",
                "The scratch-trained TorchSharp autoencoder reconstructs normalized PCA numeric vectors, not the original prose. Its heldout reconstruction MSE is neither semantic accuracy nor a clustering score.",
                "Reconstruction is contextualized by constant-zero and training-mean numeric baselines on the same heldout vectors; the heldout inputs do not fit either baseline.",
                "Autoencoder upstream vocabulary and PCA fit training passages only; fixed five epochs have no validation-based early stopping. Actual input/bottleneck dimensions are reported; these experimental weights are not exported or deployed.",
                "Autoencoder memory is the maximum process working-set sample at batch boundaries, including previously loaded pipeline/native state; it is not isolated model memory.",
                "DBSCAN epsilon 0.45 and minPoints 5 and hierarchical target cluster count are fixed exploratory settings, not tuned or selected against a semantic gold set.",
                "Cluster identifiers are arbitrary and have no mapping to TYM narrative, time, location, or relation labels.",
                "Recorded times are one-process wall-clock measurements on the current host; shared representation time is recorded explicitly and is not a hardware-normalized speed ranking."
            }
        });
    }

    private static BookRow[] ReadRows(string path)
    {
        if (new FileInfo(path).Length > CorpusFiles.MaximumArchiveBytes)
            throw new InvalidDataException("Book JSONL exceeds the 100 MiB input limit.");
        var rows = new List<BookRow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 100000) throw new InvalidDataException("Book JSONL contains an oversized row.");
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || document.RootElement.TryGetProperty("label", out _) || document.RootElement.TryGetProperty("task", out _))
                throw new InvalidDataException("Expected unlabeled BookRow JSONL without label or task fields.");
            var row = JsonSerializer.Deserialize<BookRow>(line, CorpusFiles.Json)
                ?? throw new InvalidDataException("Empty book row.");
            if (string.IsNullOrWhiteSpace(row.Id) || string.IsNullOrWhiteSpace(row.Text) || row.Text.Length > 50000
                || row.Language is not ("en" or "ro") || row.SourceType != "unlabeled_user_provided"
                || string.IsNullOrWhiteSpace(row.SourceGroup) || string.IsNullOrWhiteSpace(row.DocumentId)
                || string.IsNullOrWhiteSpace(row.ChapterId) || !ids.Add(row.Id))
                throw new InvalidDataException("Expected unique unlabeled book rows with document/chapter provenance and en/ro language.");
            rows.Add(row);
        }
        if (rows.Count == 0 || rows.Select(row => row.Language).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("Book exploration requires nonempty input containing exactly one language.");
        return rows.ToArray();
    }

    private static string DocumentKey(BookRow row) => row.SourceGroup + "|" + row.DocumentId;
    private static string PassageKey(BookRow row) => CorpusFiles.Hash(CorpusFiles.NormalizeSpaces(row.Text));

    private static BookSplit CreateSplit(BookRow[] rows, int seed)
    {
        var parents = rows.Select(DocumentKey).Distinct(StringComparer.Ordinal).ToDictionary(key => key, key => key, StringComparer.Ordinal);
        string Find(string key)
        {
            var root = key;
            while (parents[root] != root) root = parents[root];
            while (parents[key] != key) { var next = parents[key]; parents[key] = root; key = next; }
            return root;
        }
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var row in rows)
        {
            var key = PassageKey(row);
            var document = DocumentKey(row);
            if (seen.TryGetValue(key, out var previous) && previous != document)
            {
                var left = Find(document); var right = Find(previous);
                if (left != right) parents[string.CompareOrdinal(left, right) < 0 ? right : left] = string.CompareOrdinal(left, right) < 0 ? left : right;
                duplicates++;
            }
            else seen[key] = document;
        }
        var components = rows.Select(row => Find(DocumentKey(row))).Distinct(StringComparer.Ordinal).Count();
        var keys = rows.Select(row => components > 1 ? Find(DocumentKey(row)) : PassageKey(row)).ToArray();
        var groups = keys.GroupBy(key => key).Select(group => new { Key = group.Key, Rows = group.Count(), Order = CorpusFiles.Hash(seed + "|split|" + group.Key) })
            .OrderBy(group => group.Order, StringComparer.Ordinal).ToArray();
        if (groups.Length < 2) throw new InvalidDataException("No independent document or normalized-passage groups are available for a holdout.");
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var selectedRows = 0;
        foreach (var group in groups.Take(groups.Length - 1))
        {
            selected.Add(group.Key); selectedRows += group.Rows;
            if (selectedRows >= Math.Ceiling(rows.Length * 0.2)) break;
        }
        var heldout = keys.Select(selected.Contains).ToArray();
        if (rows.Select((row, index) => (Key: PassageKey(row), Heldout: heldout[index])).GroupBy(row => row.Key)
            .Any(group => group.Select(row => row.Heldout).Distinct().Count() > 1))
            throw new InvalidDataException("A normalized duplicate passage crossed the holdout boundary.");
        return new(heldout, components > 1 ? "complete_document_component_holdout" : "within_source_work_normalized_passage_holdout", components, duplicates);
    }

    private static object Summarize(MLContext context, IDataView scored, int expected)
    {
        var predictions = context.Data.CreateEnumerable<BookPrediction>(scored, reuseRowObject: false).ToArray();
        if (predictions.Length != expected || predictions.Any(row => row.Score.Length == 0 || row.Score.Any(value => !float.IsFinite(value))))
            throw new InvalidDataException("Invalid clustering prediction count or nonfinite scores.");
        var metrics = context.Clustering.Evaluate(scored, scoreColumnName: "Score", featureColumnName: "Features");
        return new
        {
            rows = predictions.Length,
            average_squared_centroid_distance = Finite(metrics.AverageDistance),
            davies_bouldin_index = Finite(metrics.DaviesBouldinIndex),
            davies_bouldin_status = double.IsFinite(metrics.DaviesBouldinIndex) ? "computed" : "undefined_degenerate_clusters",
            cluster_counts = predictions.GroupBy(row => row.PredictedLabel).OrderBy(group => group.Key)
                .Select(group => new { cluster = group.Key, rows = group.Count() }).ToArray()
        };
    }

    /// <summary>Average-linkage agglomeration; cached distances use cluster-cardinality weighted updates.</summary>
    private static int[] Hierarchical(float[][] points, int clusters)
    {
        var count = points.Length;
        var distances = new double[count, count];
        var active = Enumerable.Repeat(true, count).ToArray();
        var sizes = Enumerable.Repeat(1, count).ToArray();
        var members = Enumerable.Range(0, count).Select(index => new List<int> { index }).ToArray();
        for (var left = 0; left < count; left++)
            for (var right = left + 1; right < count; right++) distances[left, right] = distances[right, left] = Distance(points[left], points[right]);
        for (var remaining = count; remaining > Math.Min(clusters, count); remaining--)
        {
            var best = double.PositiveInfinity; var mergeLeft = -1; var mergeRight = -1;
            for (var left = 0; left < count; left++) if (active[left])
                for (var right = left + 1; right < count; right++) if (active[right] && distances[left, right] < best)
                { best = distances[left, right]; mergeLeft = left; mergeRight = right; }
            if (mergeLeft < 0) throw new InvalidDataException("No finite hierarchical merge is available.");
            for (var other = 0; other < count; other++) if (active[other] && other != mergeLeft && other != mergeRight)
                distances[mergeLeft, other] = distances[other, mergeLeft] = (sizes[mergeLeft] * distances[mergeLeft, other]
                    + sizes[mergeRight] * distances[mergeRight, other]) / (sizes[mergeLeft] + sizes[mergeRight]);
            sizes[mergeLeft] += sizes[mergeRight]; members[mergeLeft].AddRange(members[mergeRight]); active[mergeRight] = false;
        }
        var labels = new int[count]; var cluster = 0;
        for (var index = 0; index < count; index++) if (active[index])
        { cluster++; foreach (var member in members[index]) labels[member] = cluster; }
        return labels;
    }

    /// <summary>Euclidean DBSCAN with inclusive epsilon neighborhoods and minPoints including the point itself.</summary>
    private static int[] Dbscan(float[][] points, double epsilon, int minimumPoints)
    {
        var labels = new int[points.Length]; var visited = new bool[points.Length]; var cluster = 0;
        int[] Neighbors(int point) => Enumerable.Range(0, points.Length).Where(index => Distance(points[point], points[index]) <= epsilon).ToArray();
        for (var point = 0; point < points.Length; point++)
        {
            if (visited[point]) continue;
            visited[point] = true;
            var neighbors = Neighbors(point);
            if (neighbors.Length < minimumPoints) { labels[point] = -1; continue; }
            cluster++; labels[point] = cluster;
            var queue = new Queue<int>(neighbors); var queued = neighbors.ToHashSet();
            while (queue.TryDequeue(out var neighbor))
            {
                if (!visited[neighbor])
                {
                    visited[neighbor] = true;
                    var expansion = Neighbors(neighbor);
                    if (expansion.Length >= minimumPoints)
                        foreach (var candidate in expansion) if (queued.Add(candidate)) queue.Enqueue(candidate);
                }
                if (labels[neighbor] <= 0) labels[neighbor] = cluster;
            }
        }
        return labels;
    }

    private static object SampleMetrics(float[][] points, int[] labels)
    {
        var groups = Enumerable.Range(0, points.Length).Where(index => labels[index] > 0).GroupBy(index => labels[index])
            .ToDictionary(group => group.Key, group => group.ToArray());
        var usable = groups.Values.SelectMany(group => group).ToArray();
        var silhouettes = new List<double>();
        if (groups.Count >= 2 && usable.Length > groups.Count)
            foreach (var index in usable)
            {
                var own = groups[labels[index]];
                if (own.Length == 1) { silhouettes.Add(0); continue; }
                var a = own.Where(other => other != index).Average(other => Distance(points[index], points[other]));
                var b = groups.Where(group => group.Key != labels[index]).Min(group => group.Value.Average(other => Distance(points[index], points[other])));
                silhouettes.Add(Math.Max(a, b) > 0 ? (b - a) / Math.Max(a, b) : 0);
            }
        return new
        {
            rows = points.Length, clusters = groups.Count, noise_rows = labels.Count(label => label < 0),
            noise_fraction = labels.Count(label => label < 0) / (double)labels.Length,
            silhouette_excluding_noise = silhouettes.Count > 0 ? silhouettes.Average() : (double?)null,
            silhouette_status = silhouettes.Count > 0 ? "computed_training_sample_only" : "undefined_fewer_than_two_nontrivial_clusters",
            cluster_counts = groups.OrderBy(group => group.Key).Select(group => new { cluster = group.Key, rows = group.Value.Length }).ToArray()
        };
    }

    private static double Distance(float[] left, float[] right)
    {
        if (left.Length != right.Length) throw new InvalidDataException("Unequal exploration vector dimensions.");
        double squared = 0;
        for (var index = 0; index < left.Length; index++) { var delta = (double)left[index] - right[index]; squared += delta * delta; }
        return Math.Sqrt(squared);
    }
    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private sealed record BookSplit(bool[] Heldout, string Kind, int Components, int CrossDocumentDuplicates);
    public sealed class BookInput { public string Text { get; set; } = string.Empty; }
    public sealed class BookPrediction
    {
        public uint PredictedLabel { get; set; }
        public float[] Score { get; set; } = [];
    }
    public sealed class BookVector { public float[] Features { get; set; } = []; }
}
