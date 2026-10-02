using Tym.Corpus.Core;

namespace Tym.Benchmark;

/// <summary>Delegates to the parser also used by deployed inference; no task labels enter feature channels.</summary>
public static class StructuredFeatures
{
    public static string Channel(TextModelInput input, string name) => TaskInputParser.Channel(input, name);
    public static TextModelInput Input(string task, string text, string label) => TaskInputParser.Parse(task, text, label);
}
