using System.Diagnostics;
using Microsoft.ML.Data;

namespace Tym.Benchmark;

public sealed record CustomClassifierResult(string[] Predictions, double FitSeconds,
    double PredictionSeconds, object Parameters);

/// <summary>
/// Bounded CART and exact cosine KNN experiments over fold-fitted ML.NET vectors.
/// No validation features or labels participate in fitting or feature selection.
/// </summary>
public static class CustomClassifiers
{
    private const int MaximumTreeDepth = 8;
    private const int MinimumLeafRows = 5;
    private const int MaximumTreeFeatures = 128;
    private const int MaximumThresholds = 8;
    private const int Neighbors = 5;
    private const double ComparisonTolerance = 1e-12;

    public static CustomClassifierResult FitPredict(string algorithm, VBuffer<float>[] train,
        string[] trainLabels, VBuffer<float>[] validation, int seed)
    {
        if (algorithm is not ("decision_tree" or "knn"))
            throw new ArgumentException("Supported custom classifiers are decision_tree and knn.", nameof(algorithm));
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(trainLabels);
        ArgumentNullException.ThrowIfNull(validation);
        if (train.Length == 0 || train.Length != trainLabels.Length
            || trainLabels.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Training vectors require matching nonblank labels and at least one row.");
        var dimension = train[0].Length;
        if (dimension < 1 || train.Any(row => row.Length != dimension)
            || validation.Any(row => row.Length != dimension))
            throw new ArgumentException("All vectors must share a positive feature dimension.");
        CheckFinite(train);
        CheckFinite(validation);
        var labels = trainLabels.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var labelIndex = labels.Select((label, index) => (label, index))
            .ToDictionary(item => item.label, item => item.index, StringComparer.Ordinal);
        var targets = trainLabels.Select(label => labelIndex[label]).ToArray();
        return algorithm == "knn"
            ? FitKnn(train, targets, validation, labels, seed)
            : FitTree(train, targets, validation, labels, seed);
    }

    private static CustomClassifierResult FitKnn(VBuffer<float>[] train, int[] targets,
        VBuffer<float>[] validation, string[] labels, int seed)
    {
        var timer = Stopwatch.StartNew();
        // Copy/normalize vectors; callers retain unchanged shared featurization buffers.
        var stored = train.Select(UnitVector).ToArray();
        var fitSeconds = timer.Elapsed.TotalSeconds;
        timer.Restart();
        var predictions = new string[validation.Length];
        var effectiveK = Math.Min(Neighbors, train.Length);
        for (var row = 0; row < validation.Length; row++)
        {
            var query = UnitVector(validation[row]);
            var nearest = new Neighbor[effectiveK];
            var count = 0;
            // Exact search evaluates every training row; no candidate or sampling shortcut.
            for (var candidate = 0; candidate < stored.Length; candidate++)
            {
                var similarity = Cosine(stored[candidate], query);
                var insertion = count;
                for (var position = 0; position < count; position++)
                {
                    if (similarity > nearest[position].Similarity
                        || (similarity == nearest[position].Similarity && candidate < nearest[position].Row))
                    { insertion = position; break; }
                }
                if (insertion >= effectiveK) continue;
                var nextCount = Math.Min(effectiveK, count + 1);
                for (var position = nextCount - 1; position > insertion; position--)
                    nearest[position] = nearest[position - 1];
                nearest[insertion] = new(candidate, similarity);
                count = nextCount;
            }
            var votes = new int[labels.Length];
            var sums = new double[labels.Length];
            foreach (var neighbor in nearest)
            {
                var target = targets[neighbor.Row];
                votes[target]++;
                sums[target] += neighbor.Similarity;
            }
            var winner = 0;
            for (var target = 1; target < labels.Length; target++)
                if (votes[target] > votes[winner]
                    || (votes[target] == votes[winner] && sums[target] > sums[winner] + ComparisonTolerance))
                    winner = target;
            predictions[row] = labels[winner];
        }
        return new(predictions, fitSeconds, timer.Elapsed.TotalSeconds, new
        {
            algorithm = "knn", implementation = "custom C# exact cosine KNN",
            k = Neighbors, effective_k = effectiveK, seed, seed_used = false,
            feature_dimension = train[0].Length, stored_training_rows = train.Length,
            normalization = "L2 unit vectors; zero-norm vectors have similarity zero",
            neighbor_tie = "Similarity descending, training row index ascending",
            vote_tie = "Vote count, then summed cosine similarity (1e-12 tolerance), then ordinal label",
            fit_timing = "Training-vector copy and normalization",
            prediction_timing = "Validation normalization, exact all-row search and voting"
        });
    }

    private static CustomClassifierResult FitTree(VBuffer<float>[] train, int[] targets,
        VBuffer<float>[] validation, string[] labels, int seed)
    {
        var timer = Stopwatch.StartNew();
        var features = SelectFeatures(train);
        var projected = train.Select(row => Project(row, features)).ToArray();
        var nodeCount = 0;
        var leafCount = 0;
        var deepest = 0;
        var root = Build(Enumerable.Range(0, train.Length).ToArray(), 0);
        var fitSeconds = timer.Elapsed.TotalSeconds;
        timer.Restart();
        var predictions = validation.Select(row =>
        {
            var values = Project(row, features);
            var node = root;
            while (node.Left is not null && node.Right is not null)
                node = values[node.Feature] <= node.Threshold ? node.Left : node.Right;
            return labels[node.PredictedClass];
        }).ToArray();
        return new(predictions, fitSeconds, timer.Elapsed.TotalSeconds, new
        {
            algorithm = "decision_tree", implementation = "custom C# multiclass CART, Gini impurity",
            max_depth = MaximumTreeDepth, min_leaf = MinimumLeafRows,
            feature_selection = "Highest population variance using training rows only; implicit sparse values are zero",
            max_features = MaximumTreeFeatures, selected_features = features.Length,
            selected_feature_indices = features, feature_dimension = train[0].Length,
            max_thresholds_per_feature_per_node = MaximumThresholds,
            threshold_rule = "At most eight evenly spaced ranks among distinct node-training value boundaries, including extremes",
            split_tie = "Gini improvements within 1e-12 retain the lower original feature index and lower threshold",
            leaf_tie = "Ordinal label", minimum_gini_gain = ComparisonTolerance,
            nodes = nodeCount, leaves = leafCount, observed_depth = deepest,
            seed, seed_used = false,
            fit_timing = "Training-only variance selection, projection and complete tree induction",
            prediction_timing = "Validation projection and tree traversal"
        });

        TreeNode Build(int[] rows, int depth)
        {
            nodeCount++;
            deepest = Math.Max(deepest, depth);
            var counts = new int[labels.Length];
            foreach (var row in rows) counts[targets[row]]++;
            var majority = 0;
            for (var label = 1; label < counts.Length; label++)
                if (counts[label] > counts[majority]) majority = label;
            if (depth >= MaximumTreeDepth || rows.Length < 2 * MinimumLeafRows
                || counts[majority] == rows.Length || features.Length == 0)
            { leafCount++; return new(majority); }

            var parentGini = 1 - counts.Sum(value => (double)value * value) / ((double)rows.Length * rows.Length);
            var bestGain = 0d;
            var bestFeature = -1;
            var bestThreshold = 0d;
            for (var feature = 0; feature < features.Length; feature++)
            {
                var sorted = rows.Select(row => new ValueClass(projected[row][feature], targets[row])).ToArray();
                Array.Sort(sorted, static (left, right) => left.Value.CompareTo(right.Value));
                var distinct = new List<float>();
                foreach (var item in sorted)
                    if (distinct.Count == 0 || item.Value != distinct[^1]) distinct.Add(item.Value);
                if (distinct.Count < 2) continue;
                var boundaryCount = distinct.Count - 1;
                var candidates = Math.Min(MaximumThresholds, boundaryCount);
                var leftCounts = new int[labels.Length];
                var leftSize = 0;
                for (var candidate = 0; candidate < candidates; candidate++)
                {
                    var boundary = candidates == 1 ? 0
                        : (int)Math.Round((double)candidate * (boundaryCount - 1) / (candidates - 1));
                    // Double midpoint avoids float rounding to the upper neighboring value.
                    var threshold = (double)distinct[boundary]
                        + ((double)distinct[boundary + 1] - distinct[boundary]) / 2;
                    while (leftSize < sorted.Length && sorted[leftSize].Value <= threshold)
                    { leftCounts[sorted[leftSize].Class]++; leftSize++; }
                    var rightSize = rows.Length - leftSize;
                    if (leftSize < MinimumLeafRows || rightSize < MinimumLeafRows) continue;
                    var leftSquares = 0d;
                    var rightSquares = 0d;
                    for (var label = 0; label < counts.Length; label++)
                    {
                        leftSquares += (double)leftCounts[label] * leftCounts[label];
                        var right = counts[label] - leftCounts[label];
                        rightSquares += (double)right * right;
                    }
                    var weightedGini = 1 - (leftSquares / leftSize + rightSquares / rightSize) / rows.Length;
                    var gain = parentGini - weightedGini;
                    if (gain > bestGain + ComparisonTolerance)
                    { bestGain = gain; bestFeature = feature; bestThreshold = threshold; }
                }
            }
            if (bestFeature < 0) { leafCount++; return new(majority); }
            var leftRows = rows.Where(row => projected[row][bestFeature] <= bestThreshold).ToArray();
            var rightRows = rows.Where(row => projected[row][bestFeature] > bestThreshold).ToArray();
            return new(majority, bestFeature, bestThreshold, Build(leftRows, depth + 1), Build(rightRows, depth + 1));
        }
    }

    private static int[] SelectFeatures(VBuffer<float>[] rows)
    {
        var moments = new Dictionary<int, (double Sum, double Squares)>();
        foreach (var row in rows)
        {
            var values = row.GetValues();
            var indices = row.GetIndices();
            for (var slot = 0; slot < values.Length; slot++)
            {
                var index = row.IsDense ? slot : indices[slot];
                var value = (double)values[slot];
                if (value == 0) continue;
                var prior = moments.GetValueOrDefault(index);
                moments[index] = (prior.Sum + value, prior.Squares + value * value);
            }
        }
        return moments.Select(item => new
            {
                Index = item.Key,
                Variance = Math.Max(0, item.Value.Squares / rows.Length
                    - Math.Pow(item.Value.Sum / rows.Length, 2))
            }).Where(item => item.Variance > 0)
            .OrderByDescending(item => item.Variance).ThenBy(item => item.Index)
            .Take(MaximumTreeFeatures).Select(item => item.Index).Order().ToArray();
    }

    private static float[] Project(VBuffer<float> row, int[] features)
    {
        var projected = new float[features.Length];
        var values = row.GetValues();
        if (row.IsDense)
        {
            for (var feature = 0; feature < features.Length; feature++) projected[feature] = values[features[feature]];
            return projected;
        }
        var indices = row.GetIndices();
        var slot = 0;
        for (var feature = 0; feature < features.Length; feature++)
        {
            while (slot < indices.Length && indices[slot] < features[feature]) slot++;
            if (slot < indices.Length && indices[slot] == features[feature]) projected[feature] = values[slot];
        }
        return projected;
    }

    private static VBuffer<float> UnitVector(VBuffer<float> row)
    {
        var values = row.GetValues();
        var squared = 0d;
        foreach (var value in values) squared += (double)value * value;
        if (squared == 0) return new(row.Length, 0, [], []);
        var inverse = 1 / Math.Sqrt(squared);
        var normalized = values.ToArray();
        for (var index = 0; index < normalized.Length; index++) normalized[index] = (float)(normalized[index] * inverse);
        return row.IsDense ? new(row.Length, normalized)
            : new(row.Length, normalized.Length, normalized, row.GetIndices().ToArray());
    }

    private static double Cosine(VBuffer<float> left, VBuffer<float> right)
    {
        var a = left.GetValues();
        var b = right.GetValues();
        var score = 0d;
        if (left.IsDense && right.IsDense)
            for (var index = 0; index < a.Length; index++) score += (double)a[index] * b[index];
        else if (left.IsDense)
        {
            var indices = right.GetIndices();
            for (var index = 0; index < b.Length; index++) score += (double)a[indices[index]] * b[index];
        }
        else if (right.IsDense)
        {
            var indices = left.GetIndices();
            for (var index = 0; index < a.Length; index++) score += (double)a[index] * b[indices[index]];
        }
        else
        {
            var aIndices = left.GetIndices();
            var bIndices = right.GetIndices();
            var ai = 0;
            var bi = 0;
            while (ai < a.Length && bi < b.Length)
            {
                if (aIndices[ai] == bIndices[bi]) { score += (double)a[ai++] * b[bi++]; }
                else if (aIndices[ai] < bIndices[bi]) ai++;
                else bi++;
            }
        }
        return Math.Clamp(score, -1, 1);
    }

    private static void CheckFinite(VBuffer<float>[] rows)
    {
        foreach (var row in rows)
            foreach (var value in row.GetValues())
                if (!float.IsFinite(value)) throw new ArgumentException("Feature vectors must contain finite values.");
    }

    private readonly record struct Neighbor(int Row, double Similarity);
    private readonly record struct ValueClass(float Value, int Class);
    private sealed record TreeNode(int PredictedClass, int Feature = -1, double Threshold = 0,
        TreeNode? Left = null, TreeNode? Right = null);
}
