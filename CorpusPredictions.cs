using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;

internal sealed class CorpusPredictions
{
    public const string ResearchLimit = "Exploratory model output from provided annotations with unknown adjudication status. Independent accuracy has not been measured.";
    private static readonly string[] ModelIds =
    [
        "segment_type_en", "segment_type_ro", "temporal_relation_en", "temporal_relation_ro",
        "timebank_event_class_ro", "timebank_event_tense_ro", "timebank_timex_type_ro",
        "timebank_tlink_ro", "timebank_slink_ro", "timebank_alink_ro"
    ];
    private readonly MLContext _context = new(seed: 42);
    private readonly ConcurrentDictionary<string, Lazy<ITransformer>> _models = new(StringComparer.Ordinal);
    private readonly string? _directory = Environment.GetEnvironmentVariable("TYM_CORPUS_MODEL_DIR");

    public IEnumerable<object> Catalog() => ModelIds.Select(id =>
    {
        var metadata = Metadata(id);
        return (object)new
        {
            model_id = id,
            task = id[..id.LastIndexOf('_')],
            language = id[(id.LastIndexOf('_') + 1)..],
            available = PathFor(id) is { } path && File.Exists(path),
            training_rows = metadata.TrainingRows,
            labels = metadata.Labels,
            source_types = metadata.SourceTypes,
            research_limit = ResearchLimit
        };
    });

    public bool IsKnown(string? id) => id is not null && ModelIds.Contains(id, StringComparer.Ordinal);

    public object? Predict(string id, string text)
    {
        var path = PathFor(id);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        var model = _models.GetOrAdd(id, modelId => new Lazy<ITransformer>(
            () => _context.Model.Load(path, out _))).Value;
        // A separate engine per request avoids sharing mutable PredictionEngine state.
        using var engine = _context.Model.CreatePredictionEngine<CorpusInput, CorpusOutput>(model);
        var result = engine.Predict(new CorpusInput { Text = text, Label = string.Empty });
        if (string.IsNullOrWhiteSpace(result.Label))
        {
            throw new InvalidDataException("The corpus model returned an empty label.");
        }
        var metadata = Metadata(id);
        return new
        {
            model_id = id,
            predicted_label = result.Label,
            training_rows = metadata.TrainingRows,
            labels = metadata.Labels,
            source_types = metadata.SourceTypes,
            research_limit = ResearchLimit
        };
    }

    private string? PathFor(string id) => string.IsNullOrWhiteSpace(_directory)
        ? null
        : Path.Combine(Path.GetFullPath(_directory), id + "_sdca.zip");

    private (int? TrainingRows, string[] Labels, string[] SourceTypes) Metadata(string id)
    {
        var path = PathFor(id);
        if (path is null || !File.Exists(path + ".manifest.json"))
        {
            return (null, [], []);
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(path + ".manifest.json"));
        var root = manifest.RootElement;
        return (root.GetProperty("training_rows").GetInt32(),
            root.GetProperty("labels").EnumerateArray().Select(item => item.GetString()!).ToArray(),
            root.GetProperty("source_types").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }

    private sealed class CorpusInput
    {
        public string Text { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    private sealed class CorpusOutput
    {
        [ColumnName("PredictedLabelText")]
        public string Label { get; set; } = string.Empty;
    }
}

internal sealed record CorpusPredictionRequest(string? ModelId, string? Text);
