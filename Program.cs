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

    return null;
}

public sealed record DiagramRequest(
    string Text,
    DiagramOptions? Options = null);

public sealed record DiagramOptions(
    string Layout = "both",
    int Width = 1200,
    string[]? HighlightEntities = null,
    bool IncludeDebug = false);

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
    IReadOnlyList<NarrativeEvent> Events,
    IReadOnlyList<TimeTrack> Tracks,
    IReadOnlyList<TimeSegment> Segments,
    IReadOnlyList<Endpoint> Endpoints,
    IReadOnlyList<TimeRelation> Relations,
    IReadOnlyList<SegmentBoundary> Boundaries);

public sealed record TimeActor(
    string Id,
    string Name);

public sealed record TimeLocation(
    string Id,
    string Name,
    string ParentId);

public sealed record NarrativeEvent(
    string Id,
    string Text,
    IReadOnlyList<string> Actors,
    string TemporalAnchor,
    string Location,
    string Action,
    string TemporalCategory,
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
    string ToId);

public sealed record SegmentBoundary(
    string Id,
    string Type,
    string? FromSegmentId,
    string? ToSegmentId,
    IReadOnlyList<string> TrackIds,
    string Reason);

public sealed record RenderResult(
    string Format,
    int Width,
    int Height,
    string Svg);

internal static class TymConstants
{
    public const string Version = "tym-dotnet-paper-guided-mlnet-v0.3";
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

    public static DiagramResponse Analyze(string text, DiagramOptions options)
    {
        var events = ExtractEvents(text);
        var candidates = BuildTimeSegmentCandidates(events);
        var actors = BuildActors(events);
        var locations = BuildLocations(events);
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
            var fallbackSegmentType = HeuristicClassifySegment(raw);
            var classification = SegmentTypeClassifier.Value.Predict(raw, fallbackSegmentType);
            var segmentType = classification.Label;
            var entities = candidate.Actors.Count > 0 ? candidate.Actors : ExtractEntities(raw);
            var perspective = InferPerspective(segmentType, raw, entities);
            var key = TrackKey(entities, segmentType, candidate.TemporalCategory, perspective, raw);

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
                relations.Add(new TimeRelation(previousSegment.Id, "IMMEDIATELY_BEFORE", segment.Id));
                if (track.Id != previousSegment.TrackId)
                {
                    var rel = ParallelPattern().IsMatch(raw) ? "SIMULTANEOUS" : "BEFORE";
                    relations.Add(new TimeRelation(previousSegment.TrackId, rel, track.Id));
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

            if (JoinPattern().IsMatch(raw) && previousTrack is not null && previousTrack.Id != track.Id)
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

            if (SplitPattern().IsMatch(raw))
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

        var diagram = new Diagram(
            actors,
            locations,
            events.Select(ev => ev.ToRecord()).ToList(),
            tracks.Select(track => track.ToRecord()).ToList(),
            segments.Select(segment => segment.ToRecord()).ToList(),
            endpoints,
            relations,
            boundaries);
        var render = TymSvgRenderer.Render(diagram, options);
        var xml = TymXmlSerializer.Serialize(diagram);
        var warnings = new List<string>
        {
            "Time segment types follow the paper labels: NAR, REM, SUP, GEN, and FIC.",
            "The XML output follows the paper notation: TT-SECTION, TIME-SECTION, EP-SECTION, and inline TS tags.",
            "ML.NET calculates event temporal category and TS type from bundled paper-guided seed examples; replace these with an annotated corpus for production use."
        };
        if (candidates.Count == 0)
        {
            warnings.Add("No text segments were detected.");
        }

        return new DiagramResponse(HashId(text), TymConstants.Version, diagram, render, xml, warnings);
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

    private static List<MutableNarrativeEvent> ExtractEvents(string text)
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
            var clauses = VerbPattern().Matches(sentence).Count > 1
                ? SplitEventClauses(sentence, sentenceStart)
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

                if (!VerbPattern().IsMatch(eventText))
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

                var actors = ExtractEntities(eventText)
                    .Where(entity => !LooksLikeTemporalEntity(entity))
                    .Where(entity => !LooksLikeLocationEntity(eventText, entity))
                    .ToList();
                if (actors.Count == 0 && previousActors.Length > 0)
                {
                    actors = previousActors.ToList();
                }

                var temporalAnchor = ExtractTemporalAnchor(eventText);
                if (temporalAnchor.Length == 0 && previousTemporalAnchor.Length > 0)
                {
                    temporalAnchor = previousTemporalAnchor;
                }

                var fallbackTemporalCategory = HeuristicTemporalCategory(eventText);
                var temporalClassification = EventTemporalClassifier.Value.Predict(eventText, fallbackTemporalCategory);
                var location = ExtractLocation(eventText);
                var action = ExtractAction(eventText);

                events.Add(new MutableNarrativeEvent
                {
                    Id = $"EV{events.Count + 1}",
                    Text = eventText,
                    Actors = actors,
                    TemporalAnchor = temporalAnchor,
                    Location = location,
                    Action = action,
                    TemporalCategory = temporalClassification.Label,
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

    private static List<ClauseSpan> SplitEventClauses(string sentence, int sentenceStart)
    {
        var spans = new List<ClauseSpan>();
        var searchStart = 0;
        foreach (var part in EventBoundaryPattern().Split(sentence))
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

    private static IReadOnlyList<TimeActor> BuildActors(IReadOnlyList<MutableNarrativeEvent> events)
    {
        var actors = new List<TimeActor> { new("A0", "Narrator") };
        actors.AddRange(events
            .SelectMany(ev => ev.Actors)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => new TimeActor($"A{index + 1}", name)));
        return actors;
    }

    private static IReadOnlyList<TimeLocation> BuildLocations(IReadOnlyList<MutableNarrativeEvent> events)
    {
        var locations = new List<TimeLocation> { new("L1", "unknown", "") };
        locations.AddRange(events
            .Select(ev => ev.Location)
            .Where(location => location.Length > 0 && !location.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((name, index) => new TimeLocation($"L{index + 2}", name, "")));
        return locations;
    }

    private static bool SameSet(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count &&
            left.OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .SequenceEqual(right.OrderBy(item => item, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTemporalEntity(string entity)
    {
        return TemporalEntityPattern().IsMatch(entity);
    }

    private static bool LooksLikeLocationEntity(string text, string entity)
    {
        return Regex.IsMatch(
            text,
            $@"\b(?:in|at|on|to|from|inside|outside|near)\s+(?:the\s+)?{Regex.Escape(entity)}\b",
            RegexOptions.IgnoreCase);
    }

    private static string ExtractTemporalAnchor(string text)
    {
        var match = TemporalAnchorPattern().Match(text);
        return match.Success ? match.Value.Trim() : "";
    }

    private static string ExtractLocation(string text)
    {
        var match = LocationPattern().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    private static string ExtractAction(string text)
    {
        var match = VerbPattern().Match(text);
        return match.Success ? match.Value : "";
    }

    private static string HeuristicTemporalCategory(string text)
    {
        if (FutureTensePattern().IsMatch(text))
        {
            return "Future";
        }

        if (PastTensePattern().IsMatch(text))
        {
            return "Past";
        }

        return "Present";
    }

    private static string HeuristicClassifySegment(string text)
    {
        if (RememberPattern().IsMatch(text))
        {
            return "REM";
        }

        if (SuppositionPattern().IsMatch(text))
        {
            return "SUP";
        }

        if (GeneralPattern().IsMatch(text))
        {
            return "GEN";
        }

        if (FictionPattern().IsMatch(text))
        {
            return "FIC";
        }

        return "NAR";
    }

    private static IReadOnlyList<string> ExtractEntities(string text)
    {
        var entities = new List<string>();
        foreach (Match match in EntityPattern().Matches(text))
        {
            var candidate = match.Value;
            var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.All(part => EntityStopwords.Contains(part)))
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

    private static string InferPerspective(string segmentType, string text, IReadOnlyList<string> entities)
    {
        if (segmentType == "REM" && entities.Count > 0)
        {
            return entities[0];
        }

        var match = PerspectivePattern().Match(text);
        return match.Success && !EntityStopwords.Contains(match.Groups[1].Value)
            ? match.Groups[1].Value
            : "narrator";
    }

    private static string TrackKey(
        IReadOnlyList<string> entities,
        string segmentType,
        string temporalCategory,
        string perspective,
        string text)
    {
        if (ParallelPattern().IsMatch(text))
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

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?\b")]
    private static partial Regex EntityPattern();

    [GeneratedRegex(@"\b([A-Z][a-z]+)\s+(wondered|thought|remembered|imagined|believed|saw|heard)\b")]
    private static partial Regex PerspectivePattern();
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

        xml.AppendLine("""  <EVENT-SECTION>""");
        foreach (var ev in diagram.Events)
        {
            var actorIds = ev.Actors
                .Where(actorIdByName.ContainsKey)
                .Select(actor => actorIdByName[actor]);
            var locationId = ev.Location.Length > 0 && locationIdByName.TryGetValue(ev.Location, out var locId) ? locId : "L1";
            xml.AppendLine(
                $"""    <EVENT ID="{X(ev.Id)}" SPANS="{ev.SpanStart}~{ev.SpanEnd}" ACTORS="{X(string.Join(",", actorIds))}" LOCATION="{X(locationId)}" TEMPORAL-ANCHOR="{X(ev.TemporalAnchor)}" TENSE="{X(ev.TemporalCategory)}" ACTION="{X(ev.Action)}" ORDER="{ev.Order}" TEXT="{X(ev.Text)}" />""");
        }
        xml.AppendLine("""  </EVENT-SECTION>""");

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
                $"""    <{tagName} ID="TI{i + 1}" FROM="{X(relation.FromId)}" REL="{X(relation.Rel.Replace("_", "-", StringComparison.Ordinal))}" TO="{X(relation.ToId)}" />""");
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
        var usable = Math.Max(400, width - marginLeft - marginRight);

        var y = 38.0;
        if (layout is "both" or "text_order")
        {
            var chainY = y + 38;
            parts.Append(SvgText(width / 2.0, y, "a) Text-order time segments", 16, weight: "700"));

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
        }

        if (layout is "both" or "time_yard")
        {
            parts.Append(SvgText(width / 2.0, y, "b) TYM time yard", 16, weight: "700"));
            y += 42;

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
                    var dash = segment.Type is "REM" or "SUP" or "FIC" ? "7 7" : "";
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
        }

        var height = Math.Max(240, (int)(y + Math.Max(1, diagram.Tracks.Count) * 86 + 72));
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
}
