namespace Tym.Corpus.Api;

public sealed record CorpusPredictionRequest(string? ModelId, string? Text);
public sealed record ApiError(string Error, string Code);

public sealed record ModelCatalogEntry(
    string ModelId,
    string Task,
    string Language,
    string DisplayName,
    string InputHint,
    bool Available,
    bool MetadataAvailable,
    string MetadataStatus,
    int? TrainingRows,
    string[] Labels,
    string[] SourceTypes,
    string[] SourceGroups,
    int? DocumentCount,
    string? Trainer,
    string? Featurizer,
    string? ModelSha256,
    string Provenance,
    string ResearchLimit,
    bool Installed,
    bool Loaded,
    bool Scorable,
    string IntegrityStatus,
    string IdentityStatus,
    string ReadinessStatus,
    string? InputFormat);

/// <summary>Readiness checks loadability and a scoring probe; artifact verification is separate from scientific validity.</summary>
public sealed record ModelReadinessResponse(
    bool Ready,
    bool AllIntegrityVerified,
    int InstalledModels,
    int LoadedModels,
    int ScorableModels,
    int ReadyModels,
    int TotalModels,
    IReadOnlyList<ModelReadinessEntry> Models);

public sealed record ModelReadinessEntry(string ModelId, bool Ready, string ReadinessStatus,
    string IntegrityStatus, string IdentityStatus);

public sealed record CorpusPredictionResponse(
    string ModelId,
    string Task,
    string Language,
    string PredictedLabel,
    int? TrainingRows,
    string[] Labels,
    string[] SourceTypes,
    string[] SourceGroups,
    int? DocumentCount,
    bool MetadataAvailable,
    string MetadataStatus,
    string Provenance,
    string ResearchLimit,
    string IntegrityStatus,
    string IdentityStatus,
    string ReadinessStatus,
    string? InputFormat);

public static class PredictionValidation
{
    public const int MaximumTextLength = 50_000;

    public static ApiError? Validate(CorpusPredictionRequest? request)
    {
        if (request is null)
        {
            return new ApiError("A JSON request containing model_id and text is required.", "invalid_request");
        }
        if (!ModelCatalog.IsKnown(request.ModelId))
        {
            return new ApiError("Choose a model_id from GET /v1/models.", "invalid_model_id");
        }
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaximumTextLength)
        {
            return new ApiError("Text must contain 1 to 50,000 characters.", "invalid_text");
        }
        return null;
    }
}
