using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.ML;
using Microsoft.ML.Data;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.WriteIndented = true;
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();
app.UseCors();

app.MapGet("/", () => Results.Json(new
{
    service = "tym-api",
    version = TymConstants.Version,
    extraction = "non-llm article-guided rules plus ML.NET seed classifiers plus TimeML-style temporal annotations",
    supportedLanguages = new[] { "en", "ro" },
    endpoints = new[]
    {
        "GET /health",
        "GET /openapi.yaml",
        "POST /v1/diagrams",
        "POST /v1/diagrams/svg",
        "POST /v1/diagrams/xml"
    }
}));

app.MapGet("/health", () => Results.Json(new { ok = true, service = "tym-api", version = TymConstants.Version }));

app.MapGet("/openapi.yaml", () =>
{
    var path = Path.Combine(app.Environment.ContentRootPath, "openapi.yaml");
    return File.Exists(path)
        ? Results.File(path, "application/yaml; charset=utf-8")
        : Results.NotFound(new { error = "openapi.yaml not found" });
});

app.MapPost("/v1/diagrams", (DiagramRequest request) =>
{
    var validation = Validate(request);
    return validation is not null
        ? Results.BadRequest(new ErrorResponse(validation))
        : Results.Json(TymAnalyzer.Analyze(request.Text, request.Options ?? new DiagramOptions()));
});

app.MapPost("/v1/diagrams/svg", (DiagramRequest request) =>
{
    var validation = Validate(request);
    if (validation is not null)
    {
        return Results.BadRequest(new ErrorResponse(validation));
    }

    var response = TymAnalyzer.Analyze(request.Text, request.Options ?? new DiagramOptions());
    return Results.Text(response.Render.Svg, "image/svg+xml; charset=utf-8");
});

app.MapPost("/v1/diagrams/xml", (DiagramRequest request) =>
{
    var validation = Validate(request);
    if (validation is not null)
    {
        return Results.BadRequest(new ErrorResponse(validation));
    }

    var response = TymAnalyzer.Analyze(request.Text, request.Options ?? new DiagramOptions());
    return Results.Text(response.Xml, "application/xml; charset=utf-8");
});

app.Run();

static string? Validate(DiagramRequest? request)
{
    if (request is null)
    {
        return "Request body must be a JSON object.";
    }

    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return "Request body must include a non-empty string field named 'text'.";
    }

    var layout = request.Options?.Layout;
    if (!string.IsNullOrWhiteSpace(layout) && layout is not ("both" or "text_order" or "time_yard"))
    {
        return "'options.layout' must be one of: both, text_order, time_yard.";
    }

    var language = request.Options?.Language;
    if (!string.IsNullOrWhiteSpace(language) && NormalizeLanguage(language) is null)
    {
        return "'options.language' must be one of: en, ro.";
    }

    return null;
}

static string? NormalizeLanguage(string language)
{
    return language.Trim().ToLowerInvariant() switch
    {
        "en" or "eng" or "english" => "en",
        "ro" or "ron" or "rom" or "romanian" or "română" or "romana" => "ro",
        _ => null
    };
}

public sealed record DiagramRequest(
    string Text,
    DiagramOptions? Options = null);

public sealed record DiagramOptions(
    string Layout = "both",
    int Width = 1200,
    string[]? HighlightEntities = null,
    bool IncludeDebug = false,
    string Language = "en");

public sealed record ErrorResponse(string Error);

public sealed record DiagramResponse(
    string Id,
    string ModelVersion,
    Diagram Diagram,
    RenderResult Render,
    string Xml,
    IReadOnlyList<string> Warnings);

public sealed record Diagram(
    IReadOnlyList<TimeActor> Actors,
    IReadOnlyList<TimeLocation> Locations,
    IReadOnlyList<EntityMention> EntityMentions,
    IReadOnlyList<NarrativeEvent> Events,
    IReadOnlyList<TimeTrack> Tracks,
    IReadOnlyList<TimeSegment> Segments,
    IReadOnlyList<Endpoint> Endpoints,
    IReadOnlyList<TimeRelation> Relations,
    IReadOnlyList<SegmentBoundary> Boundaries,
    TimeMlDocument TimeMl);

public sealed record TimeActor(
    string Id,
    string Name);

public sealed record TimeLocation(
    string Id,
    string Name,
    string ParentId);

public sealed record EntityMention(
    string Id,
    string Text,
    string Label,
    string Source,
    int SpanStart,
    int SpanEnd,
    string EventId);

public sealed record NarrativeEvent(
    string Id,
    string Text,
    IReadOnlyList<string> Actors,
    string TemporalAnchor,
    string Location,
    string Action,
    string TemporalCategory,
    string RelationToPrevious,
    string RelationCue,
    string RelationEvidence,
    int Order,
    int SpanStart,
    int SpanEnd,
    double Confidence,
    string Classifier);

public sealed record TimeTrack(
    string Id,
    string Name,
    string LeftEndpoint,
    string RightEndpoint);

public sealed record TimeSegment(
    string Id,
    string Text,
    string TrackId,
    string Type,
    string Perspective,
    int TextOrder,
    double StoryOrder,
    IReadOnlyList<string> Entities,
    IReadOnlyList<string> Actors,
    string LocationId,
    string TemporalAnchor,
    string TemporalCategory,
    int SpanStart,
    int SpanEnd,
    IReadOnlyList<string> EventIds,
    double Confidence,
    string Classifier);

public sealed record Endpoint(
    string Id,
    string Type,
    string Name,
    IReadOnlyList<string> TrackIds,
    string SegmentId);

public sealed record TimeRelation(
    string FromId,
    string Rel,
    string ToId,
    string Cue,
    string Evidence);

public sealed record SegmentBoundary(
    string Id,
    string Type,
    string? FromSegmentId,
    string? ToSegmentId,
    IReadOnlyList<string> TrackIds,
    string Reason);

public sealed record TimeMlDocument(
    IReadOnlyList<TimeMlEvent> Events,
    IReadOnlyList<TimeMlTimex3> Timex3,
    IReadOnlyList<TimeMlSignal> Signals,
    IReadOnlyList<TimeMlMakeInstance> MakeInstances,
    [property: JsonPropertyName("tlinks")]
    IReadOnlyList<TimeMlTLink> TLinks);

public sealed record TimeMlEvent(
    string Eid,
    string Class,
    string Text,
    int SpanStart,
    int SpanEnd,
    string TymEventId);

public sealed record TimeMlTimex3(
    string Tid,
    string Type,
    string Value,
    string Text,
    int SpanStart,
    int SpanEnd,
    string Source,
    string FunctionInDocument);

public sealed record TimeMlSignal(
    string Sid,
    string Text,
    int SpanStart,
    int SpanEnd,
    string EventId,
    string RelationHint);

public sealed record TimeMlMakeInstance(
    string Eiid,
    string EventId,
    string Tense,
    string Aspect,
    string Polarity,
    string Pos,
    string TymEventId);

public sealed record TimeMlTLink(
    string Lid,
    string RelType,
    string? EventInstanceId,
    string? RelatedToEventInstance,
    string? TimeId,
    string? RelatedToTime,
    string? SignalId,
    string Origin);

public sealed record RenderResult(
    string Format,
    int Width,
    int Height,
    string Svg);

internal static class TymConstants
{
    public const string Version = "tym-dotnet-paper-guided-nonllm-timeml-ro-v0.6";
}

internal sealed class MutableTrack
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string LeftEndpoint { get; set; } = "START";
    public string RightEndpoint { get; set; } = "STOP";

    public TimeTrack ToRecord() => new(Id, Name, LeftEndpoint, RightEndpoint);
}

internal sealed class MutableSegment
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required string TrackId { get; init; }
    public required string Type { get; init; }
    public required string Perspective { get; init; }
    public required int TextOrder { get; init; }
    public required double StoryOrder { get; init; }
    public required IReadOnlyList<string> Entities { get; init; }
    public required IReadOnlyList<string> Actors { get; init; }
    public required string LocationId { get; init; }
    public required string TemporalAnchor { get; init; }
    public required string TemporalCategory { get; init; }
    public required int SpanStart { get; init; }
    public required int SpanEnd { get; init; }
    public required IReadOnlyList<string> EventIds { get; init; }
    public required double Confidence { get; init; }
    public required string Classifier { get; init; }

    public TimeSegment ToRecord() => new(
        Id,
        Text,
        TrackId,
        Type,
        Perspective,
        TextOrder,
        StoryOrder,
        Entities,
        Actors,
        LocationId,
        TemporalAnchor,
        TemporalCategory,
        SpanStart,
        SpanEnd,
        EventIds,
        Confidence,
        Classifier);
}

internal sealed record SegmentClassification(string Label, double Confidence, string Source);

internal sealed record EventTemporalClassification(string Label, double Confidence, string Source);

internal sealed record TemporalCue(string Relation, string Cue, string Evidence);

internal sealed class SegmentTypeTrainingRow
{
    public string Text { get; set; } = "";
    public string Label { get; set; } = "";
}

internal sealed class SegmentTypePrediction
{
    [ColumnName("PredictedLabelText")]
    public string PredictedLabel { get; set; } = "";

    public float[]? Score { get; set; }
}

internal sealed class EventTemporalTrainingRow
{
    public string Text { get; set; } = "";
    public string Label { get; set; } = "";
}

internal sealed class EventTemporalPrediction
{
    [ColumnName("PredictedLabelText")]
    public string PredictedLabel { get; set; } = "";

    public float[]? Score { get; set; }
}

internal sealed class MutableNarrativeEvent
{
    public required string Id { get; init; }
    public required string Text { get; init; }
    public required IReadOnlyList<string> Actors { get; init; }
    public required string TemporalAnchor { get; init; }
    public required string Location { get; init; }
    public required string Action { get; init; }
    public required string TemporalCategory { get; init; }
    public required string RelationToPrevious { get; init; }
    public required string RelationCue { get; init; }
    public required string RelationEvidence { get; init; }
    public required int Order { get; init; }
    public required int SpanStart { get; init; }
    public required int SpanEnd { get; init; }
    public required double Confidence { get; init; }
    public required string Classifier { get; init; }

    public NarrativeEvent ToRecord() => new(
        Id,
        Text,
        Actors,
        TemporalAnchor,
        Location,
        Action,
        TemporalCategory,
        RelationToPrevious,
        RelationCue,
        RelationEvidence,
        Order,
        SpanStart,
        SpanEnd,
        Confidence,
        Classifier);
}

internal sealed class TimeSegmentCandidate
{
    public required string Text { get; init; }
    public required IReadOnlyList<MutableNarrativeEvent> Events { get; init; }
    public required IReadOnlyList<string> Actors { get; init; }
    public required string TemporalAnchor { get; init; }
    public required string Location { get; init; }
    public required string TemporalCategory { get; init; }
    public required int SpanStart { get; init; }
    public required int SpanEnd { get; init; }
}

internal sealed record ClauseSpan(string Text, int Start, int End);

internal sealed class TymSegmentTypeClassifier
{
    private const double MinimumModelConfidence = 0.60;

    private static readonly HashSet<string> KnownLabels = new(StringComparer.Ordinal)
    {
        "NAR", "REM", "SUP", "GEN", "FIC"
    };

    private readonly PredictionEngine<SegmentTypeTrainingRow, SegmentTypePrediction> _engine;
    private readonly object _predictionLock = new();

    private TymSegmentTypeClassifier(PredictionEngine<SegmentTypeTrainingRow, SegmentTypePrediction> engine)
    {
        _engine = engine;
    }

    public static TymSegmentTypeClassifier Train()
    {
        var mlContext = new MLContext(seed: 42);
        var trainingData = mlContext.Data.LoadFromEnumerable(SeedRows());
        var pipeline = mlContext.Transforms.Conversion.MapValueToKey("LabelKey", nameof(SegmentTypeTrainingRow.Label))
            .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(SegmentTypeTrainingRow.Text)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel"));

        var model = pipeline.Fit(trainingData);
        return new TymSegmentTypeClassifier(
            mlContext.Model.CreatePredictionEngine<SegmentTypeTrainingRow, SegmentTypePrediction>(model));
    }

    public SegmentClassification Predict(string text, string fallbackLabel)
    {
        try
        {
            SegmentTypePrediction prediction;
            lock (_predictionLock)
            {
                prediction = _engine.Predict(new SegmentTypeTrainingRow { Text = text });
            }

            var label = KnownLabels.Contains(prediction.PredictedLabel)
                ? prediction.PredictedLabel
                : fallbackLabel;
            var confidence = prediction.Score is { Length: > 0 }
                ? Math.Round(Math.Clamp(prediction.Score.Max(), 0f, 1f), 4)
                : 0.0;

            if (!KnownLabels.Contains(prediction.PredictedLabel))
            {
                return new SegmentClassification(fallbackLabel, 0.0, "heuristic_fallback_unknown_ml_label");
            }

            if (confidence < MinimumModelConfidence)
            {
                return new SegmentClassification(fallbackLabel, 0.0, "heuristic_fallback_low_ml_confidence");
            }

            if (fallbackLabel != "NAR" && label != fallbackLabel && confidence < 0.90)
            {
                return new SegmentClassification(fallbackLabel, 0.0, "heuristic_fallback_conflict");
            }

            var source = label == prediction.PredictedLabel ? "mlnet_seed_model" : "heuristic_fallback";
            return new SegmentClassification(label, confidence, source);
        }
        catch
        {
            return new SegmentClassification(fallbackLabel, 0.0, "heuristic_fallback");
        }
    }

    private static IEnumerable<SegmentTypeTrainingRow> SeedRows()
    {
        return
        [
            Row("Adam walked through the market.", "NAR"),
            Row("Margaret opened the door.", "NAR"),
            Row("Karl travelled across the island.", "NAR"),
            Row("The soldiers arrived before noon.", "NAR"),
            Row("She waited in the kitchen.", "NAR"),
            Row("He searched for Margaret after the storm.", "NAR"),
            Row("They entered the house together.", "NAR"),
            Row("The boy crossed the street and stopped near the gate.", "NAR"),
            Row("Adam and Johan grew up in the same house.", "NAR"),
            Row("Adam disappeared before dawn.", "NAR"),
            Row("Meanwhile, Karl traveled alone across the island.", "NAR"),

            Row("Years earlier, Karl had carried Adam through the rain.", "REM"),
            Row("Margaret remembered her mother.", "REM"),
            Row("She recalled the words from childhood.", "REM"),
            Row("Back then Adam lived in the orphanage.", "REM"),
            Row("Long ago he had met Johan.", "REM"),
            Row("He dreamed of his father's old house.", "REM"),
            Row("In the past, she had waited for him there.", "REM"),
            Row("The old voice returned from memory.", "REM"),

            Row("Perhaps Adam would find her.", "SUP"),
            Row("She imagined that Karl might return.", "SUP"),
            Row("If only he could have stayed.", "SUP"),
            Row("He supposed the road might be safe.", "SUP"),
            Row("They might meet again before dawn.", "SUP"),
            Row("She believed he would have escaped.", "SUP"),
            Row("Maybe the soldiers had already left.", "SUP"),
            Row("It could have happened another way.", "SUP"),

            Row("People usually remember home by its smells.", "GEN"),
            Row("Everyone knows that islands change slowly.", "GEN"),
            Row("The sea always takes back what it is given.", "GEN"),
            Row("Children generally fear dark rooms.", "GEN"),
            Row("A person never knows the end of a journey.", "GEN"),
            Row("Mothers usually wait for letters.", "GEN"),
            Row("Stories often preserve what families forget.", "GEN"),
            Row("Time always moves differently in exile.", "GEN"),

            Row("In the story the prince crossed a river.", "FIC"),
            Row("In the film the city burned all night.", "FIC"),
            Row("The fictional detective found a hidden map.", "FIC"),
            Row("The invented kingdom had no clocks.", "FIC"),
            Row("In the play Margaret spoke to a stranger.", "FIC"),
            Row("The novel described an impossible war.", "FIC"),
            Row("The movie showed a child walking through fire.", "FIC"),
            Row("Inside the tale, the island floated away.", "FIC")
        ];
    }

    private static SegmentTypeTrainingRow Row(string text, string label) => new()
    {
        Text = text,
        Label = label
    };
}

internal sealed class TymEventTemporalClassifier
{
    private const double MinimumModelConfidence = 0.55;

    private static readonly HashSet<string> KnownLabels = new(StringComparer.Ordinal)
    {
        "Past", "Present", "Future"
    };

    private readonly PredictionEngine<EventTemporalTrainingRow, EventTemporalPrediction> _engine;
    private readonly object _predictionLock = new();

    private TymEventTemporalClassifier(PredictionEngine<EventTemporalTrainingRow, EventTemporalPrediction> engine)
    {
        _engine = engine;
    }

    public static TymEventTemporalClassifier Train()
    {
        var mlContext = new MLContext(seed: 42);
        var trainingData = mlContext.Data.LoadFromEnumerable(SeedRows());
        var pipeline = mlContext.Transforms.Conversion.MapValueToKey("LabelKey", nameof(EventTemporalTrainingRow.Label))
            .Append(mlContext.Transforms.Text.FeaturizeText("Features", nameof(EventTemporalTrainingRow.Text)))
            .Append(mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("LabelKey", "Features"))
            .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel"));

        var model = pipeline.Fit(trainingData);
        return new TymEventTemporalClassifier(
            mlContext.Model.CreatePredictionEngine<EventTemporalTrainingRow, EventTemporalPrediction>(model));
    }

    public EventTemporalClassification Predict(string text, string fallbackLabel)
    {
        try
        {
            EventTemporalPrediction prediction;
            lock (_predictionLock)
            {
                prediction = _engine.Predict(new EventTemporalTrainingRow { Text = text });
            }

            if (!KnownLabels.Contains(prediction.PredictedLabel))
            {
                return new EventTemporalClassification(fallbackLabel, 0.0, "heuristic_fallback_unknown_ml_label");
            }

            var confidence = prediction.Score is { Length: > 0 }
                ? Math.Round(Math.Clamp(prediction.Score.Max(), 0f, 1f), 4)
                : 0.0;
            if (confidence < MinimumModelConfidence)
            {
                return new EventTemporalClassification(fallbackLabel, 0.0, "heuristic_fallback_low_ml_confidence");
            }

            if (fallbackLabel != "Present" && prediction.PredictedLabel != fallbackLabel)
            {
                return new EventTemporalClassification(fallbackLabel, 0.0, "heuristic_fallback_explicit_tense");
            }

            return new EventTemporalClassification(prediction.PredictedLabel, confidence, "mlnet_event_temporal_model");
        }
        catch
        {
            return new EventTemporalClassification(fallbackLabel, 0.0, "heuristic_fallback");
        }
    }

    private static IEnumerable<EventTemporalTrainingRow> SeedRows()
    {
        return
        [
            Row("Years earlier Karl had carried Adam through the rain", "Past"),
            Row("She remembered her mother in Jakarta", "Past"),
            Row("He was five years old", "Past"),
            Row("Before he came here", "Past"),
            Row("Long ago Margaret lived in another house", "Past"),
            Row("Karl had been imprisoned", "Past"),
            Row("The soldiers left yesterday", "Past"),
            Row("Adam recalled the orphanage", "Past"),

            Row("This is Adam", "Present"),
            Row("Now he is sixteen", "Present"),
            Row("She waits in the kitchen", "Present"),
            Row("Adam searches for Margaret", "Present"),
            Row("Margaret finds Adam at the doorway", "Present"),
            Row("Karl travels alone across the island", "Present"),
            Row("The narrator describes the house", "Present"),
            Row("They are walking to the truck", "Present"),

            Row("Adam will find Margaret", "Future"),
            Row("She would return before dawn", "Future"),
            Row("He is going to leave soon", "Future"),
            Row("Karl might come back", "Future"),
            Row("They shall meet tomorrow", "Future"),
            Row("The soldiers could arrive later", "Future"),
            Row("Margaret would have escaped", "Future"),
            Row("The child will remember this", "Future")
        ];
    }

    private static EventTemporalTrainingRow Row(string text, string label) => new()
    {
        Text = text,
        Label = label
    };
}

internal static partial class TymAnalyzer
{
    private static readonly Lazy<TymSegmentTypeClassifier> SegmentTypeClassifier = new(TymSegmentTypeClassifier.Train);
    private static readonly Lazy<TymEventTemporalClassifier> EventTemporalClassifier = new(TymEventTemporalClassifier.Train);

    private static readonly HashSet<string> EntityStopwords = new(StringComparer.Ordinal)
    {
        "A", "An", "And", "After", "At", "Before", "During", "Earlier", "Elsewhere",
        "Finally", "He", "Her", "His", "In", "Later", "Meanwhile", "On", "She",
        "The", "Then", "They", "This", "When", "While", "Years", "Now", "Last",
        "Tomorrow", "Today", "Yesterday", "From", "To"
    };

    private static readonly HashSet<string> RomanianEntityStopwords = new(StringComparer.Ordinal)
    {
        "A", "Al", "Ale", "Ai", "Acest", "Aceasta", "Apoi", "Atunci", "Ani", "An",
        "Astăzi", "Astazi", "Azi", "Când", "Cand", "Către", "Catre", "Cu", "Dar",
        "De", "Demult", "Din", "După", "Dupa", "Ea", "El", "Ele", "Ei", "În",
        "In", "Înainte", "Inainte", "Între", "Intre", "Ieri", "La", "Lui",
        "Mai", "Mâine", "Maine", "O", "Pe", "Pentru", "Prin", "Spre", "Și",
        "Si", "The", "Un", "Odinioară", "Odinioara"
    };

    public static DiagramResponse Analyze(string text, DiagramOptions options)
    {
        var language = NormalizeLanguageCode(options.Language);
        var events = ExtractEvents(text, language);
        var candidates = BuildTimeSegmentCandidates(events);
        var actors = BuildActors(events, language);
        var locations = BuildLocations(events, language);
        var entityMentions = BuildEntityMentions(events);
        var locationIdByName = locations.ToDictionary(location => location.Name, location => location.Id, StringComparer.OrdinalIgnoreCase);
        var tracks = new List<MutableTrack>();
        var trackByKey = new Dictionary<string, MutableTrack>(StringComparer.OrdinalIgnoreCase);
        var trackStoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var segments = new List<MutableSegment>();
        var endpoints = new List<Endpoint>();
        var relations = new List<TimeRelation>();
        var boundaries = new List<SegmentBoundary>();

        MutableSegment? previousSegment = null;
        MutableTrack? previousTrack = null;

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            var raw = candidate.Text;
            var fallbackSegmentType = HeuristicClassifySegment(raw, language);
            var classification = ClassifySegment(raw, fallbackSegmentType, language);
            var segmentType = classification.Label;
            var entities = candidate.Actors.Count > 0 ? candidate.Actors : ExtractEntities(raw, language);
            var perspective = InferPerspective(segmentType, raw, entities, language);
            var key = TrackKey(entities, segmentType, candidate.TemporalCategory, perspective, raw, language);

            if (!trackByKey.TryGetValue(key, out var track))
            {
                track = new MutableTrack
                {
                    Id = $"TT{tracks.Count + 1}",
                    Name = TrackName(entities, "Narrative")
                };
                trackByKey[key] = track;
                tracks.Add(track);
                trackStoryCounts[track.Id] = 0;
            }

            trackStoryCounts[track.Id] += 1;
            var storyOrder = (double)trackStoryCounts[track.Id];
            if (segmentType == "REM")
            {
                storyOrder -= 0.35;
            }

            var segment = new MutableSegment
            {
                Id = $"TS{index + 1}",
                Text = raw,
                TrackId = track.Id,
                Type = segmentType,
                Perspective = perspective,
                TextOrder = index + 1,
                StoryOrder = storyOrder,
                Entities = entities,
                Actors = candidate.Actors,
                LocationId = candidate.Location.Length > 0 && locationIdByName.TryGetValue(candidate.Location, out var locationId) ? locationId : "",
                TemporalAnchor = candidate.TemporalAnchor,
                TemporalCategory = candidate.TemporalCategory,
                SpanStart = candidate.SpanStart,
                SpanEnd = candidate.SpanEnd,
                EventIds = candidate.Events.Select(ev => ev.Id).ToList(),
                Confidence = classification.Confidence,
                Classifier = classification.Source
            };
            segments.Add(segment);

            if (previousSegment is null)
            {
                boundaries.Add(new SegmentBoundary(
                    $"B{boundaries.Count + 1}",
                    "START",
                    null,
                    segment.Id,
                    [track.Id],
                    "First TS in text order."));
            }

            if (previousSegment is not null)
            {
                AddSegmentTemporalRelation(relations, previousSegment, segment, candidate.Events.FirstOrDefault());
                if (track.Id != previousSegment.TrackId)
                {
                    var parallelMatch = ParallelMatch(raw, language);
                    var rel = parallelMatch.Success ? "SIMULTANEOUS" : "BEFORE";
                    relations.Add(new TimeRelation(
                        previousSegment.TrackId,
                        rel,
                        track.Id,
                        parallelMatch.Success ? parallelMatch.Value : "",
                        parallelMatch.Success
                            ? "Parallel cue triggered a cross-track relation."
                            : "Adjacent text segments belong to different time tracks."));
                    boundaries.Add(new SegmentBoundary(
                        $"B{boundaries.Count + 1}",
                        "RUPTURE",
                        previousSegment.Id,
                        segment.Id,
                        [previousSegment.TrackId, track.Id],
                        "Adjacent text segments belong to different time tracks."));
                }
                else if (previousSegment.Type != segment.Type ||
                    previousSegment.Perspective != segment.Perspective ||
                    previousSegment.TemporalCategory != segment.TemporalCategory)
                {
                    boundaries.Add(new SegmentBoundary(
                        $"B{boundaries.Count + 1}",
                        "COMMUTE",
                        previousSegment.Id,
                        segment.Id,
                        [track.Id],
                        "Story remains on the same time track but changes temporal category, TS type, or perspective."));
                }
            }

            if ((IsRomanian(language) ? RomanianJoinPattern() : JoinPattern()).IsMatch(raw) &&
                previousTrack is not null &&
                previousTrack.Id != track.Id)
            {
                var endpoint = new Endpoint(
                    $"EP{endpoints.Count + 1}",
                    "JOIN",
                    $"{previousTrack.Name}+{track.Name}",
                    [previousTrack.Id, track.Id],
                    segment.Id);
                endpoints.Add(endpoint);
                previousTrack.RightEndpoint = endpoint.Id;
                track.LeftEndpoint = endpoint.Id;
                boundaries.Add(new SegmentBoundary(
                    $"B{boundaries.Count + 1}",
                    "JOIN",
                    previousSegment?.Id,
                    segment.Id,
                    [previousTrack.Id, track.Id],
                    "Text indicates that separate developments meet and continue together."));
            }

            if ((IsRomanian(language) ? RomanianSplitPattern() : SplitPattern()).IsMatch(raw))
            {
                var splitTrackIds = previousTrack is not null && previousTrack.Id != track.Id
                    ? new[] { previousTrack.Id, track.Id }
                    : new[] { track.Id };
                var endpoint = new Endpoint(
                    $"EP{endpoints.Count + 1}",
                    "SPLIT",
                    $"{track.Name} split",
                    splitTrackIds,
                    segment.Id);
                endpoints.Add(endpoint);
                if (previousTrack is not null && previousTrack.Id != track.Id)
                {
                    previousTrack.RightEndpoint = endpoint.Id;
                    track.LeftEndpoint = endpoint.Id;
                }
                else
                {
                    track.RightEndpoint = endpoint.Id;
                }

                boundaries.Add(new SegmentBoundary(
                    $"B{boundaries.Count + 1}",
                    "SPLIT",
                    previousSegment?.Id,
                    segment.Id,
                    splitTrackIds,
                    "Text indicates separation, disappearance, or an actor continuing alone."));
            }

            previousSegment = segment;
            previousTrack = track;
        }

        if (segments.Count > 0)
        {
            var lastSegment = segments[^1];
            boundaries.Add(new SegmentBoundary(
                $"B{boundaries.Count + 1}",
                "STOP",
                lastSegment.Id,
                null,
                [lastSegment.TrackId],
                "Last TS in text order."));
        }

        var timeMl = BuildTimeMl(events, language);
        var diagram = new Diagram(
            actors,
            locations,
            entityMentions,
            events.Select(ev => ev.ToRecord()).ToList(),
            tracks.Select(track => track.ToRecord()).ToList(),
            segments.Select(segment => segment.ToRecord()).ToList(),
            endpoints,
            relations,
            boundaries,
            timeMl);
        var render = TymSvgRenderer.Render(diagram, options);
        var xml = TymXmlSerializer.Serialize(diagram);
        var warnings = new List<string>
        {
            "Time segment types follow the paper labels: NAR, REM, SUP, GEN, and FIC.",
            "The XML output follows the paper notation: TT-SECTION, TIME-SECTION, EP-SECTION, and inline TS tags.",
            "The diagram includes a TimeML-style layer with EVENT, TIMEX3, SIGNAL, MAKEINSTANCE, and TLINK annotations.",
            language == "ro"
                ? "Romanian extraction uses non-LLM rule profiles for Romanian entities, temporal cues, tense, and TimeML-like annotations."
                : "English extraction uses non-LLM rules plus ML.NET seed classifiers for event temporal category and TS type.",
            language == "ro"
                ? "Romanian mode intentionally avoids the English ML.NET seed classifiers; train Romanian annotated TYM/TimeML data for production use."
                : "ML.NET calculates event temporal category and TS type from bundled paper-guided seed examples; replace these with an annotated corpus for production use."
        };
        if (candidates.Count == 0)
        {
            warnings.Add("No text segments were detected.");
        }

        return new DiagramResponse(HashId(text), TymConstants.Version, diagram, render, xml, warnings);
    }

    private static string NormalizeLanguageCode(string language)
    {
        return language.Trim().ToLowerInvariant() switch
        {
            "ro" or "ron" or "rom" or "romanian" or "română" or "romana" => "ro",
            _ => "en"
        };
    }

    private static bool IsRomanian(string language) => language == "ro";

    private static SegmentClassification ClassifySegment(string text, string fallbackLabel, string language)
    {
        return IsRomanian(language)
            ? new SegmentClassification(fallbackLabel, 0.75, "romanian_rule_segment_type")
            : SegmentTypeClassifier.Value.Predict(text, fallbackLabel);
    }

    private static EventTemporalClassification ClassifyEventTemporal(string text, string fallbackLabel, string language)
    {
        return IsRomanian(language)
            ? new EventTemporalClassification(fallbackLabel, 0.75, "romanian_rule_event_temporal")
            : EventTemporalClassifier.Value.Predict(text, fallbackLabel);
    }

    private static MatchCollection VerbMatches(string text, string language)
    {
        return IsRomanian(language)
            ? RomanianVerbPattern().Matches(text)
            : VerbPattern().Matches(text);
    }

    private static bool HasVerb(string text, string language)
    {
        return IsRomanian(language)
            ? RomanianVerbPattern().IsMatch(text)
            : VerbPattern().IsMatch(text);
    }

    private static string[] EventBoundarySplit(string text, string language)
    {
        return IsRomanian(language)
            ? RomanianEventBoundaryPattern().Split(text)
            : EventBoundaryPattern().Split(text);
    }

    private static Match ParallelMatch(string text, string language)
    {
        return IsRomanian(language)
            ? RomanianParallelPattern().Match(text)
            : ParallelPattern().Match(text);
    }

    private static bool HasParallelCue(string text, string language)
    {
        return IsRomanian(language)
            ? RomanianParallelPattern().IsMatch(text)
            : ParallelPattern().IsMatch(text);
    }

    private static void AddSegmentTemporalRelation(
        List<TimeRelation> relations,
        MutableSegment previousSegment,
        MutableSegment segment,
        MutableNarrativeEvent? firstEvent)
    {
        var cue = firstEvent?.RelationCue ?? "";
        var evidence = firstEvent?.RelationEvidence ?? "Adjacent segment in text order.";
        var relation = firstEvent?.RelationToPrevious ?? "IMMEDIATELY_AFTER";

        switch (relation)
        {
            case "BEFORE":
                relations.Add(new TimeRelation(segment.Id, "BEFORE", previousSegment.Id, cue, evidence));
                break;
            case "SIMULTANEOUS":
                relations.Add(new TimeRelation(previousSegment.Id, "SIMULTANEOUS", segment.Id, cue, evidence));
                break;
            case "AFTER":
                relations.Add(new TimeRelation(previousSegment.Id, "BEFORE", segment.Id, cue, evidence));
                break;
            case "IMMEDIATELY_AFTER":
            default:
                relations.Add(new TimeRelation(previousSegment.Id, "IMMEDIATELY_BEFORE", segment.Id, cue, evidence));
                break;
        }
    }

    private static TimeMlDocument BuildTimeMl(IReadOnlyList<MutableNarrativeEvent> events, string language)
    {
        var timeMlEvents = new List<TimeMlEvent>();
        var timexes = new List<TimeMlTimex3>();
        var signals = new List<TimeMlSignal>();
        var makeInstances = new List<TimeMlMakeInstance>();
        var tlinks = new List<TimeMlTLink>();
        var timexIdByAnchor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var instanceIdByTymEvent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < events.Count; index++)
        {
            var ev = events[index];
            var eid = $"e{index + 1}";
            var eiid = $"ei{index + 1}";
            var action = ev.Action.Length > 0 ? ev.Action : ev.Text;
            var actionIndex = ev.Action.Length > 0
                ? ev.Text.IndexOf(ev.Action, StringComparison.OrdinalIgnoreCase)
                : -1;
            var actionStart = actionIndex >= 0 ? ev.SpanStart + actionIndex : ev.SpanStart;
            var actionEnd = actionIndex >= 0 ? actionStart + ev.Action.Length : ev.SpanEnd;

            timeMlEvents.Add(new TimeMlEvent(
                eid,
                InferTimeMlEventClass(ev.Text, ev.Action, language),
                action,
                actionStart,
                actionEnd,
                ev.Id));

            makeInstances.Add(new TimeMlMakeInstance(
                eiid,
                eid,
                MapTimeMlTense(ev.TemporalCategory),
                InferTimeMlAspect(ev.Text, language),
                InferTimeMlPolarity(ev.Text, language),
                ev.Action.Length > 0 ? "VERB" : "UNKNOWN",
                ev.Id));
            instanceIdByTymEvent[ev.Id] = eiid;

            if (ev.TemporalAnchor.Length > 0)
            {
                var timexId = GetOrAddTimex3(ev, timexes, timexIdByAnchor, language);
                tlinks.Add(new TimeMlTLink(
                    $"l{tlinks.Count + 1}",
                    "IS_INCLUDED",
                    eiid,
                    null,
                    null,
                    timexId,
                    null,
                    "event_temporal_anchor"));
            }

            if (index > 0 && instanceIdByTymEvent.TryGetValue(events[index - 1].Id, out var previousEiid))
            {
                var signalId = AddSignalIfPresent(ev, signals);
                var (from, relation, to) = MapTimeMlEventRelation(ev.RelationToPrevious, previousEiid, eiid);
                tlinks.Add(new TimeMlTLink(
                    $"l{tlinks.Count + 1}",
                    relation,
                    from,
                    to,
                    null,
                    null,
                    signalId,
                    signalId is null ? "event_text_order" : "event_relation_signal"));
            }
        }

        return new TimeMlDocument(timeMlEvents, timexes, signals, makeInstances, tlinks);
    }

    private static string GetOrAddTimex3(
        MutableNarrativeEvent ev,
        List<TimeMlTimex3> timexes,
        Dictionary<string, string> timexIdByAnchor,
        string language)
    {
        var key = ev.TemporalAnchor.Trim();
        if (timexIdByAnchor.TryGetValue(key, out var existingId))
        {
            return existingId;
        }

        var localIndex = ev.Text.IndexOf(ev.TemporalAnchor, StringComparison.OrdinalIgnoreCase);
        var isExplicit = localIndex >= 0;
        var start = isExplicit ? ev.SpanStart + localIndex : ev.SpanStart;
        var end = isExplicit ? start + ev.TemporalAnchor.Length : start;
        var tid = $"t{timexes.Count + 1}";
        timexes.Add(new TimeMlTimex3(
            tid,
            InferTimex3Type(ev.TemporalAnchor, language),
            NormalizeTimex3Value(ev.TemporalAnchor, ev.TemporalCategory, language),
            ev.TemporalAnchor,
            start,
            end,
            isExplicit ? "explicit_temporal_expression" : "inherited_temporal_anchor",
            "NONE"));
        timexIdByAnchor[key] = tid;
        return tid;
    }

    private static string? AddSignalIfPresent(MutableNarrativeEvent ev, List<TimeMlSignal> signals)
    {
        if (ev.RelationCue.Length == 0)
        {
            return null;
        }

        var localIndex = ev.Text.IndexOf(ev.RelationCue, StringComparison.OrdinalIgnoreCase);
        var start = localIndex >= 0 ? ev.SpanStart + localIndex : ev.SpanStart;
        var end = localIndex >= 0 ? start + ev.RelationCue.Length : start;
        var sid = $"s{signals.Count + 1}";
        signals.Add(new TimeMlSignal(sid, ev.RelationCue, start, end, ev.Id, ev.RelationToPrevious));
        return sid;
    }

    private static (string From, string Relation, string To) MapTimeMlEventRelation(
        string relationToPrevious,
        string previousEiid,
        string currentEiid)
    {
        return relationToPrevious switch
        {
            "BEFORE" => (currentEiid, "BEFORE", previousEiid),
            "SIMULTANEOUS" => (previousEiid, "SIMULTANEOUS", currentEiid),
            "AFTER" => (previousEiid, "BEFORE", currentEiid),
            "IMMEDIATELY_AFTER" => (previousEiid, "IBEFORE", currentEiid),
            _ => (previousEiid, "IBEFORE", currentEiid)
        };
    }

    private static List<string> SplitSegments(string text)
    {
        var cleaned = WhitespacePattern().Replace(text, " ").Trim();
        if (cleaned.Length == 0)
        {
            return [];
        }

        return SentenceBoundaryPattern()
            .Split(cleaned)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    private static List<MutableNarrativeEvent> ExtractEvents(string text, string language)
    {
        var events = new List<MutableNarrativeEvent>();
        var previousActors = Array.Empty<string>();
        var previousTemporalAnchor = "";

        foreach (Match sentenceMatch in SentenceWithOffsetPattern().Matches(text))
        {
            var sentence = sentenceMatch.Value.Trim();
            if (sentence.Length == 0)
            {
                continue;
            }

            var sentenceStart = sentenceMatch.Index + sentenceMatch.Value.IndexOf(sentence, StringComparison.Ordinal);
            var clauses = VerbMatches(sentence, language).Count > 1
                ? SplitEventClauses(sentence, sentenceStart, language)
                : [new ClauseSpan(sentence, sentenceStart, sentenceStart + sentence.Length)];
            var pendingPrefix = "";
            var pendingPrefixStart = sentenceStart;

            foreach (var clause in clauses)
            {
                var eventText = clause.Text.Trim();
                if (eventText.Length == 0)
                {
                    continue;
                }

                if (!HasVerb(eventText, language))
                {
                    if (pendingPrefix.Length == 0)
                    {
                        pendingPrefixStart = clause.Start;
                    }

                    pendingPrefix = pendingPrefix.Length == 0
                        ? eventText
                        : $"{pendingPrefix} {eventText}";
                    continue;
                }

                var eventStart = clause.Start;
                if (pendingPrefix.Length > 0)
                {
                    eventText = $"{pendingPrefix}, {eventText}";
                    eventStart = pendingPrefixStart;
                    pendingPrefix = "";
                }

                var actors = ExtractEntities(eventText, language)
                    .Where(entity => !LooksLikeTemporalEntity(entity, language))
                    .Where(entity => !LooksLikeLocationEntity(eventText, entity, language))
                    .ToList();
                if (actors.Count == 0 && previousActors.Length > 0)
                {
                    actors = previousActors.ToList();
                }

                var temporalAnchor = ExtractTemporalAnchor(eventText, language);
                if (temporalAnchor.Length == 0 && previousTemporalAnchor.Length > 0)
                {
                    temporalAnchor = previousTemporalAnchor;
                }

                var fallbackTemporalCategory = HeuristicTemporalCategory(eventText, language);
                var temporalClassification = ClassifyEventTemporal(eventText, fallbackTemporalCategory, language);
                var location = ExtractLocation(eventText, language);
                var action = ExtractAction(eventText, language);
                var temporalCue = InferTemporalCue(eventText, events.Count == 0, language);

                events.Add(new MutableNarrativeEvent
                {
                    Id = $"EV{events.Count + 1}",
                    Text = eventText,
                    Actors = actors,
                    TemporalAnchor = temporalAnchor,
                    Location = location,
                    Action = action,
                    TemporalCategory = temporalClassification.Label,
                    RelationToPrevious = temporalCue.Relation,
                    RelationCue = temporalCue.Cue,
                    RelationEvidence = temporalCue.Evidence,
                    Order = events.Count + 1,
                    SpanStart = eventStart,
                    SpanEnd = clause.End,
                    Confidence = temporalClassification.Confidence,
                    Classifier = temporalClassification.Source
                });

                if (actors.Count > 0)
                {
                    previousActors = actors.ToArray();
                }

                if (temporalAnchor.Length > 0)
                {
                    previousTemporalAnchor = temporalAnchor;
                }
            }
        }

        return events;
    }

    private static List<ClauseSpan> SplitEventClauses(string sentence, int sentenceStart, string language)
    {
        var spans = new List<ClauseSpan>();
        var searchStart = 0;
        foreach (var part in EventBoundarySplit(sentence, language))
        {
            var trimmed = part.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var localStart = sentence.IndexOf(trimmed, searchStart, StringComparison.Ordinal);
            if (localStart < 0)
            {
                localStart = searchStart;
            }

            var start = sentenceStart + localStart;
            var end = start + trimmed.Length;
            spans.Add(new ClauseSpan(trimmed, start, end));
            searchStart = Math.Min(sentence.Length, localStart + trimmed.Length);
        }

        return spans.Count > 0 ? spans : [new ClauseSpan(sentence, sentenceStart, sentenceStart + sentence.Length)];
    }

    private static List<TimeSegmentCandidate> BuildTimeSegmentCandidates(IReadOnlyList<MutableNarrativeEvent> events)
    {
        var candidates = new List<TimeSegmentCandidate>();
        var current = new List<MutableNarrativeEvent>();

        foreach (var currentEvent in events)
        {
            if (current.Count == 0)
            {
                current.Add(currentEvent);
                continue;
            }

            var previous = current[^1];
            if (previous.TemporalCategory == currentEvent.TemporalCategory &&
                SameSet(previous.Actors, currentEvent.Actors))
            {
                current.Add(currentEvent);
                continue;
            }

            candidates.Add(CreateCandidate(current));
            current = [currentEvent];
        }

        if (current.Count > 0)
        {
            candidates.Add(CreateCandidate(current));
        }

        return candidates;
    }

    private static TimeSegmentCandidate CreateCandidate(IReadOnlyList<MutableNarrativeEvent> events)
    {
        var actors = events.SelectMany(ev => ev.Actors).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var temporalAnchor = events.Select(ev => ev.TemporalAnchor).FirstOrDefault(anchor => anchor.Length > 0) ?? "";
        var location = events.Select(ev => ev.Location).FirstOrDefault(location => location.Length > 0) ?? "unknown";
        return new TimeSegmentCandidate
        {
            Text = string.Join(" ", events.Select(ev => ev.Text)),
            Events = events.ToList(),
            Actors = actors,
            TemporalAnchor = temporalAnchor,
            Location = location,
            TemporalCategory = events[0].TemporalCategory,
            SpanStart = events.Min(ev => ev.SpanStart),
            SpanEnd = events.Max(ev => ev.SpanEnd)
        };
    }

    private static IReadOnlyList<TimeActor> BuildActors(IReadOnlyList<MutableNarrativeEvent> events, string language)
    {
        var actors = new List<TimeActor> { new("A0", IsRomanian(language) ? "Narator" : "Narrator") };
        actors.AddRange(events
            .SelectMany(ev => ev.Actors)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => new TimeActor($"A{index + 1}", name)));
        return actors;
    }

    private static IReadOnlyList<TimeLocation> BuildLocations(IReadOnlyList<MutableNarrativeEvent> events, string language)
    {
        var unknown = IsRomanian(language) ? "necunoscut" : "unknown";
        var locations = new List<TimeLocation> { new("L1", unknown, "") };
        locations.AddRange(events
            .Select(ev => ev.Location)
            .Where(location => location.Length > 0 &&
                !location.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                !location.Equals("necunoscut", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => new TimeLocation($"L{index + 2}", name, "")));
        return locations;
    }

    private static IReadOnlyList<EntityMention> BuildEntityMentions(IReadOnlyList<MutableNarrativeEvent> events)
    {
        var mentions = new List<EntityMention>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in events)
        {
            foreach (var actor in ev.Actors)
            {
                AddEntityMention(mentions, seen, ev, actor, "PERSON", "capitalized_actor_mention", "carried_actor_context");
            }

            AddEntityMention(mentions, seen, ev, ev.Location, "LOCATION", "preposition_location_pattern");
            AddEntityMention(mentions, seen, ev, ev.TemporalAnchor, "DATE_TIME", "temporal_expression_pattern");
            AddEntityMention(mentions, seen, ev, ev.Action, "EVENT_ACTION", "verb_pattern");
        }

        return mentions;
    }

    private static void AddEntityMention(
        List<EntityMention> mentions,
        HashSet<string> seen,
        MutableNarrativeEvent ev,
        string text,
        string label,
        string exactSource,
        string inferredSource = "")
    {
        if (string.IsNullOrWhiteSpace(text) || text.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var localIndex = ev.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase);
        if (localIndex < 0 && inferredSource.Length == 0)
        {
            return;
        }

        var start = localIndex >= 0 ? ev.SpanStart + localIndex : ev.SpanStart;
        var end = localIndex >= 0 ? start + text.Length : start;
        var source = localIndex >= 0 ? exactSource : inferredSource;
        var key = $"{ev.Id}|{label}|{start}|{end}|{text}";
        if (!seen.Add(key))
        {
            return;
        }

        mentions.Add(new EntityMention($"EM{mentions.Count + 1}", text, label, source, start, end, ev.Id));
    }

    private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count &&
            left.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(right.OrderBy(item => item, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTemporalEntity(string entity, string language)
    {
        return IsRomanian(language)
            ? RomanianTemporalEntityPattern().IsMatch(entity)
            : TemporalEntityPattern().IsMatch(entity);
    }

    private static bool LooksLikeLocationEntity(string text, string entity, string language)
    {
        var prefix = IsRomanian(language)
            ? @"(?:în|in|la|spre|din|lângă|langa|aproape\s+de|către|catre)"
            : @"(?:in|at|on|to|from|inside|outside|near)";
        var article = IsRomanian(language) ? @"(?:un|o|același|aceeași|acelasi|aceeasi|the)?\s*" : @"(?:the\s+)?";
        return Regex.IsMatch(text, $@"\b{prefix}\s+{article}{Regex.Escape(entity)}\b", RegexOptions.IgnoreCase);
    }

    private static string ExtractTemporalAnchor(string text, string language)
    {
        var match = IsRomanian(language)
            ? RomanianTemporalAnchorPattern().Match(text)
            : TemporalAnchorPattern().Match(text);
        return match.Success ? match.Value.Trim() : "";
    }

    private static TemporalCue InferTemporalCue(string text, bool isFirstEvent, string language)
    {
        if (isFirstEvent)
        {
            return new TemporalCue("NONE", "", "First event in text order.");
        }

        var parallel = IsRomanian(language) ? RomanianParallelPattern().Match(text) : ParallelPattern().Match(text);
        if (parallel.Success)
        {
            return new TemporalCue("SIMULTANEOUS", parallel.Value, "Parallel cue places this event alongside the previous event.");
        }

        var retrospective = IsRomanian(language) ? RomanianRetrospectiveCuePattern().Match(text) : RetrospectiveCuePattern().Match(text);
        if (retrospective.Success)
        {
            return new TemporalCue("BEFORE", retrospective.Value, "Retrospective cue places this event earlier than the previous event.");
        }

        var sequential = IsRomanian(language) ? RomanianSequentialCuePattern().Match(text) : SequentialCuePattern().Match(text);
        if (sequential.Success)
        {
            return new TemporalCue("IMMEDIATELY_AFTER", sequential.Value, "Sequential cue places this event immediately after the previous event.");
        }

        var forward = IsRomanian(language) ? RomanianForwardCuePattern().Match(text) : ForwardCuePattern().Match(text);
        if (forward.Success)
        {
            return new TemporalCue("AFTER", forward.Value, "Forward cue places this event after the previous event.");
        }

        return new TemporalCue("IMMEDIATELY_AFTER", "", "No explicit cue; defaulting to text-order adjacency.");
    }

    private static string InferTimeMlEventClass(string text, string action, string language)
    {
        if ((IsRomanian(language) ? RomanianPerceptionPattern() : PerceptionPattern()).IsMatch(text))
        {
            return "PERCEPTION";
        }

        if ((IsRomanian(language) ? RomanianReportingPattern() : ReportingPattern()).IsMatch(text))
        {
            return "REPORTING";
        }

        if ((IsRomanian(language) ? RomanianIntentionalActionPattern() : IntentionalActionPattern()).IsMatch(text))
        {
            return "I_ACTION";
        }

        if ((IsRomanian(language) ? RomanianRememberPattern() : RememberPattern()).IsMatch(text) ||
            (IsRomanian(language) ? RomanianCognitiveStatePattern() : CognitiveStatePattern()).IsMatch(text))
        {
            return "I_STATE";
        }

        if ((IsRomanian(language) ? RomanianStatePattern() : StatePattern()).IsMatch(action.Length > 0 ? action : text))
        {
            return "STATE";
        }

        return "OCCURRENCE";
    }

    private static string MapTimeMlTense(string temporalCategory)
    {
        return temporalCategory switch
        {
            "Past" => "PAST",
            "Future" => "FUTURE",
            "Present" => "PRESENT",
            _ => "NONE"
        };
    }

    private static string InferTimeMlAspect(string text, string language)
    {
        if ((IsRomanian(language) ? RomanianPerfectiveAspectPattern() : PerfectiveAspectPattern()).IsMatch(text))
        {
            return "PERFECTIVE";
        }

        if ((IsRomanian(language) ? RomanianProgressiveAspectPattern() : ProgressiveAspectPattern()).IsMatch(text))
        {
            return "PROGRESSIVE";
        }

        return "NONE";
    }

    private static string InferTimeMlPolarity(string text, string language)
    {
        return (IsRomanian(language) ? RomanianNegativePolarityPattern() : NegativePolarityPattern()).IsMatch(text)
            ? "NEG"
            : "POS";
    }

    private static string InferTimex3Type(string text, string language)
    {
        if ((IsRomanian(language) ? RomanianDurationPattern() : DurationPattern()).IsMatch(text))
        {
            return "DURATION";
        }

        if ((IsRomanian(language) ? RomanianClockTimePattern() : ClockTimePattern()).IsMatch(text))
        {
            return "TIME";
        }

        if ((IsRomanian(language) ? RomanianSetTimePattern() : SetTimePattern()).IsMatch(text))
        {
            return "SET";
        }

        return "DATE";
    }

    private static string NormalizeTimex3Value(string text, string temporalCategory, string language)
    {
        var normalized = text.Trim().ToLowerInvariant();
        var year = YearPattern().Match(normalized);
        if (year.Success)
        {
            return year.Value;
        }

        if (normalized.Contains("today", StringComparison.Ordinal) ||
            normalized.Contains("now", StringComparison.Ordinal) ||
            normalized.Contains("astăzi", StringComparison.Ordinal) ||
            normalized.Contains("astazi", StringComparison.Ordinal) ||
            normalized.Contains("azi", StringComparison.Ordinal) ||
            normalized.Contains("acum", StringComparison.Ordinal))
        {
            return "PRESENT_REF";
        }

        if (normalized.Contains("tomorrow", StringComparison.Ordinal) ||
            normalized.Contains("next", StringComparison.Ordinal) ||
            normalized.Contains("later", StringComparison.Ordinal) ||
            normalized.Contains("mâine", StringComparison.Ordinal) ||
            normalized.Contains("maine", StringComparison.Ordinal) ||
            normalized.Contains("mai târziu", StringComparison.Ordinal) ||
            normalized.Contains("mai tarziu", StringComparison.Ordinal) ||
            normalized.Contains("viitor", StringComparison.Ordinal))
        {
            return "FUTURE_REF";
        }

        if (normalized.Contains("yesterday", StringComparison.Ordinal) ||
            normalized.Contains("earlier", StringComparison.Ordinal) ||
            normalized.Contains("ago", StringComparison.Ordinal) ||
            normalized.Contains("before", StringComparison.Ordinal) ||
            normalized.Contains("long ago", StringComparison.Ordinal) ||
            normalized.Contains("back then", StringComparison.Ordinal) ||
            normalized.Contains("ieri", StringComparison.Ordinal) ||
            normalized.Contains("înainte", StringComparison.Ordinal) ||
            normalized.Contains("inainte", StringComparison.Ordinal) ||
            normalized.Contains("urmă", StringComparison.Ordinal) ||
            normalized.Contains("urma", StringComparison.Ordinal) ||
            normalized.Contains("demult", StringComparison.Ordinal) ||
            normalized.Contains("odinioară", StringComparison.Ordinal) ||
            normalized.Contains("odinioara", StringComparison.Ordinal))
        {
            return "PAST_REF";
        }

        return temporalCategory switch
        {
            "Past" => "PAST_REF",
            "Future" => "FUTURE_REF",
            _ => "PRESENT_REF"
        };
    }

    private static string ExtractLocation(string text, string language)
    {
        var match = IsRomanian(language) ? RomanianLocationPattern().Match(text) : LocationPattern().Match(text);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        if (IsRomanian(language))
        {
            var peMatch = RomanianPeLocationPattern().Match(text);
            if (peMatch.Success)
            {
                return peMatch.Groups[1].Value.Trim();
            }
        }

        return "";
    }

    private static string ExtractAction(string text, string language)
    {
        var match = IsRomanian(language) ? RomanianVerbPattern().Match(text) : VerbPattern().Match(text);
        return match.Success ? match.Value : "";
    }

    private static string HeuristicTemporalCategory(string text, string language)
    {
        if ((IsRomanian(language) ? RomanianFutureTensePattern() : FutureTensePattern()).IsMatch(text))
        {
            return "Future";
        }

        if ((IsRomanian(language) ? RomanianPastTensePattern() : PastTensePattern()).IsMatch(text))
        {
            return "Past";
        }

        return "Present";
    }

    private static string HeuristicClassifySegment(string text, string language)
    {
        if ((IsRomanian(language) ? RomanianRememberPattern() : RememberPattern()).IsMatch(text))
        {
            return "REM";
        }

        if ((IsRomanian(language) ? RomanianSuppositionPattern() : SuppositionPattern()).IsMatch(text))
        {
            return "SUP";
        }

        if ((IsRomanian(language) ? RomanianGeneralPattern() : GeneralPattern()).IsMatch(text))
        {
            return "GEN";
        }

        if ((IsRomanian(language) ? RomanianFictionPattern() : FictionPattern()).IsMatch(text))
        {
            return "FIC";
        }

        return "NAR";
    }

    private static IReadOnlyList<string> ExtractEntities(string text, string language)
    {
        var entities = new List<string>();
        var stopwords = IsRomanian(language) ? RomanianEntityStopwords : EntityStopwords;
        var matches = IsRomanian(language) ? RomanianEntityPattern().Matches(text) : EntityPattern().Matches(text);
        foreach (Match match in matches)
        {
            var candidate = match.Value;
            var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.All(part => stopwords.Contains(part)))
            {
                continue;
            }

            if (!entities.Contains(candidate, StringComparer.Ordinal))
            {
                entities.Add(candidate);
            }
        }

        return entities;
    }

    private static string InferPerspective(string segmentType, string text, IReadOnlyList<string> entities, string language)
    {
        if (segmentType == "REM" && entities.Count > 0)
        {
            return entities[0];
        }

        var stopwords = IsRomanian(language) ? RomanianEntityStopwords : EntityStopwords;
        var match = IsRomanian(language) ? RomanianPerspectivePattern().Match(text) : PerspectivePattern().Match(text);
        return match.Success && !stopwords.Contains(match.Groups[1].Value)
            ? match.Groups[1].Value
            : IsRomanian(language) ? "narator" : "narrator";
    }

    private static string TrackKey(
        IReadOnlyList<string> entities,
        string segmentType,
        string temporalCategory,
        string perspective,
        string text,
        string language)
    {
        if (HasParallelCue(text, language))
        {
            return $"parallel:{TrackName(entities, "Narrative")}:{temporalCategory}:{perspective}";
        }

        return segmentType == "REM"
            ? $"memory:{TrackName(entities, "Past")}:{temporalCategory}:{perspective}"
            : $"main:{TrackName(entities, "Narrative")}:{temporalCategory}:{perspective}";
    }

    private static string TrackName(IReadOnlyList<string> entities, string fallback)
    {
        return entities.Count > 0
            ? string.Join("&", entities.Take(3))
            : fallback;
    }

    private static string HashId(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceBoundaryPattern();

    [GeneratedRegex(@"[^.!?]+[.!?]?")]
    private static partial Regex SentenceWithOffsetPattern();

    [GeneratedRegex(@"[,;]\s+|\s+\b(?:and then|and|but|or|yet|so|when|while|because|then)\b\s+", RegexOptions.IgnoreCase)]
    private static partial Regex EventBoundaryPattern();

    [GeneratedRegex(@"\b(?:am|is|are|was|were|be|been|being|have|has|had|do|does|did|will|shall|would|could|might|must|can|grew|grow|grows|growing|came|come|go|went|walks?|walked|walking|travels?|traveled|travelled|traveling|searches?|searched|searching|finds?|found|finding|remembers?|remembered|remembering|recalls?|recalled|recalling|lives?|lived|living|opens?|opened|opening|disappears?|disappeared|disappearing|left|leave|leaves|leaving|met|meet|meets|meeting|looks?|looked|looking|waits?|waited|waiting|carried|carry|carries|carrying|jumps?|jumped|jumping|climbs?|climbed|climbing|follows?|followed|following|think|thinks|thought|saw|see|sees|seeing|entered|enter|enters|entering|\w+(?:ed|ing))\b", RegexOptions.IgnoreCase)]
    private static partial Regex VerbPattern();

    [GeneratedRegex(@"\b(will|shall|would|might|could|going to|tomorrow|later|soon|future)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FutureTensePattern();

    [GeneratedRegex(@"\b(had|was|were|did|grew|found|saw|came|went|left|met|thought|remembered|recalled|dreamed|years earlier|earlier|long ago|back then|ago|yesterday|last\s+\w+|before\b|\w+ed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PastTensePattern();

    [GeneratedRegex(@"\b(?:now|today|yesterday|tomorrow|earlier|years earlier|last\s+\w+|next\s+\w+|before\s+(?:the\s+)?\w+|after\s+(?:the\s+)?\w+|\d+\s+(?:year|years|month|months|week|weeks|day|days|hour|hours)\s+(?:old|ago)|\d{4}|Christmas|Thanksgiving)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TemporalAnchorPattern();

    [GeneratedRegex(@"\b(?:in|at|on|to|from|inside|outside|near)\s+(the\s+[a-z]+(?:\s+[a-z]+)?|a\s+[a-z]+(?:\s+[a-z]+)?|[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex LocationPattern();

    [GeneratedRegex(@"\b(Now|Last|Tomorrow|Today|Yesterday|Christmas|Thanksgiving|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday|January|February|March|April|May|June|July|August|September|October|November|December)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TemporalEntityPattern();

    [GeneratedRegex(@"\b(remembered|recalled|dreamed|years earlier|long ago|back then|childhood|past)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RememberPattern();

    [GeneratedRegex(@"\b(saw|seen|heard|felt|noticed|watched|observed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PerceptionPattern();

    [GeneratedRegex(@"\b(said|told|asked|answered|reported|announced|wrote|explained)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportingPattern();

    [GeneratedRegex(@"\b(tried|attempted|decided|planned|promised|agreed|refused|started|began)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IntentionalActionPattern();

    [GeneratedRegex(@"\b(thought|believed|knew|wanted|imagined|hoped|feared|wondered|supposed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CognitiveStatePattern();

    [GeneratedRegex(@"\b(am|is|are|was|were|be|been|being|lives?|lived|knows?|knew|has|had)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StatePattern();

    [GeneratedRegex(@"\b(might|perhaps|supposed|imagined|could have|would have|if only)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SuppositionPattern();

    [GeneratedRegex(@"\b(always|never|generally|usually|everyone knows)\b", RegexOptions.IgnoreCase)]
    private static partial Regex GeneralPattern();

    [GeneratedRegex(@"\b(in the story|in the play|in the film|fictional|invented)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FictionPattern();

    [GeneratedRegex(@"\b(met|found|joined|reunited|came together|encountered)\b", RegexOptions.IgnoreCase)]
    private static partial Regex JoinPattern();

    [GeneratedRegex(@"\b(left|departed|separated|split|alone|disappeared|lost)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SplitPattern();

    [GeneratedRegex(@"\b(meanwhile|elsewhere|at the same time|simultaneously|in another)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ParallelPattern();

    [GeneratedRegex(@"\b(years earlier|earlier|long ago|back then|previously|formerly|had\s+(?:been\s+)?\w+(?:ed|en)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RetrospectiveCuePattern();

    [GeneratedRegex(@"\b(and then|then|subsequently|next)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SequentialCuePattern();

    [GeneratedRegex(@"\b(later|afterward|afterwards|eventually|finally|after\s+(?:the\s+)?\w+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForwardCuePattern();

    [GeneratedRegex(@"\b(?:had|has|have)\s+(?:been\s+)?\w+(?:ed|en)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PerfectiveAspectPattern();

    [GeneratedRegex(@"\b(?:am|is|are|was|were|be|been|being)\s+\w+ing\b", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressiveAspectPattern();

    [GeneratedRegex(@"\b(?:not|never|no|n't)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NegativePolarityPattern();

    [GeneratedRegex(@"\b(?:for\s+)?\d+\s+(?:second|seconds|minute|minutes|hour|hours|day|days|week|weeks|month|months|year|years)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"\b(?:dawn|noon|midnight|night|morning|evening|\d{1,2}:\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClockTimePattern();

    [GeneratedRegex(@"\b(?:always|usually|often|every\s+\w+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SetTimePattern();

    [GeneratedRegex(@"\b\d{4}\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b")]
    private static partial Regex EntityPattern();

    [GeneratedRegex(@"\b([A-Z][a-z]+)\s+(wondered|thought|remembered|imagined|believed|saw|heard)\b")]
    private static partial Regex PerspectivePattern();

    [GeneratedRegex(@"[,;]\s+|\s+\b(?:și apoi|si apoi|și|si|dar|însă|insa|apoi|atunci|când|cand|în timp ce|in timp ce|pentru că|pentru ca|deoarece)\b\s+", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianEventBoundaryPattern();

    [GeneratedRegex(@"\b(?:este|sunt|era|erau|fost|fi|are|avea|aveau|avut|va|vor|voi|vei|vom|veți|veti|merge|mergea|mers|mersese|pleacă|pleaca|plecat|plecase|caută|cauta|căuta|căutat|cautat|găsește|gaseste|găsit|gasit|își amintește|isi aminteste|își amintea|isi amintea|amintit|trăiește|traieste|locuiește|locuieste|locuia|dispărut|disparut|dispare|spune|spus|vede|văzut|vazut|aude|auzit|întâlnește|intalneste|întâlnit|intalnit|așteaptă|asteapta|așteptat|asteptat|călătorește|calatoreste|călătorit|calatorit|duce|dus|intră|intra|intrat|deschide|deschis|urcă|urca|urcat|urmează|urmeaza|urmărit|urmarit|crește|creste|crescut|gândește|gandeste|gândit|gandit|crede|crezut|\w+(?:at|it|ut|ase|ese|ise|use|ează|eaza|esc|ește|ind|ând|and))\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianVerbPattern();

    [GeneratedRegex(@"\b(?:va|vor|voi|vei|vom|veți|veti|o\s+să|o\s+sa|mâine|maine|mai\s+târziu|mai\s+tarziu|viitor|urmează\s+să|urmeaza\s+sa)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianFutureTensePattern();

    [GeneratedRegex(@"\b(?:era|erau|fusese|fuseseră|fusesera|plecase|mersese|găsit|gasit|văzut|vazut|spus|amintit|căutat|cautat|crescut|dus|ieri|odinioară|odinioara|demult|înainte|inainte|cu\s+(?:\d+\s+\w+|ani|mult\s+timp)\s+(?:în\s+urmă|in\s+urma)|\w+(?:ase|ese|ise|use|at|it|ut))\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianPastTensePattern();

    [GeneratedRegex(@"\b(?:acum|astăzi|astazi|azi|ieri|mâine|maine|mai\s+devreme|mai\s+târziu|mai\s+tarziu|odinioară|odinioara|demult|cândva|candva|înainte(?:\s+de\s+\w+)?|inainte(?:\s+de\s+\w+)?|după(?:\s+de\s+\w+|\s+\w+)?|dupa(?:\s+de\s+\w+|\s+\w+)?|cu\s+(?:\d+\s+(?:ani|luni|zile|ore)|ani|mult\s+timp)\s+(?:în\s+urmă|in\s+urma)|\d{4}|Crăciun|Craciun|Paște|Paste|Paști|Pasti)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianTemporalAnchorPattern();

    [GeneratedRegex(@"\b(?:în|in|la|spre|din|lângă|langa|aproape\s+de|către|catre)\s+(un\s+[\p{L}]+(?:\s+[\p{L}]+)?|o\s+[\p{L}]+(?:\s+[\p{L}]+)?|același\s+[\p{L}]+(?:\s+[\p{L}]+)?|acelasi\s+[\p{L}]+(?:\s+[\p{L}]+)?|aceeași\s+[\p{L}]+(?:\s+[\p{L}]+)?|aceeasi\s+[\p{L}]+(?:\s+[\p{L}]+)?|[A-ZĂÂÎȘȚ][\p{Ll}ăâîșț]+(?:\s+[A-ZĂÂÎȘȚ][\p{Ll}ăâîșț]+)?|[a-zăâîșț]+(?:\s+[a-zăâîșț]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianLocationPattern();

    [GeneratedRegex(@"\bpe\s+([a-zăâîșț]+(?:\s+[a-zăâîșț]+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianPeLocationPattern();

    [GeneratedRegex(@"\b(?:Acum|Astăzi|Astazi|Azi|Ieri|Mâine|Maine|Luni|Marți|Marti|Miercuri|Joi|Vineri|Sâmbătă|Sambata|Duminică|Duminica|Ianuarie|Februarie|Martie|Aprilie|Mai|Iunie|Iulie|August|Septembrie|Octombrie|Noiembrie|Decembrie|Crăciun|Craciun|Paște|Paste)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianTemporalEntityPattern();

    [GeneratedRegex(@"\b(și-a\s+amintit|si-a\s+amintit|își\s+amintea|isi\s+amintea|amintit|amintire|odinioară|odinioara|demult|în\s+trecut|in\s+trecut)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianRememberPattern();

    [GeneratedRegex(@"\b(a\s+văzut|a\s+vazut|văzut|vazut|a\s+auzit|auzit|a\s+simțit|a\s+simtit|observat|urmărit|urmarit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianPerceptionPattern();

    [GeneratedRegex(@"\b(a\s+spus|spus|a\s+întrebat|a\s+intrebat|întrebat|intrebat|a\s+răspuns|a\s+raspuns|răspuns|raspuns|a\s+scris|scris|a\s+explicat|explicat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianReportingPattern();

    [GeneratedRegex(@"\b(a\s+încercat|a\s+incercat|încercat|incercat|a\s+decis|decis|a\s+planificat|planificat|a\s+promis|promis|a\s+început|a\s+inceput|început|inceput)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianIntentionalActionPattern();

    [GeneratedRegex(@"\b(s-a\s+gândit|s-a\s+gandit|gândit|gandit|a\s+crezut|crezut|a\s+știut|a\s+stiut|știut|stiut|a\s+imaginat|imaginat|a\s+sperat|sperat|a\s+temut|temut)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianCognitiveStatePattern();

    [GeneratedRegex(@"\b(este|sunt|era|erau|fi|fost|trăiește|traieste|locuiește|locuieste|locuia|are|avea)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianStatePattern();

    [GeneratedRegex(@"\b(poate|probabil|ar\s+putea|ar\s+fi|și-ar\s+fi\s+imaginat|si-ar\s+fi\s+imaginat|dacă|daca)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianSuppositionPattern();

    [GeneratedRegex(@"\b(întotdeauna|intotdeauna|niciodată|niciodata|de\s+obicei|în\s+general|in\s+general|toată\s+lumea\s+știe|toata\s+lumea\s+stie)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianGeneralPattern();

    [GeneratedRegex(@"\b(în\s+poveste|in\s+poveste|în\s+roman|in\s+roman|în\s+film|in\s+film|fictiv|inventat)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianFictionPattern();

    [GeneratedRegex(@"\b(întâlnit|intalnit|găsit|gasit|reunit|s-au\s+întâlnit|s-au\s+intalnit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianJoinPattern();

    [GeneratedRegex(@"\b(plecat|separat|dispărut|disparut|singur|a\s+părăsit|a\s+parasit|s-a\s+despărțit|s-a\s+despartit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianSplitPattern();

    [GeneratedRegex(@"\b(între\s+timp|intre\s+timp|în\s+altă\s+parte|in\s+alta\s+parte|simultan|în\s+același\s+timp|in\s+acelasi\s+timp)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianParallelPattern();

    [GeneratedRegex(@"\b(cu\s+(?:\d+\s+\w+|ani|mult\s+timp)\s+(?:în\s+urmă|in\s+urma)|mai\s+devreme|odinioară|odinioara|demult|în\s+trecut|in\s+trecut|înainte|inainte)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianRetrospectiveCuePattern();

    [GeneratedRegex(@"\b(apoi|după\s+aceea|dupa\s+aceea|atunci|pe\s+urmă|pe\s+urma)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianSequentialCuePattern();

    [GeneratedRegex(@"\b(mai\s+târziu|mai\s+tarziu|după|dupa|în\s+cele\s+din\s+urmă|in\s+cele\s+din\s+urma)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianForwardCuePattern();

    [GeneratedRegex(@"\b(?:a|au|am|ai|ați|ati)\s+\w+(?:at|it|ut)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianPerfectiveAspectPattern();

    [GeneratedRegex(@"\b(?:este|era|sunt|erau)\s+(?:în\s+curs\s+de|in\s+curs\s+de)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianProgressiveAspectPattern();

    [GeneratedRegex(@"\b(?:nu|niciodată|niciodata|n-a|n-au|n-am)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianNegativePolarityPattern();

    [GeneratedRegex(@"\b(?:timp\s+de\s+)?\d+\s+(?:secundă|secunda|secunde|minut|minute|oră|ora|ore|zi|zile|săptămână|saptamana|săptămâni|saptamani|lună|luna|luni|an|ani)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianDurationPattern();

    [GeneratedRegex(@"\b(?:zori|amiază|amiaza|miezul\s+nopții|miezul\s+noptii|noapte|dimineață|dimineata|seară|seara|\d{1,2}:\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianClockTimePattern();

    [GeneratedRegex(@"\b(?:întotdeauna|intotdeauna|de\s+obicei|adesea|în\s+fiecare\s+\w+|in\s+fiecare\s+\w+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianSetTimePattern();

    [GeneratedRegex(@"\b[A-ZĂÂÎȘȚ][\p{Ll}ăâîșț]+(?:\s+[A-ZĂÂÎȘȚ][\p{Ll}ăâîșț]+)?\b")]
    private static partial Regex RomanianEntityPattern();

    [GeneratedRegex(@"\b([A-ZĂÂÎȘȚ][\p{Ll}ăâîșț]+)\s+(s-a\s+gândit|s-a\s+gandit|și-a\s+amintit|si-a\s+amintit|a\s+crezut|a\s+văzut|a\s+vazut|a\s+auzit)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RomanianPerspectivePattern();
}

internal static class TymXmlSerializer
{
    public static string Serialize(Diagram diagram)
    {
        var xml = new StringBuilder();
        var actorIdByName = diagram.Actors.ToDictionary(actor => actor.Name, actor => actor.Id, StringComparer.OrdinalIgnoreCase);
        var locationIdByName = diagram.Locations.ToDictionary(location => location.Name, location => location.Id, StringComparer.OrdinalIgnoreCase);
        xml.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        xml.AppendLine("""<TYM>""");
        xml.AppendLine("""  <TA-SECTION>""");
        foreach (var actor in diagram.Actors)
        {
            xml.AppendLine($"""    <TA ID="{X(actor.Id)}" NAME="{X(actor.Name)}" />""");
        }
        xml.AppendLine("""  </TA-SECTION>""");

        xml.AppendLine("""  <TL-SECTION>""");
        foreach (var location in diagram.Locations)
        {
            xml.AppendLine($"""    <TL ID="{X(location.Id)}" NAME="{X(location.Name)}" PARENTID="{X(location.ParentId)}" />""");
        }
        xml.AppendLine("""  </TL-SECTION>""");

        xml.AppendLine("""  <ENTITY-MENTION-SECTION>""");
        foreach (var mention in diagram.EntityMentions)
        {
            xml.AppendLine(
                $"""    <ENTITY-MENTION ID="{X(mention.Id)}" TYPE="{X(mention.Label)}" SOURCE="{X(mention.Source)}" EVENT="{X(mention.EventId)}" SPANS="{mention.SpanStart}~{mention.SpanEnd}" TEXT="{X(mention.Text)}" />""");
        }
        xml.AppendLine("""  </ENTITY-MENTION-SECTION>""");

        xml.AppendLine("""  <EVENT-SECTION>""");
        foreach (var ev in diagram.Events)
        {
            var actorIds = ev.Actors
                .Where(actorIdByName.ContainsKey)
                .Select(actor => actorIdByName[actor]);
            var locationId = ev.Location.Length > 0 && locationIdByName.TryGetValue(ev.Location, out var locId) ? locId : "L1";
            xml.AppendLine(
                $"""    <EVENT ID="{X(ev.Id)}" SPANS="{ev.SpanStart}~{ev.SpanEnd}" ACTORS="{X(string.Join(",", actorIds))}" LOCATION="{X(locationId)}" TEMPORAL-ANCHOR="{X(ev.TemporalAnchor)}" TENSE="{X(ev.TemporalCategory)}" ACTION="{X(ev.Action)}" REL-PREV="{X(ev.RelationToPrevious)}" REL-CUE="{X(ev.RelationCue)}" ORDER="{ev.Order}" TEXT="{X(ev.Text)}" />""");
        }
        xml.AppendLine("""  </EVENT-SECTION>""");

        xml.AppendLine("""  <TIMEML-SECTION>""");
        foreach (var ev in diagram.TimeMl.Events)
        {
            xml.AppendLine(
                $"""    <EVENT eid="{X(ev.Eid)}" class="{X(ev.Class)}" tymEventId="{X(ev.TymEventId)}" spans="{ev.SpanStart}~{ev.SpanEnd}">{X(ev.Text)}</EVENT>""");
        }

        foreach (var timex in diagram.TimeMl.Timex3)
        {
            xml.AppendLine(
                $"""    <TIMEX3 tid="{X(timex.Tid)}" type="{X(timex.Type)}" value="{X(timex.Value)}" functionInDocument="{X(timex.FunctionInDocument)}" source="{X(timex.Source)}" spans="{timex.SpanStart}~{timex.SpanEnd}">{X(timex.Text)}</TIMEX3>""");
        }

        foreach (var signal in diagram.TimeMl.Signals)
        {
            xml.AppendLine(
                $"""    <SIGNAL sid="{X(signal.Sid)}" eventId="{X(signal.EventId)}" relationHint="{X(signal.RelationHint)}" spans="{signal.SpanStart}~{signal.SpanEnd}">{X(signal.Text)}</SIGNAL>""");
        }

        foreach (var instance in diagram.TimeMl.MakeInstances)
        {
            xml.AppendLine(
                $"""    <MAKEINSTANCE eiid="{X(instance.Eiid)}" eventID="{X(instance.EventId)}" tense="{X(instance.Tense)}" aspect="{X(instance.Aspect)}" polarity="{X(instance.Polarity)}" pos="{X(instance.Pos)}" tymEventId="{X(instance.TymEventId)}" />""");
        }

        foreach (var tlink in diagram.TimeMl.TLinks)
        {
            xml.AppendLine(
                $"""    <TLINK lid="{X(tlink.Lid)}" relType="{X(tlink.RelType)}" eventInstanceID="{X(tlink.EventInstanceId ?? "")}" relatedToEventInstance="{X(tlink.RelatedToEventInstance ?? "")}" timeID="{X(tlink.TimeId ?? "")}" relatedToTime="{X(tlink.RelatedToTime ?? "")}" signalID="{X(tlink.SignalId ?? "")}" origin="{X(tlink.Origin)}" />""");
        }
        xml.AppendLine("""  </TIMEML-SECTION>""");

        xml.AppendLine("""  <TT-SECTION>""");
        foreach (var track in diagram.Tracks)
        {
            xml.AppendLine(
                $"""    <TT ID="{X(track.Id)}" NAME="{X(track.Name)}" LEFT-EP="{X(track.LeftEndpoint)}" RIGHT-EP="{X(track.RightEndpoint)}" />""");
        }
        xml.AppendLine("""  </TT-SECTION>""");

        xml.AppendLine("""  <TIME-SECTION>""");
        for (var i = 0; i < diagram.Relations.Count; i++)
        {
            var relation = diagram.Relations[i];
            var tagName = relation.FromId.StartsWith("TS", StringComparison.OrdinalIgnoreCase) &&
                relation.ToId.StartsWith("TS", StringComparison.OrdinalIgnoreCase)
                    ? "TREL"
                    : "TIME";
            xml.AppendLine(
                $"""    <{tagName} ID="TI{i + 1}" FROM="{X(relation.FromId)}" REL="{X(relation.Rel.Replace("_", "-", StringComparison.Ordinal))}" TO="{X(relation.ToId)}" CUE="{X(relation.Cue)}" EVIDENCE="{X(relation.Evidence)}" />""");
        }
        xml.AppendLine("""  </TIME-SECTION>""");

        xml.AppendLine("""  <EP-SECTION>""");
        foreach (var endpoint in diagram.Endpoints)
        {
            var tt1 = endpoint.TrackIds.Count > 0 ? endpoint.TrackIds[0] : "";
            var tt2 = endpoint.TrackIds.Count > 1 ? endpoint.TrackIds[1] : "";
            xml.AppendLine(
                $"""    <ENDPOINT ID="{X(endpoint.Id)}" TYPE="{X(endpoint.Type)}" NAME="{X(endpoint.Name)}" TT1="{X(tt1)}" TT2="{X(tt2)}" SEGMENT="{X(endpoint.SegmentId)}" />""");
        }
        xml.AppendLine("""  </EP-SECTION>""");

        xml.AppendLine("""  <POINT-SECTION>""");
        foreach (var boundary in diagram.Boundaries)
        {
            xml.AppendLine(
                $"""    <POINT ID="{X(boundary.Id)}" TYPE="{X(boundary.Type)}" FROM-TS="{X(boundary.FromSegmentId ?? "")}" TO-TS="{X(boundary.ToSegmentId ?? "")}" TRACKS="{X(string.Join(" ", boundary.TrackIds))}" REASON="{X(boundary.Reason)}" />""");
        }
        xml.AppendLine("""  </POINT-SECTION>""");

        xml.AppendLine("""  <BODY>""");
        foreach (var segment in diagram.Segments.OrderBy(segment => segment.TextOrder))
        {
            var actorIds = segment.Actors
                .Where(actorIdByName.ContainsKey)
                .Select(actor => actorIdByName[actor]);
            var locationId = segment.LocationId.Length > 0 ? segment.LocationId : "L1";
            xml.AppendLine(
                $"""    <TS ID="{X(segment.Id)}" IN-TT="{X(segment.TrackId)}" SPANS="{segment.SpanStart}~{segment.SpanEnd}" ACTORS="{X(string.Join(",", actorIds))}" LOCATION="{X(locationId)}" NAME="{X(segment.Id)}" TYPE="{X(segment.Type)}" TST="{X(segment.TemporalCategory)}" TIME="{X(segment.TemporalAnchor)}" PER="{X(segment.Perspective)}" EVENTS="{X(string.Join(",", segment.EventIds))}">{X(segment.Text)}</TS>""");
        }
        xml.AppendLine("""  </BODY>""");
        xml.AppendLine("""</TYM>""");

        return xml.ToString();
    }

    private static string X(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}

internal static class TymSvgRenderer
{
    public static RenderResult Render(Diagram diagram, DiagramOptions options)
    {
        var width = Math.Clamp(options.Width, 720, 2400);
        var layout = string.IsNullOrWhiteSpace(options.Layout) ? "both" : options.Layout;
        var highlightEntities = new HashSet<string>(
            options.HighlightEntities ?? [],
            StringComparer.OrdinalIgnoreCase);

        var parts = new StringBuilder();
        const int marginLeft = 105;
        const int marginRight = 80;
        const int legendHeight = 112;
        var usable = Math.Max(400, width - marginLeft - marginRight);

        var y = 38.0;
        var contentBottom = y;
        if (layout is "both" or "text_order")
        {
            var chainY = y + 38;

            if (diagram.Segments.Count > 0)
            {
                var step = usable / Math.Max(1.0, diagram.Segments.Count);
                var startX = marginLeft;
                var endX = marginLeft + step * diagram.Segments.Count;
                parts.Append(Line(startX, chainY, endX, chainY, stroke: "#374151", strokeWidth: "2"));

                for (var i = 0; i < diagram.Segments.Count; i++)
                {
                    var x = marginLeft + step * i;
                    parts.Append(Line(x, chainY - 12, x, chainY + 12, strokeWidth: "1.5"));
                    var labelY = i % 2 == 0 ? chainY - 22 : chainY + 34;
                    parts.Append(SvgText(x + 8, labelY, diagram.Segments[i].Id, 13));
                }

                parts.Append(Line(endX, chainY - 12, endX, chainY + 12, strokeWidth: "1.5"));
            }
            else
            {
                parts.Append(SvgText(width / 2.0, chainY, "No segments detected", 13));
            }

            y = chainY + 82;
            contentBottom = Math.Max(contentBottom, y);
        }

        if (layout is "both" or "time_yard")
        {
            if (layout is "time_yard")
            {
                y += 24;
            }

            var segmentsByTrack = diagram.Tracks.ToDictionary(
                track => track.Id,
                track => diagram.Segments
                    .Where(segment => segment.TrackId == track.Id)
                    .OrderBy(segment => segment.StoryOrder)
                    .ThenBy(segment => segment.TextOrder)
                    .ToList());

            var rowGap = 86;
            var maxTrackSpan = Math.Max(1, segmentsByTrack.Values.Select(items => items.Count).DefaultIfEmpty(1).Max());
            var step = Math.Min(145.0, usable / Math.Max(1.0, maxTrackSpan + 1));

            for (var row = 0; row < diagram.Tracks.Count; row++)
            {
                var track = diagram.Tracks[row];
                var trackY = y + row * rowGap;
                var items = segmentsByTrack.GetValueOrDefault(track.Id) ?? [];
                var trackEntities = track.Name.Replace("&", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var highlighted = trackEntities.Any(highlightEntities.Contains);
                var strokeWidth = highlighted ? "5" : "2.4";

                parts.Append(SvgText(18, trackY + 5, $"{track.Id}: {track.Name}", 12, anchor: "start", weight: "700"));
                if (items.Count == 0)
                {
                    continue;
                }

                var firstX = marginLeft;
                var lastX = marginLeft + step * Math.Max(1, items.Count);
                parts.Append(Triangle(firstX - 35, trackY, "start"));
                parts.Append(Triangle(lastX + 35, trackY, "stop"));

                for (var index = 0; index < items.Count; index++)
                {
                    var segment = items[index];
                    var x1 = marginLeft + step * index;
                    var x2 = marginLeft + step * (index + 1);
                    var dash = segment.Type is "REM" or "SUP" or "FIC" ? "2 6" : "";
                    parts.Append(Line(x1, trackY, x2, trackY, strokeWidth: strokeWidth, dashArray: dash));
                    parts.Append(Rect(x1 - 13, trackY - 12, 26, 24));
                    parts.Append(SvgText(x1, trackY + 4, segment.Id, 10, weight: "700"));
                    parts.Append(Line(x2, trackY - 13, x2, trackY + 13, strokeWidth: "1.4"));

                    var label = $"{segment.Id}: {TruncateLabel(segment.Text, 44)}";
                    var labelY = index % 2 == 0 ? trackY - 25 : trackY + 38;
                    parts.Append(SvgText((x1 + x2) / 2, labelY, label, 12));
                    if (segment.Type != "NAR")
                    {
                        parts.Append(SvgText((x1 + x2) / 2, labelY + 16, segment.Type, 10, weight: "700"));
                    }
                }
            }

            var endpointYBase = y + diagram.Tracks.Count * rowGap + 8;
            for (var index = 0; index < diagram.Endpoints.Count; index++)
            {
                var endpoint = diagram.Endpoints[index];
                var label = $"{endpoint.Id}: {endpoint.Type} at {endpoint.SegmentId}";
                parts.Append(SvgText(marginLeft + 180 * (index % 5), endpointYBase + 18 * (index / 5), label, 11, anchor: "start"));
            }

            var endpointRows = Math.Max(1, (diagram.Endpoints.Count + 4) / 5);
            contentBottom = Math.Max(contentBottom, endpointYBase + endpointRows * 18);
        }

        var legendY = contentBottom + 28;
        parts.Append(Legend(18, legendY, width - 36));

        var height = Math.Max(240, (int)(legendY + legendHeight + 24));
        var svg =
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="TYM time-yard diagram">""" +
            """<rect width="100%" height="100%" fill="#ffffff"/>""" +
            """<style>text{dominant-baseline:auto}</style>""" +
            parts +
            "</svg>";

        return new RenderResult("svg", width, height, svg);
    }

    private static string TruncateLabel(string value, int limit)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        return normalized.Length <= limit
            ? normalized
            : normalized[..Math.Max(0, limit - 1)].TrimEnd() + "...";
    }

    private static string SvgText(double x, double y, string value, int size, string anchor = "middle", string weight = "400")
    {
        return $"""
            <text x="{x:F1}" y="{y:F1}" text-anchor="{anchor}" font-family="Arial, Helvetica, sans-serif" font-size="{size}" font-weight="{weight}" fill="#111827">{WebUtility.HtmlEncode(value)}</text>
            """;
    }

    private static string Line(
        double x1,
        double y1,
        double x2,
        double y2,
        string stroke = "#111827",
        string strokeWidth = "2",
        string dashArray = "")
    {
        var dash = string.IsNullOrWhiteSpace(dashArray) ? "" : $" stroke-dasharray=\"{dashArray}\"";
        return $"""
            <line x1="{x1:F1}" y1="{y1:F1}" x2="{x2:F1}" y2="{y2:F1}" stroke="{stroke}" stroke-width="{strokeWidth}" stroke-linecap="round"{dash}/>
            """;
    }

    private static string Rect(double x, double y, double width, double height)
    {
        return $"""
            <rect x="{x:F1}" y="{y:F1}" width="{width:F1}" height="{height:F1}" fill="#dbeafe" stroke="#111827" stroke-width="1.5" rx="2"/>
            """;
    }

    private static string Triangle(double cx, double cy, string direction)
    {
        var points = direction == "start"
            ? $"{cx - 13:F1},{cy - 12:F1} {cx - 13:F1},{cy + 12:F1} {cx + 9:F1},{cy:F1}"
            : $"{cx + 13:F1},{cy - 12:F1} {cx + 13:F1},{cy + 12:F1} {cx - 9:F1},{cy:F1}";

        return $"""
            <polygon points="{points}" fill="#dbeafe" stroke="#111827" stroke-width="1.5"/>
            """;
    }

    private static string Legend(double x, double y, double width)
    {
        var parts = new StringBuilder();
        var columnWidth = Math.Max(320, (width - 34) / 2.0);
        var leftX = x + 18;
        var rightX = x + 18 + columnWidth + 16;
        var itemY = y + 48;

        parts.Append($"""
            <rect x="{x:F1}" y="{y:F1}" width="{width:F1}" height="112.0" fill="#f9fafb" stroke="#d1d5db" stroke-width="1.2" rx="6"/>
            """);
        parts.Append(SvgText(x + 18, y + 25, "Legend", 13, anchor: "start", weight: "700"));

        parts.Append(LegendLine(leftX, itemY, "Solid line: narrative continuity"));
        parts.Append(LegendLine(rightX, itemY, "Dotted line: REM/SUP/GEN/FIC segment", dashArray: "2 6"));

        itemY += 28;
        parts.Append(LegendTriangle(leftX, itemY, "Triangle: track start/stop"));
        parts.Append(LegendRect(rightX, itemY, "Blue box: TS marker"));

        itemY += 28;
        parts.Append(LegendTick(leftX, itemY, "Vertical tick: segment boundary"));
        parts.Append(LegendEndpoint(rightX, itemY, "Endpoint text: JOIN/SPLIT"));

        return parts.ToString();
    }

    private static string LegendLine(double x, double y, string label, string dashArray = "")
    {
        return Line(x, y - 4, x + 44, y - 4, strokeWidth: "2.4", dashArray: dashArray) +
            SvgText(x + 56, y, label, 11, anchor: "start");
    }

    private static string LegendTriangle(double x, double y, string label)
    {
        return Triangle(x + 10, y - 5, "start") +
            Triangle(x + 43, y - 5, "stop") +
            SvgText(x + 56, y, label, 11, anchor: "start");
    }

    private static string LegendRect(double x, double y, string label)
    {
        return Rect(x + 12, y - 17, 28, 24) +
            SvgText(x + 26, y - 1, "TS", 9, weight: "700") +
            SvgText(x + 56, y, label, 11, anchor: "start");
    }

    private static string LegendTick(double x, double y, string label)
    {
        return Line(x + 23, y - 18, x + 23, y + 7, strokeWidth: "1.5") +
            SvgText(x + 56, y, label, 11, anchor: "start");
    }

    private static string LegendEndpoint(double x, double y, string label)
    {
        return SvgText(x + 12, y, "EP", 11, anchor: "start", weight: "700") +
            SvgText(x + 56, y, label, 11, anchor: "start");
    }
}
