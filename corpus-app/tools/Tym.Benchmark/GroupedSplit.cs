using Tym.Corpus.Data;

namespace Tym.Benchmark;

/// <summary>Groups entire documents, joining cross-document duplicate feature rows before assigning folds.</summary>
public static class GroupedSplit
{
    public static string DocumentKey(CorpusRow row) => row.SourceGroup + ":" + row.DocumentId;
    public static string FeatureKey(CorpusRow row) => row.Task + ":" + row.Language + ":" + CorpusFiles.Hash(CorpusFiles.NormalizeSpaces(row.Text));

    public static SplitPlan Create(IReadOnlyList<CorpusRow> rows, int folds = 3, int seed = 42)
    {
        if (folds is < 2 or > 10 || rows.Count == 0) throw new ArgumentException("Need data and 2-10 folds.");
        var parent = rows.Select(DocumentKey).Distinct(StringComparer.Ordinal).ToDictionary(key => key, key => key, StringComparer.Ordinal);
        string Find(string key)
        {
            var root = key;
            while (parent[root] != root) root = parent[root];
            while (parent[key] != key) { var next = parent[key]; parent[key] = root; key = next; }
            return root;
        }
        var features = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicateRows = 0;
        foreach (var row in rows)
        {
            var document = DocumentKey(row);
            var feature = FeatureKey(row);
            if (features.TryGetValue(feature, out var previous) && previous != document)
            {
                var left = Find(document);
                var right = Find(previous);
                if (left != right) parent[string.CompareOrdinal(left, right) > 0 ? left : right] = string.CompareOrdinal(left, right) > 0 ? right : left;
                duplicateRows++;
            }
            else features[feature] = document;
        }
        var documents = parent.Keys.ToArray();
        var components = rows.GroupBy(row => Find(DocumentKey(row))).Select(group => new
        {
            Id = group.Key, Rows = group.Count(), Documents = group.Select(DocumentKey).Distinct().ToArray(),
            Order = CorpusFiles.Hash(seed + "|" + group.Key)
        }).OrderByDescending(component => component.Rows).ThenBy(component => component.Order, StringComparer.Ordinal).ToArray();
        if (components.Length < folds) throw new InvalidDataException("Too few independent document components for these folds.");
        var foldRows = new int[folds];
        var assignments = new Dictionary<string, int>(StringComparer.Ordinal);
        var componentByDocument = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            var fold = Enumerable.Range(0, folds).OrderBy(index => foldRows[index]).ThenBy(index => index).First();
            foldRows[fold] += component.Rows;
            foreach (var document in component.Documents)
            {
                assignments.Add(document, fold);
                componentByDocument.Add(document, component.Id);
            }
        }
        // Assert the property the evaluation depends on, independently of union traversal details.
        if (rows.GroupBy(FeatureKey).Any(group => group.Select(row => assignments[DocumentKey(row)]).Distinct().Count() > 1))
            throw new InvalidDataException("Duplicate feature text crossed a validation fold.");
        return new(folds, seed, assignments, componentByDocument, components.Length, duplicateRows, foldRows);
    }
}

public sealed record SplitPlan(int Folds, int Seed, IReadOnlyDictionary<string, int> DocumentFolds,
    IReadOnlyDictionary<string, string> DocumentComponents, int IndependentComponents, int CrossDocumentDuplicateRows, int[] FoldRows);
