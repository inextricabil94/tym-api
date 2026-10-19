using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Tym.ExamplePreviews <private-examples-directory>");
    return 1;
}

var directory = Path.GetFullPath(args[0]);
var examplesPath = Path.Combine(directory, "examples.json");
var examples = JsonNode.Parse(File.ReadAllText(examplesPath))!.AsArray();
var outputDirectory = Path.Combine(directory, "previews");
Directory.CreateDirectory(outputDirectory);

// Exercise the existing internal analyzer; this developer tool is not an HTTP quota path.
var analyze = typeof(DiagramOptions).Assembly.GetType("TymAnalyzer", throwOnError: true)!
    .GetMethod("Analyze", BindingFlags.Public | BindingFlags.Static)!;
var cards = new StringBuilder();
var results = new JsonArray();

foreach (var node in examples)
{
    var example = node!.AsObject();
    var id = example["id"]!.GetValue<string>();
    if (!Regex.IsMatch(id, "^[a-z0-9-]+$", RegexOptions.CultureInvariant))
    {
        throw new InvalidDataException("Example ID is not a safe filename.");
    }

    var label = example["label"]!.GetValue<string>();
    var text = example["text"]!.GetValue<string>();
    var language = example["language"]!.GetValue<string>();
    var highlight = example["highlight"]?.GetValue<string>() ?? "";
    var options = new DiagramOptions(Language: language,
        HighlightEntities: string.IsNullOrEmpty(highlight) ? [] : [highlight]);
    var response = (DiagramResponse)analyze.Invoke(null, [text, options])!;
    File.WriteAllText(Path.Combine(outputDirectory, id + ".svg"), response.Render.Svg);
    example["preview_svg"] = response.Render.Svg;
    example["preview_model_version"] = response.ModelVersion;

    results.Add(new JsonObject
    {
        ["example_id"] = id,
        ["model_version"] = response.ModelVersion,
        ["events"] = response.Analysis.EventCount,
        ["segments"] = response.Analysis.SegmentCount,
        ["tracks"] = response.Analysis.TrackCount,
        ["relations"] = response.Analysis.RelationCount,
        ["svg_bytes"] = Encoding.UTF8.GetByteCount(response.Render.Svg),
        ["interpretation"] = "Unlabeled extraction/rendering observation, not accuracy"
    });

    cards.Append($"<figure><a href=\"{id}.svg\"><img src=\"{id}.svg\" alt=\"{WebUtility.HtmlEncode(label)} TYM diagram\" loading=\"lazy\"></a>");
    cards.Append($"<figcaption><strong>{WebUtility.HtmlEncode(label)}</strong><br>{response.Analysis.EventCount} events; {response.Analysis.SegmentCount} segments; {response.Analysis.RelationCount} relations</figcaption></figure>");
}

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
File.WriteAllText(examplesPath, examples.ToJsonString(jsonOptions));
File.WriteAllText(Path.Combine(outputDirectory, "results.json"), results.ToJsonString(jsonOptions));
var html = """
    <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Private TYM example diagrams</title><style>
    *{box-sizing:border-box}body{margin:24px;font:15px system-ui;color:#202124;background:#f6f8f7}
    h1{font-size:24px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,400px),1fr));gap:20px}
    figure{margin:0;border:1px solid #d8ddd8;border-radius:6px;background:white;overflow:hidden}
    img{width:100%;height:260px;object-fit:contain}figcaption{padding:12px;border-top:1px solid #d8ddd8;overflow-wrap:anywhere}
    </style><h1>Private TYM example diagrams</h1><p>Current renderer output for local, unlabeled literary excerpts. Counts describe extraction; chronology is not adjudicated gold.</p><main>
    """ + cards + "</main></html>";
File.WriteAllText(Path.Combine(outputDirectory, "index.html"), html);
Console.WriteLine(JsonSerializer.Serialize(new { previews = examples.Count, output = outputDirectory }));
return 0;
