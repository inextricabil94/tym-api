using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML;
using Microsoft.ML.Data;
using Tym.Corpus.Core;

namespace Tym.Corpus.Api;

/// <summary>
/// Loads trusted, immutable model bundles from a private directory. A cached bundle includes weights,
/// manifest identity/integrity results and a scoring probe. Predictions remain exploratory annotations.
/// </summary>
public sealed class CorpusModelService
{
    public const string ResearchLimit = "Exploratory model output. Independent accuracy has not been measured; predictions are not adjudicated annotations.";
    public static readonly TimeSpan FailedBundleRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
    private readonly string? _directory;
    private readonly TimeProvider _clock;
    private readonly MLContext _context = new(seed: 42);
    private readonly ConcurrentDictionary<string, Lazy<ModelBundle>> _bundles = new(StringComparer.Ordinal);

    public CorpusModelService(string? modelDirectory, TimeProvider? clock = null)
    {
        _directory = string.IsNullOrWhiteSpace(modelDirectory) ? null : Path.GetFullPath(modelDirectory);
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsConfigured => _directory is not null;

    /// <summary>Available means the cached bundle loaded and scored successfully, rather than merely existing on disk.</summary>
    public IReadOnlyList<ModelCatalogEntry> Catalog() => ModelCatalog.All.Select(definition => Describe(definition, Bundle(definition))).ToArray();

    /// <summary>Every expected classifier must score; missing metadata permits explicitly unverified exploratory readiness.</summary>
    public ModelReadinessResponse Ready()
    {
        var catalog = Catalog();
        return new(catalog.All(model => model.Available), catalog.All(model => model.Available && model.IntegrityStatus == "verified"),
            catalog.Count(model => model.Installed), catalog.Count(model => model.Loaded), catalog.Count(model => model.Scorable),
            catalog.Count(model => model.Available), catalog.Count,
            catalog.Select(model => new ModelReadinessEntry(model.ModelId, model.Available, model.ReadinessStatus,
                model.IntegrityStatus, model.IdentityStatus)).ToArray());
    }

    public CorpusPredictionResponse Predict(string modelId, string text) => PredictMany(modelId, [text])[0];

    /// <summary>Scores a bounded document batch with one request-owned engine; engines are never shared between requests.</summary>
    public IReadOnlyList<CorpusPredictionResponse> PredictMany(string modelId, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count is < 1 or > 500) throw new ArgumentException("A prediction batch requires 1 to 500 texts.", nameof(texts));
        foreach (var text in texts)
        {
            var validation = PredictionValidation.Validate(new CorpusPredictionRequest(modelId, text));
            if (validation is not null)
                throw new ArgumentException(validation.Error,
                    validation.Code == "invalid_model_id" ? nameof(modelId) : nameof(texts));
        }
        var definition = ModelCatalog.Get(modelId);
        var bundle = Bundle(definition);
        if (!bundle.Available) throw new ModelUnavailableException(modelId, bundle.Failure);
        // Task format errors belong to request validation and must not evict a healthy model bundle.
        var inputs = texts.Select(text => Input(definition, text, bundle.Manifest.InputFormat)).ToArray();

        var labels = new List<string>(texts.Count);
        try
        {
            // PredictionEngine is mutable: every request owns and disposes its engine.
            using var engine = _context.Model.CreatePredictionEngine<TextModelInput, TextOutput>(bundle.Model!);
            foreach (var input in inputs)
            {
                var label = engine.Predict(input).PredictedLabel;
                if (string.IsNullOrWhiteSpace(label)) throw new InvalidDataException("The model returned an empty label.");
                labels.Add(label);
            }
        }
        catch (Exception exception) when (IsArtifactFailure(exception))
        {
            // A failed request invalidates this snapshot so a repaired trusted artifact may be retried.
            _bundles.TryRemove(modelId, out _);
            throw new ModelUnavailableException(modelId, exception);
        }

        var metadata = Describe(definition, bundle);
        return labels.Select(label => new CorpusPredictionResponse(modelId, definition.Task, definition.Language, label,
            metadata.TrainingRows, metadata.Labels, metadata.SourceTypes, metadata.SourceGroups,
            metadata.DocumentCount, metadata.MetadataAvailable, metadata.MetadataStatus,
            metadata.Provenance, metadata.ResearchLimit, metadata.IntegrityStatus, metadata.IdentityStatus,
            metadata.ReadinessStatus, metadata.InputFormat)).ToArray();
    }

    private Lazy<ModelBundle> Deferred(ModelDefinition definition) => new(() => LoadBundle(definition), LazyThreadSafetyMode.ExecutionAndPublication);

    private ModelBundle Bundle(ModelDefinition definition)
    {
        while (true)
        {
            var cached = _bundles.GetOrAdd(definition.ModelId, _ => Deferred(definition));
            var bundle = cached.Value;
            if (bundle.Available || _directory is null || _clock.GetUtcNow() < bundle.RetryAfter) return bundle;
            // Replace only the observed failed snapshot; concurrent callers cannot remove a newer bundle.
            var retry = Deferred(definition);
            if (_bundles.TryUpdate(definition.ModelId, retry, cached)) return retry.Value;
        }
    }

    private ModelBundle LoadBundle(ModelDefinition definition)
    {
        var path = _directory is null ? null : Path.Combine(_directory, definition.ModelId + "_sdca.zip");
        var retryAfter = _clock.GetUtcNow() + FailedBundleRetryDelay;
        var manifest = ReadManifest(path);
        var identity = manifest.IdentityInvalid
            || (manifest.Task is not null && manifest.Task != definition.Task)
            || (manifest.Language is not null && manifest.Language != definition.Language)
            || (manifest.InputFormat is not null && manifest.InputFormat != TaskInputParser.StructuredFormat)
            ? "mismatch" : manifest.Task is not null && manifest.Language is not null ? "verified" : "not_supplied";
        var integrity = manifest.HashInvalid ? "invalid" : manifest.Hash is null ? "not_supplied" : "not_checked";
        var installed = path is not null && File.Exists(path);
        if (!installed) return new(null, manifest, false, false, false, integrity, identity, "missing_weights", retryAfter);
        if (identity == "mismatch") return new(null, manifest, true, false, false, integrity, identity, "identity_mismatch", retryAfter);
        if (integrity == "invalid") return new(null, manifest, true, false, false, integrity, identity, "integrity_invalid", retryAfter);

        ITransformer? model = null;
        try
        {
            // Hash and deserialize the same captured bytes. Successful bundles remain immutable for this process.
            var bytes = File.ReadAllBytes(path!);
            if (manifest.Hash is not null)
            {
                var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                integrity = string.Equals(actual, manifest.Hash, StringComparison.OrdinalIgnoreCase) ? "verified" : "mismatch";
                if (integrity == "mismatch") return new(null, manifest, true, false, false, integrity, identity, "integrity_mismatch", retryAfter);
            }
            using var stream = new MemoryStream(bytes, writable: false);
            model = _context.Model.Load(stream, out _);
            using var probe = _context.Model.CreatePredictionEngine<TextModelInput, TextOutput>(model);
            if (string.IsNullOrWhiteSpace(probe.Predict(Input(definition, ProbeText(definition.Task), manifest.InputFormat)).PredictedLabel))
                throw new InvalidDataException("The scoring probe returned an empty label.");
            var readiness = integrity == "verified" && identity == "verified" ? "ready_verified" : "ready_unverified";
            return new(model, manifest, true, true, true, integrity, identity, readiness, retryAfter);
        }
        catch (Exception exception) when (IsArtifactFailure(exception))
        {
            return new(model, manifest, true, model is not null, false, integrity, identity,
                model is null ? "load_failed" : "scoring_failed", retryAfter, exception);
        }
    }

    private static ModelCatalogEntry Describe(ModelDefinition definition, ModelBundle bundle)
    {
        // Mismatched identity/integrity metadata cannot describe the usable model honestly.
        var rejected = bundle.IdentityStatus == "mismatch" || bundle.IntegrityStatus is "mismatch" or "invalid";
        var manifest = rejected ? null : bundle.Manifest.Metadata;
        var metadataStatus = rejected ? bundle.ReadinessStatus : bundle.Manifest.MetadataStatus;
        var provenance = manifest is null
            ? "Training provenance unavailable: no valid matching model manifest was found."
            : "Training provenance is reported by the supplied model manifest and has not been independently adjudicated.";
        var limit = (string.IsNullOrWhiteSpace(manifest?.ResearchLimit) ? string.Empty : manifest.ResearchLimit + " ") + ResearchLimit;
        if (bundle.IntegrityStatus != "verified" || bundle.IdentityStatus != "verified")
            limit += " Artifact integrity or task/language identity is unverified; successful scoring does not establish scientific validity.";
        return new(definition.ModelId, definition.Task, definition.Language, definition.DisplayName, definition.InputHint,
            bundle.Available, manifest is not null, metadataStatus, manifest?.TrainingRows, manifest?.Labels ?? [],
            manifest?.SourceTypes ?? [], manifest?.SourceGroups ?? [], manifest?.DocumentCount, manifest?.Trainer,
            manifest?.Featurizer, bundle.Manifest.Hash, provenance, limit, bundle.Installed, bundle.Loaded, bundle.Scorable,
            bundle.IntegrityStatus, bundle.IdentityStatus, bundle.ReadinessStatus, rejected ? null : bundle.Manifest.InputFormat);
    }

    private static ManifestSnapshot ReadManifest(string? modelPath)
    {
        if (modelPath is null || !File.Exists(modelPath + ".manifest.json")) return new(null, "missing");
        try
        {
            var json = File.ReadAllText(modelPath + ".manifest.json");
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, "invalid");
            var root = document.RootElement;
            string? Field(string name, out bool invalid)
            {
                invalid = false;
                if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
                if (value.ValueKind != JsonValueKind.String) { invalid = true; return null; }
                var text = value.GetString();
                invalid = string.IsNullOrWhiteSpace(text);
                return text;
            }
            var task = Field("task", out var taskInvalid);
            var language = Field("language", out var languageInvalid);
            var inputFormat = Field("input_format", out var formatInvalid);
            var hash = Field("model_sha256", out var hashInvalid);
            hashInvalid |= hash is not null && (hash.Length != 64 || hash.Any(character => !Uri.IsHexDigit(character)));
            ModelManifest? metadata;
            try { metadata = JsonSerializer.Deserialize<ModelManifest>(json, ManifestJson); }
            catch (JsonException) { metadata = null; }
            var valid = metadata?.TrainingRows is >= 0 && metadata.Labels is not null
                && metadata.SourceTypes is not null && metadata.SourceGroups is not null
                && !metadata.Labels.Any(string.IsNullOrWhiteSpace)
                && !metadata.SourceTypes.Any(string.IsNullOrWhiteSpace)
                && !metadata.SourceGroups.Any(string.IsNullOrWhiteSpace)
                && (metadata.DocumentCount is null or >= 0);
            return new(valid ? metadata : null, valid ? "loaded" : "invalid", task, language, hash,
                taskInvalid || languageInvalid || formatInvalid, hashInvalid, inputFormat);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new(null, "invalid");
        }
    }

    private static bool IsArtifactFailure(Exception exception) => exception is IOException or InvalidOperationException
        or ArgumentException or FormatException or NotSupportedException or UnauthorizedAccessException;

    private static TextModelInput Input(ModelDefinition definition, string text, string? inputFormat) => inputFormat == TaskInputParser.StructuredFormat
        ? TaskInputParser.Parse(definition.Task, text)
        : new TextModelInput { Text = text };

    private static string ProbeText(string task) => task is "timebank_tlink" or "timebank_slink" or "timebank_alink"
        ? "From: read\nFrom context: Mara [TARGET]read[/TARGET] a letter.\nSignal: before\nTo: left\nTo context: Ana [TARGET]left[/TARGET] the garden."
        : task == "timebank_timex_type" ? "Context: Mara arrived [TARGET]yesterday[/TARGET].\nTime expression: yesterday"
        : "Context: Mara [TARGET]read[/TARGET] a letter.\nEvent: read";

    public sealed class TextInput
    {
        public string Text { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public sealed class TextOutput
    {
        [ColumnName("PredictedLabelText")]
        public string PredictedLabel { get; set; } = string.Empty;
    }

    private sealed record ModelBundle(ITransformer? Model, ManifestSnapshot Manifest, bool Installed,
        bool Loaded, bool Scorable, string IntegrityStatus, string IdentityStatus, string ReadinessStatus,
        DateTimeOffset RetryAfter, Exception? Failure = null)
    {
        public bool Available => Loaded && Scorable && IdentityStatus != "mismatch" && IntegrityStatus is not ("mismatch" or "invalid");
    }

    private sealed record ManifestSnapshot(ModelManifest? Metadata, string MetadataStatus, string? Task = null,
        string? Language = null, string? Hash = null, bool IdentityInvalid = false, bool HashInvalid = false,
        string? InputFormat = null);

    private sealed class ModelManifest
    {
        public int? TrainingRows { get; init; }
        public string[]? Labels { get; init; }
        public string[]? SourceTypes { get; init; }
        public string[]? SourceGroups { get; init; }
        public int? DocumentCount { get; init; }
        public string? Trainer { get; init; }
        public string? Featurizer { get; init; }
        public string? ResearchLimit { get; init; }
        public string? InputFormat { get; init; }
    }
}

public sealed class ModelUnavailableException(string modelId, Exception? innerException = null)
    : Exception("The selected corpus model is unavailable.", innerException)
{
    public string ModelId { get; } = modelId;
}

