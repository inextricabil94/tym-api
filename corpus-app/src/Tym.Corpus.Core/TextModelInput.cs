namespace Tym.Corpus.Core;

/// <summary>One schema shared by training and inference, preserving source/target feature direction.</summary>
public sealed class TextModelInput
{
    public string Text { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SourceContext { get; set; } = string.Empty;
    public string TargetContext { get; set; } = string.Empty;
    public string SourceMention { get; set; } = string.Empty;
    public string TargetMention { get; set; } = string.Empty;
    public string Signal { get; set; } = string.Empty;
}

/// <summary>Reads explicit task fields. It never infers a temporal direction or uses a label as a feature.</summary>
public static class TaskInputParser
{
    public const string StructuredFormat = "structured_target_v1";

    public static TextModelInput Parse(string task, string text, string label = "")
    {
        var input = new TextModelInput { Text = text, Label = label };
        var lines = text.Split('\n');
        string Value(string prefix)
        {
            var matches = lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1) throw new ArgumentException("Repeated task input field: " + prefix);
            return matches.FirstOrDefault()?[prefix.Length..].Trim() ?? string.Empty;
        }
        if (task is "timebank_tlink" or "timebank_slink" or "timebank_alink")
        {
            input.SourceContext = Value("From context:");
            input.TargetContext = Value("To context:");
            input.SourceMention = Value("From:");
            input.TargetMention = Value("To:");
            input.Signal = Value("Signal:");
            if (input.SourceContext.Length == 0 || input.TargetContext.Length == 0
                || input.SourceMention.Length == 0 || input.TargetMention.Length == 0)
                throw new ArgumentException("Provide From, From context, Signal, To and To context fields for this relation task.");
        }
        else if (task is "timebank_event_class" or "timebank_event_tense" or "timebank_timex_type")
        {
            input.TargetContext = Value("Context:");
            input.TargetMention = task == "timebank_timex_type" ? Value("Time expression:") : Value("Event:");
            if (input.TargetContext.Length == 0 || input.TargetMention.Length == 0)
                throw new ArgumentException("Provide Context and the Event or Time expression field for this target task.");
        }
        else throw new ArgumentException("This task does not support structured target input.");
        return input;
    }

    public static string Channel(TextModelInput input, string name) => name switch
    {
        nameof(TextModelInput.SourceContext) => input.SourceContext,
        nameof(TextModelInput.TargetContext) => input.TargetContext,
        nameof(TextModelInput.SourceMention) => input.SourceMention,
        nameof(TextModelInput.TargetMention) => input.TargetMention,
        nameof(TextModelInput.Signal) => input.Signal,
        _ => throw new ArgumentException("Unknown feature channel.", nameof(name))
    };

    public static IReadOnlyList<string> Channels(string task) => task is "timebank_tlink" or "timebank_slink" or "timebank_alink"
        ? [nameof(TextModelInput.SourceContext), nameof(TextModelInput.TargetContext), nameof(TextModelInput.SourceMention), nameof(TextModelInput.TargetMention), nameof(TextModelInput.Signal)]
        : [nameof(TextModelInput.TargetContext), nameof(TextModelInput.TargetMention)];
}
