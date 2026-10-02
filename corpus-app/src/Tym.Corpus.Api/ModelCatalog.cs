namespace Tym.Corpus.Api;

public sealed record ModelDefinition(
    string ModelId,
    string Task,
    string Language,
    string DisplayName,
    string InputHint);

public static class ModelCatalog
{
    private const string SegmentHint = "Enter one narrative segment in the model's language.";
    private const string SegmentPairHint = "Training format: Earlier segment: <text>\nRelation cue: <cue>\nLater segment: <text>";
    private const string EventHint = "Training format: Context: <sentence with [TARGET] event [/TARGET]>\nEvent: <event>";
    private const string TimexHint = "Training format: Context: <sentence with [TARGET] expression [/TARGET]>\nTime expression: <expression>";
    private const string LinkHint = "Training format: From: <participant>\nFrom context: <context>\nSignal: <cue>\nTo: <participant>\nTo context: <context>";

    public static IReadOnlyList<ModelDefinition> All { get; } = Array.AsReadOnly<ModelDefinition>(
    [
        new("segment_type_en", "segment_type", "en", "Narrative segment · English", SegmentHint),
        new("segment_type_ro", "segment_type", "ro", "Narrative segment · Romanian", SegmentHint),
        new("temporal_relation_en", "temporal_relation", "en", "Segment relation · English", SegmentPairHint),
        new("temporal_relation_ro", "temporal_relation", "ro", "Segment relation · Romanian", SegmentPairHint),
        new("timebank_event_class_ro", "timebank_event_class", "ro", "Event class · Romanian", EventHint),
        new("timebank_event_tense_ro", "timebank_event_tense", "ro", "Event tense · Romanian", EventHint),
        new("timebank_timex_type_ro", "timebank_timex_type", "ro", "Time expression type · Romanian", TimexHint),
        new("timebank_tlink_ro", "timebank_tlink", "ro", "Temporal link · Romanian", LinkHint),
        new("timebank_slink_ro", "timebank_slink", "ro", "Subordination link · Romanian", LinkHint),
        new("timebank_alink_ro", "timebank_alink", "ro", "Aspectual link · Romanian", LinkHint),
        new("timebank_event_class_ro_structured", "timebank_event_class", "ro", "Event class · Romanian (structured)", EventHint),
        new("timebank_event_tense_ro_structured", "timebank_event_tense", "ro", "Event tense · Romanian (structured)", EventHint),
        new("timebank_timex_type_ro_structured", "timebank_timex_type", "ro", "Time expression type · Romanian (structured)", TimexHint),
        new("timebank_tlink_ro_structured", "timebank_tlink", "ro", "Temporal link · Romanian (structured)", LinkHint),
        new("timebank_slink_ro_structured", "timebank_slink", "ro", "Subordination link · Romanian (structured)", LinkHint),
        new("timebank_alink_ro_structured", "timebank_alink", "ro", "Aspectual link · Romanian (structured)", LinkHint)
    ]);

    private static readonly IReadOnlyDictionary<string, ModelDefinition> ById =
        All.ToDictionary(model => model.ModelId, StringComparer.Ordinal);

    public static bool IsKnown(string? modelId) => modelId is not null && ById.ContainsKey(modelId);

    public static ModelDefinition Get(string modelId) => ById.TryGetValue(modelId, out var model)
        ? model
        : throw new ArgumentException("Choose a model_id from GET /v1/models.", nameof(modelId));
}
