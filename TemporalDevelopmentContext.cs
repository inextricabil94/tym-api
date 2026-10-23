using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

internal sealed class TemporalDevelopmentContext
{
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly Regex Prefix = new(@"^[^\p{L}\d]+", Options);
    private static readonly Regex Backward = new(@"^(?:years? earlier|months? earlier|days? earlier|earlier|long ago|back then|in the past|cu (?:\d+|un|doi|doua|trei|multi)?\s*(?:ani|an|luni|luna|zile|zi|ore|ora) in urma|odinioara|demult|in trecut|mai devreme)\b", Options);
    private static readonly Regex Forward = new(@"^(?:(?:(?:\d+|one|two|three|several|many|un|o|doi|doua|trei|cateva)\s+)?(?:years?|months?|weeks?|days?|hours?|minutes?) later|later|the (?:next|following) (?:day|morning|evening|week|month|year)|mai tarziu|a doua zi|in (?:ziua|dimineata|seara|saptamana|luna) urmatoare|dupa (?:\d+|un|o|doi|doua|trei|cateva) (?:ani|an|luni|luna|zile|zi|ore|ora))\b", Options);
    private static readonly Regex Return = new(@"^(?:back (?:in|to) (?:the )?present|in the present|back in the story|returning to the present|in reality|in fact|inapoi in prezent|revenind in prezent|in prezent|in realitate|de fapt)\b", Options);
    private static readonly Regex Parallel = new(@"^(?:meanwhile|at the same time|elsewhere|in the meantime|intre timp|in acelasi timp|in alta parte|simultan)\b", Options);
    private static readonly Regex SameTime = new(@"^(?:on the same day|that same (?:day|morning|evening)|during the same|in aceeasi zi|in aceeasi dimineata|in aceeasi seara|tot atunci)\b", Options);
    private static readonly Regex Hypothetical = new(@"^(?:in (?:his|her|their) (?:dream|imagination)|(?:he|she|they|[\p{L}]+) (?:imagined|dreamed|hoped)|in vis|in imaginatia|(?:el|ea|[\p{L}]+) (?:isi imagina|visa|spera))\b", Options);
    private static readonly Regex General = new(@"^(?:generally|in general|as a rule|in general|de obicei|in mod obisnuit)\b", Options);
    private static readonly Regex Perspective = new(@"^(?:according to|from the perspective of|din perspectiva lui|din perspectiva|dupa parerea lui)\s+(?<owner>[\p{L}]+)\b", Options);
    private static readonly Regex Interaction = new(@"\b(?:parted ways|went their separate ways|started (?:a |the )?(?:phone call|telephone conversation)|ended (?:the |their )?(?:call|conversation)|au inceput (?:o |convorbirea |conversatia )?(?:convorbire|conversatie) telefonica|au incheiat (?:convorbirea|conversatia)|s-au despartit)\b", Options);
    private static readonly Regex Future = new(@"\b(?:will|shall|va|vor|voi|vei|vom|veti)\s+[\p{L}]+\b", Options);
    private static readonly Regex PastPerfect = new(@"\bhad\s+(?:been|gone|seen|left|met|found|[\p{L}]+ed)\b|\b(?:fusese|fusesera|plecase|mersese|ajunsese|vazuse)\b", Options);
    private static readonly Regex IsoDate = new(@"\b\d{4}-\d{2}-\d{2}\b", Options);
    private static readonly Regex Year = new(@"\b[12]\d{3}\b", Options);
    private static readonly Regex Clock = new(@"\b(?<hour>[01]?\d|2[0-3]):(?<minute>[0-5]\d)\b", Options);
    private static readonly Regex Deictic = new(@"\b(?<day>now|today|yesterday|tomorrow|acum|astazi|azi|ieri|maine)\b", Options);
    private static readonly Regex Weekday = new(@"^(?:(?:on|in ziua de)\s+)?(?<day>monday|tuesday|wednesday|thursday|friday|saturday|sunday|luni|marti|miercuri|joi|vineri|sambata|duminica)\b", Options);
    private readonly Dictionary<string, TimeAnchor> anchors = new(StringComparer.Ordinal);
    private string temporalFrame = "narrative", mode = "actual", perspective = "";
    private bool hasEvent, futureTense, pastPerfect;
    public string Frame => mode == "actual" ? temporalFrame : mode + "/" + temporalFrame;
    public string TimeReference { get; private set; } = "";
    public IReadOnlyList<string> DevelopmentEvidence { get; private set; } = [];

    public float[] Observe(MutableNarrativeEvent current)
    {
        var text = Prefix.Replace(Fold(current.Text), "");
        var reference = FindReference(text);
        var previous = reference is not null && anchors.TryGetValue(reference.Kind, out var known) ? known : null;
        var changed = reference is not null && previous is not null && !previous.Compatible(reference);
        var same = SameTime.IsMatch(text) || reference is not null && previous is not null && previous.Compatible(reference);
        var backwardsDate = changed && reference!.Start.HasValue && previous!.Start.HasValue && reference.End < previous.Start;
        var retrospective = Backward.IsMatch(text) || backwardsDate;
        var returning = (Return.IsMatch(text) || reference?.Value == "present")
            && (temporalFrame != "narrative" || mode != "actual");
        var parallel = Parallel.IsMatch(text);
        var forward = Forward.IsMatch(text);
        var nextMode = Hypothetical.IsMatch(text) ? "hypothetical" : General.IsMatch(text) ? "general" : mode;
        if (returning) nextMode = "actual";
        var owner = Perspective.Match(text);
        var perspectiveChange = owner.Success && owner.Groups["owner"].Value != perspective;
        var interactionChange = Interaction.IsMatch(text);
        var frameChange = nextMode != mode || perspectiveChange || interactionChange;
        DevelopmentEvidence = new[] { nextMode != mode ? "world_mode_change" : null,
            perspectiveChange ? "perspective_change" : null, interactionChange ? "interaction_change" : null }
            .OfType<string>().ToArray();
        var isFuture = Future.IsMatch(text);
        var isPastPerfect = PastPerfect.IsMatch(text);
        var tenseShift = isFuture && !futureTense && temporalFrame != "prospective"
            || isPastPerfect && !pastPerfect && temporalFrame != "flashback";
        var features = new float[] { changed ? 1 : 0, retrospective ? 1 : 0, returning ? 1 : 0,
            forward ? 1 : 0, parallel ? 1 : 0, frameChange ? 1 : 0, tenseShift ? 1 : 0, same ? 1 : 0 };

        // Keep context across sentences, paragraphs and unanchored events; do not inherit a flashback's clock on return.
        if (retrospective || returning || parallel || forward || nextMode != mode)
        {
            anchors.Clear(); TimeReference = "";
        }
        if (retrospective) temporalFrame = "flashback";
        else if (returning) temporalFrame = "narrative";
        else if (parallel) temporalFrame = "parallel";
        else if (isFuture && !same) temporalFrame = "prospective";
        else if (isPastPerfect && !same) temporalFrame = "flashback";
        mode = nextMode;
        if (owner.Success) perspective = owner.Groups["owner"].Value;
        if (reference is not null)
        {
            // A later mention of the year must not erase the more precise date already established.
            var retained = same && previous?.Start is not null && reference.Start <= previous.Start && reference.End >= previous.End
                ? previous : reference;
            anchors[retained.Kind] = retained; TimeReference = retained.Kind + ":" + retained.Value;
        }
        else if (retrospective) TimeReference = "relative:earlier";
        else if (forward) TimeReference = "relative:later";
        futureTense = isFuture; pastPerfect = isPastPerfect;
        if (!hasEvent) { Array.Clear(features); hasEvent = true; }
        return features;
    }

    private static string Fold(string value)
    {
        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return string.Concat(normalized.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
            .Normalize(NormalizationForm.FormC);
    }

    public static bool IsTemporalHeading(string value)
    {
        var text = Prefix.Replace(Fold(value), "").TrimEnd(' ', '.', ':', ';', '\r', '\n');
        return Backward.IsMatch(text) || Forward.IsMatch(text) || Return.IsMatch(text) || Parallel.IsMatch(text)
            || Deictic.Match(text) is { Success: true } day && day.Value.Length == text.Length
            || Regex.IsMatch(text, @"^(?:(?:in|on|anul|in anul)\s+)?(?:[12]\d{3}|\d{4}-\d{2}-\d{2})$", Options);
    }

    private static TimeAnchor? FindReference(string text)
    {
        var date = IsoDate.Match(text);
        if (date.Success)
        {
            if (!DateOnly.TryParseExact(date.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return null;
            return new("calendar", parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), parsed.DayNumber, parsed.DayNumber);
        }
        var year = Year.Match(text);
        if (year.Success)
        {
            var number = int.Parse(year.Value, CultureInfo.InvariantCulture);
            return new("calendar", year.Value, new DateOnly(number, 1, 1).DayNumber, new DateOnly(number, 12, 31).DayNumber);
        }
        var day = Deictic.Match(text);
        if (day.Success) return new("story-day", day.Groups["day"].Value switch
        { "yesterday" or "ieri" => "previous", "tomorrow" or "maine" => "next", _ => "present" });
        var weekday = Weekday.Match(text);
        if (weekday.Success) return new("weekday", weekday.Groups["day"].Value switch
        {
            "monday" or "luni" => "1", "tuesday" or "marti" => "2", "wednesday" or "miercuri" => "3",
            "thursday" or "joi" => "4", "friday" or "vineri" => "5", "saturday" or "sambata" => "6", _ => "7"
        });
        var clock = Clock.Match(text);
        return clock.Success ? new("clock", $"{int.Parse(clock.Groups["hour"].Value, CultureInfo.InvariantCulture):D2}:{clock.Groups["minute"].Value}") : null;
    }

    private sealed record TimeAnchor(string Kind, string Value, int? Start = null, int? End = null)
    {
        public bool Compatible(TimeAnchor other) => Start.HasValue && other.Start.HasValue
            ? Start <= other.End && other.Start <= End : Value == other.Value;
    }
}
