using System.Text.Json;
using Microsoft.ML;
using Tym.Corpus.Api;

namespace Tym.Corpus.Api.Tests;

/// <summary>
/// Creates a real model from original demo sentences. These component tests
/// do not evaluate corpus accuracy or support any generalization claim.
/// </summary>
public sealed class SyntheticModelFixture : IDisposable
{
    private readonly GuardedTemporaryDirectory _directory = new();
    public const string ModelId = "segment_type_en";
    public string DirectoryPath => _directory.Path;
    public string ModelPath => System.IO.Path.Combine(DirectoryPath, ModelId + "_sdca.zip");
    public string ManifestPath => ModelPath + ".manifest.json";

    public SyntheticModelFixture()
    {
        try
        {
            var activities = new[]
            {
                "walk in the garden", "talk at the station", "read a letter",
                "work in the office", "wait near the door", "visit a museum"
            };
            var rows = activities.SelectMany(activity => new[]
            {
                new CorpusModelService.TextInput { Text = $"Yesterday we did {activity}.", Label = "PAST" },
                new CorpusModelService.TextInput { Text = $"Now we {activity}.", Label = "PRESENT" },
                new CorpusModelService.TextInput { Text = $"Tomorrow we will {activity}.", Label = "FUTURE" }
            }).ToArray();
            var context = new MLContext(seed: 42);
            var data = context.Data.LoadFromEnumerable(rows);
            var pipeline = context.Transforms.Conversion.MapValueToKey("LabelKey", "Label")
                .Append(context.Transforms.Text.FeaturizeText("Features", "Text"))
                .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "LabelKey", featureColumnName: "Features",
                    maximumNumberOfIterations: 80))
                .Append(context.Transforms.Conversion.MapKeyToValue("PredictedLabelText", "PredictedLabel"));
            context.Model.Save(pipeline.Fit(data), data.Schema, ModelPath);
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(new
            {
                schema_version = 1,
                task = "segment_type",
                language = "en",
                training_rows = rows.Length,
                labels = new[] { "PAST", "PRESENT", "FUTURE" },
                source_types = new[] { "synthetic_demo" },
                source_groups = new[] { "original-component-test-fixture" },
                document_count = 1,
                trainer = "SdcaMaximumEntropy",
                featurizer = "ML.NET FeaturizeText",
                research_limit = "Original synthetic demo data for component tests; no corpus accuracy claim."
            }));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose() => _directory.Dispose();
}

/// <summary>Owns one generated directory directly beneath the resolved temporary root.</summary>
public sealed class GuardedTemporaryDirectory : IDisposable
{
    private const string Prefix = "tym-corpus-tests-";
    private readonly string _temporaryRoot = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath())
        .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    private readonly string _directoryName = Prefix + Guid.NewGuid().ToString("N");

    public string Path { get; }

    public GuardedTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(_temporaryRoot, _directoryName);
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        var target = System.IO.Path.GetFullPath(Path);
        var parent = System.IO.Path.GetDirectoryName(target);
        var name = System.IO.Path.GetFileName(target);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(parent, _temporaryRoot, comparison)
            || !string.Equals(name, _directoryName, StringComparison.Ordinal)
            || !name.StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected temporary directory.");
        }
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
    }
}
