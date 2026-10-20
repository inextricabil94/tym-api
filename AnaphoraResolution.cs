using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record NamedMention(string Name, string Label, int Start, int End, int Sentence,
    string Source = "proper_name_rule", string? ModelLabel = null, IReadOnlyList<string>? PartsOfSpeech = null);
internal readonly record struct HighlightSpan(int Start, int End);

internal static partial class AnaphoraResolution
{
    private sealed record Profile(string Gender = "", bool Plural = false, bool Reflexive = false,
        bool Object = false, bool Place = false, bool Relative = false, bool Unsupported = false);
    private sealed record Antecedent(string Name, string Label, int Sentence, int Start, string Gender = "");
    private static readonly Dictionary<string, Profile> English = new(StringComparer.OrdinalIgnoreCase)
    {
        ["he"] = new("m"), ["him"] = new("m", Object: true), ["his"] = new("m"), ["himself"] = new("m", Reflexive: true),
        ["she"] = new("f"), ["her"] = new("f"), ["hers"] = new("f"), ["herself"] = new("f", Reflexive: true),
        ["they"] = new(Plural: true), ["them"] = new(Plural: true, Object: true), ["their"] = new(Plural: true),
        ["theirs"] = new(Plural: true), ["themselves"] = new(Plural: true, Reflexive: true),
        ["here"] = new(Place: true), ["there"] = new(Place: true), ["who"] = new(Relative: true),
        ["whom"] = new(Relative: true), ["whose"] = new(Relative: true),
        ["i"] = new(Unsupported: true), ["me"] = new(Unsupported: true), ["my"] = new(Unsupported: true),
        ["you"] = new(Unsupported: true), ["your"] = new(Unsupported: true), ["we"] = new(Unsupported: true),
        ["us"] = new(Unsupported: true), ["our"] = new(Unsupported: true), ["it"] = new(Unsupported: true),
        ["its"] = new(Unsupported: true), ["itself"] = new(Unsupported: true), ["this"] = new(Unsupported: true),
        ["that"] = new(Unsupported: true), ["these"] = new(Unsupported: true), ["those"] = new(Unsupported: true)
    };
    private static readonly Dictionary<string, Profile> Romanian = new(StringComparer.OrdinalIgnoreCase)
    {
        ["el"] = new("m"), ["il"] = new("m", Object: true), ["l"] = new("m", Object: true), ["lui"] = new("m"),
        ["ea"] = new("f"), ["o"] = new("f", Object: true), ["ei"] = new(Plural: true), ["ele"] = new("f", Plural: true),
        ["lor"] = new(Plural: true), ["le"] = new(Plural: true, Object: true), ["ii"] = new(Object: true),
        ["i"] = new(Object: true), ["isi"] = new(Reflexive: true), ["si"] = new(Reflexive: true),
        ["sine"] = new(Reflexive: true), ["insusi"] = new("m", Reflexive: true), ["insasi"] = new("f", Reflexive: true),
        ["acolo"] = new(Place: true), ["aici"] = new(Place: true), ["care"] = new(Relative: true),
        ["caruia"] = new("m", Relative: true), ["careia"] = new("f", Relative: true),
        ["eu"] = new(Unsupported: true), ["mie"] = new(Unsupported: true), ["mi"] = new(Unsupported: true),
        ["tu"] = new(Unsupported: true), ["tie"] = new(Unsupported: true), ["noi"] = new(Unsupported: true),
        ["voi"] = new(Unsupported: true), ["acesta"] = new(Unsupported: true), ["aceasta"] = new(Unsupported: true),
        ["acestia"] = new(Unsupported: true), ["acestea"] = new(Unsupported: true)
    };

    internal static bool IsReference(string word, string language) =>
        (language == "ro" ? Romanian : English).ContainsKey(Fold(word));

    public static EntityRecognitionResponse Resolve(string text, string language,
        IReadOnlyList<NamedMention> names, IReadOnlyList<RecognizedEntity> explicitEntities, PosTaggedText pos)
    {
        var profiles = language == "ro" ? Romanian : English;
        var namesBySentence = names.ToLookup(name => name.Sentence);
        var tokens = new List<(string Text, int Start, int End, int Sentence, NamedMention? Name)>();
        tokens.AddRange(names.Select(name => (name.Name, name.Start, name.End, name.Sentence, (NamedMention?)name)));
        var sentenceNumber = 0;
        foreach (Match sentence in Sentences().Matches(text))
        {
            var localNames = namesBySentence[sentenceNumber].ToList();
            foreach (Match word in ReferenceTokens().Matches(sentence.Value))
            {
                var normalized = Fold(word.Value);
                if (!profiles.ContainsKey(normalized)) continue;
                var start = sentence.Index + word.Index;
                var profile = profiles[normalized];
                var annotation = pos.Overlapping(start, start + word.Length).FirstOrDefault(token => token.Tag != "EX"
                    && (token.Pos == "PRON" || (token.Pos == "DET" && token.Possessive) || (token.Pos == "ADV" && profile.Place)));
                if (annotation is null) continue;
                // Romanian single-letter forms are clitics only beside a hyphen, not articles/conjunctions.
                if (language == "ro" && normalized is "o" or "l" or "i" or "si"
                    && !(start > 0 && text[start - 1] == '-') && !(start + word.Length < text.Length && text[start + word.Length] == '-')) continue;
                if (localNames.Any(name => name.Start <= start && name.End >= start + word.Length)) continue;
                tokens.Add((word.Value, start, start + word.Length, sentenceNumber, null));
            }
            sentenceNumber++;
        }
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        var state = new Dictionary<string, Antecedent>(StringComparer.OrdinalIgnoreCase);
        var references = new List<EntityReference>();
        var quotes = QuotedText().Matches(text).Cast<Match>().ToArray();
        var quoteIndex = 0;
        var previousSentence = -1;
        var pluralContext = new List<string>();
        var pluralSentence = -10;
        foreach (var token in tokens)
        {
            if (previousSentence != token.Sentence)
            {
                foreach (var key in state.Where(pair => token.Sentence - pair.Value.Sentence > 2).Select(pair => pair.Key).ToArray()) state.Remove(key);
                previousSentence = token.Sentence;
            }
            if (token.Name is { } named)
            {
                var key = named.Label + ":" + named.Name;
                var gender = state.GetValueOrDefault(key)?.Gender ?? "";
                state[key] = new Antecedent(named.Name, named.Label, named.Sentence, named.Start, gender);
                references.Add(new EntityReference(named.Name, named.Label, named.Start, named.End, [named.Name], "explicit_name",
                    string.Join("+", named.PartsOfSpeech ?? ["PROPN"])));
                continue;
            }
            var profile = profiles[Fold(token.Text)];
            while (quoteIndex < quotes.Length && quotes[quoteIndex].Index + quotes[quoteIndex].Length <= token.Start) quoteIndex++;
            var quoted = quoteIndex < quotes.Length && quotes[quoteIndex].Index < token.Start && token.Start < quotes[quoteIndex].Index + quotes[quoteIndex].Length;
            var eligible = state.Values.Where(person => person.Label == (profile.Place ? "LOCATION" : "PERSON"))
                .Where(person => profile.Gender.Length == 0 || person.Gender.Length == 0 || person.Gender == profile.Gender).ToList();
            var priorNames = namesBySentence[token.Sentence].Where(name => name.End <= token.Start && name.Label == "PERSON").ToList();
            if (profile.Reflexive) eligible = eligible.Where(person => priorNames.Count > 0
                ? priorNames.Any(name => name.Name.Equals(person.Name, StringComparison.OrdinalIgnoreCase))
                : person.Sentence == token.Sentence || (language == "ro" && eligible.Count == 1)).ToList();
            else if (profile.Object && priorNames.Count == 1)
                eligible.RemoveAll(person => person.Name.Equals(priorNames[0].Name, StringComparison.OrdinalIgnoreCase));
            if (profile.Relative)
                eligible = eligible.Where(person => priorNames.Any(name => name.Name == person.Name && name.End <= token.Start
                    && token.Start - name.End < 8 && Regex.IsMatch(text[name.End..token.Start], @"^[\s,]*$"))).ToList();
            var antecedents = new List<Antecedent>();
            var reason = quoted ? "quoted_context" : profile.Unsupported ? "unsupported_reference" : "ambiguous_or_missing_antecedent";
            if (!quoted && !profile.Unsupported)
            {
                if (profile.Plural)
                {
                    var latest = eligible.Count > 0 ? eligible.Max(person => person.Sentence) : -1;
                    var coordinated = namesBySentence[latest].Where(name => name.Label == "PERSON" && name.End <= token.Start).ToList();
                    var joined = coordinated.Count >= 2 && coordinated.Zip(coordinated.Skip(1)).All(pair =>
                        Regex.IsMatch(text[pair.First.End..pair.Second.Start], @"^\s*(?:,\s*)?(?:and|\u0219i|\u015fi|si)\s*$", RegexOptions.IgnoreCase));
                    if (joined) antecedents = eligible.Where(person => coordinated.Any(name => name.Name == person.Name)).ToList();
                    else if (token.Sentence - pluralSentence <= 2 && eligible.Count == pluralContext.Count
                        && eligible.All(person => pluralContext.Contains(person.Name, StringComparer.OrdinalIgnoreCase))) antecedents = eligible;
                    else if (language == "en" && eligible.Count == 1) antecedents = eligible;
                    if (antecedents.Count > 0) reason = antecedents.Count > 1 ? "coordinated_antecedents" : "single_recent_antecedent";
                }
                else if (eligible.Count == 1)
                {
                    antecedents = eligible;
                    reason = profile.Reflexive ? "local_reflexive" : profile.Relative ? "adjacent_relative" : "single_recent_antecedent";
                }
            }
            references.Add(new EntityReference(token.Text, profile.Place ? "DEICTIC" : "PRONOUN", token.Start, token.End,
                antecedents.Select(person => person.Name).ToList(), reason,
                pos.Overlapping(token.Start, token.End).First(annotation => annotation.Pos is "PRON" or "DET" or "ADV").Pos));
            if (antecedents.Count > 1) { pluralContext = antecedents.Select(person => person.Name).ToList(); pluralSentence = token.Sentence; }
            foreach (var antecedent in antecedents)
                state[antecedent.Label + ":" + antecedent.Name] = antecedent with
                { Sentence = token.Sentence, Start = token.Start, Gender = profile.Plural || profile.Gender.Length == 0 ? antecedent.Gender : profile.Gender };
        }
        var resolvedLookup = references.Where(reference => reference.Resolution != "explicit_name")
            .SelectMany(reference => reference.Antecedents.Select(name => (reference, key: (reference.Label == "DEICTIC" ? "LOCATION:" : "PERSON:") + name)))
            .ToLookup(pair => pair.key, pair => pair.reference, StringComparer.OrdinalIgnoreCase);
        var entities = explicitEntities.Select(entity =>
        {
            var resolved = resolvedLookup[entity.Label + ":" + entity.Name].ToList();
            return entity with { MentionCount = entity.MentionCount + resolved.Count, ResolvedMentionCount = resolved.Count,
                ReferenceForms = resolved.Select(reference => reference.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
        }).ToList();
        entities.AddRange(references.Where(reference => reference.Antecedents.Count == 0 && reference.PartOfSpeech == "PRON"
                && !pos.Overlapping(reference.SpanStart, reference.SpanEnd).Any(token => token.Possessive))
            .GroupBy(reference => reference.Text, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RecognizedEntity(group.First().Text, "REFERENCE", group.Count(), group.First().SpanStart, group.First().SpanEnd,
                PartsOfSpeech: ["PRON"], RecognitionSource: "unresolved_reference")));
        var excluded = pos.Tokens.Where(token => token.Pos is not ("PROPN" or "NOUN" or "PRON") || token.Tag == "EX")
            .GroupBy(token => token.Tag == "EX" ? "EXPLETIVE" : token.Pos)
            .ToDictionary(group => group.Key, group => group.Count());
        return new EntityRecognitionResponse(language, "spacy-trained-ner+pos+rule-anaphora-en-ro-v4", entities.OrderBy(entity => entity.Label)
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase).ToList(), references,
            new PosFilterInfo(pos.Provider, pos.Model, ["PROPN", "NOUN", "PRON"], excluded),
            new NerInfo(pos.Provider, pos.Model, pos.PipelineComponents,
                pos.Entities.GroupBy(span => span.Label).ToDictionary(group => group.Key, group => group.Count())));
    }

    private static string Fold(string text) => string.Concat(text.Normalize(NormalizationForm.FormD)
        .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();
    [GeneratedRegex(@"[^.!?]+[.!?]?")]
    private static partial Regex Sentences();
    [GeneratedRegex(@"(?<![\p{L}\p{N}_])[\p{L}]+(?![\p{L}\p{N}_])")]
    private static partial Regex ReferenceTokens();
    [GeneratedRegex("\"[^\"]*\"|\u201e[^\u201d]*\u201d|\u201c[^\u201d]*\u201d")]
    private static partial Regex QuotedText();
}
