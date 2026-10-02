namespace Tym.Benchmark;

/// <summary>Computes pooled out-of-fold metrics over a fixed full label inventory, including rare labels.</summary>
public static class Metrics
{
    public static ClassificationMetrics Calculate(IEnumerable<(string Actual, string Predicted)> observations, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0 || labels.Distinct(StringComparer.Ordinal).Count() != labels.Count) throw new ArgumentException("Need unique labels.");
        var rows = observations.ToArray();
        if (rows.Length == 0) throw new ArgumentException("Need observations.");
        var confusion = labels.ToDictionary(actual => actual,
            _ => labels.ToDictionary(predicted => predicted, _ => 0, StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (actual, predicted) in rows)
        {
            if (!confusion.TryGetValue(actual, out var cell) || !cell.ContainsKey(predicted)) throw new ArgumentException("Observation label outside fixed inventory.");
            cell[predicted]++;
        }
        var perClass = labels.Select(label =>
        {
            var truePositive = confusion[label][label];
            var support = confusion[label].Values.Sum();
            var predicted = labels.Sum(actual => confusion[actual][label]);
            var precision = predicted == 0 ? 0 : (double)truePositive / predicted;
            var recall = support == 0 ? 0 : (double)truePositive / support;
            var f1 = precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
            return new ClassMetrics(label, support, predicted, precision, recall, f1);
        }).ToArray();
        return new(rows.Length, (double)rows.Count(row => row.Actual == row.Predicted) / rows.Length,
            perClass.Average(row => row.F1), perClass, confusion);
    }
}

public sealed record ClassMetrics(string Label, int Support, int PredictedCount, double Precision, double Recall, double F1);
public sealed record ClassificationMetrics(int Rows, double Accuracy, double MacroF1,
    IReadOnlyList<ClassMetrics> PerClass, Dictionary<string, Dictionary<string, int>> ConfusionMatrix);
