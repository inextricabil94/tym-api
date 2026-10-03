using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace Tym.NeuralBenchmark;

/// <summary>Predictions and measured costs without any validation labels entering fitting.</summary>
public sealed record NeuralPredictionResult(
    string[] Predictions,
    double TrainingSeconds,
    double PredictionSeconds,
    long ModelParameters,
    int TrainingVocabularySize,
    int MaximumSequenceLength,
    long SampledPeakWorkingSetBytes,
    IReadOnlyDictionary<string, object> Metadata);

/// <summary>
/// Small, genuine scratch-trained CPU neural baselines implemented entirely in C#.
/// These are TorchSharp experiments alongside ML.NET; they are not pretrained BERT/NAS-BERT
/// or replicas of the historical deep-learning model described in the research paper.
/// A caller must preserve the same source-document folds used by the lexical benchmark.
/// </summary>
public static partial class NeuralAlgorithms
{
    public const int MaximumVocabularySize = 2048;
    public const int SequenceLength = 64;
    public const int BatchSize = 32;
    public const int EmbeddingSize = 32;
    private static readonly string[] Names = ["mlp", "cnn", "rnn", "transformer"];
    private static readonly Regex Word = new(@"[\p{L}\p{M}\p{Nd}]+(?:['’\-][\p{L}\p{M}\p{Nd}]+)*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    // Torch's seed/thread controls are process-global. Serialize calls in this library.
    private static readonly object RuntimeLock = new();
    public static IReadOnlyList<string> SupportedNames { get; } = Array.AsReadOnly(Names);

    /// <summary>
    /// Fit only training texts and labels, then score unseen texts without labels.
    /// Fixed epochs are not chosen from validation performance. Timing includes vocabulary,
    /// encoding, model creation, and optimization; prediction includes validation encoding.
    /// One eval warm-up batch is excluded from the prediction timer.
    /// Native runtime failures are allowed to propagate, rather than fabricate predictions.
    /// </summary>
    public static NeuralPredictionResult FitPredict(string algorithm,
        IReadOnlyList<string> trainTexts, IReadOnlyList<string> trainLabels,
        IReadOnlyList<string> validationTexts, int seed, int epochs = 5)
    {
        if (!Names.Contains(algorithm, StringComparer.Ordinal)) throw new ArgumentException("Unsupported neural algorithm.", nameof(algorithm));
        ArgumentNullException.ThrowIfNull(trainTexts);
        ArgumentNullException.ThrowIfNull(trainLabels);
        ArgumentNullException.ThrowIfNull(validationTexts);
        if (trainTexts.Count == 0 || trainTexts.Count != trainLabels.Count || validationTexts.Count == 0)
            throw new ArgumentException("Nonempty training texts/labels of equal length and validation texts are required.");
        if (trainTexts.Any(text => text is null) || validationTexts.Any(text => text is null)
            || trainLabels.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Texts must not be null and labels must not be empty.");
        if (epochs is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(epochs), "Epochs must be in [1,100].");
        lock (RuntimeLock) return FitPredictCore(algorithm, trainTexts, trainLabels, validationTexts, seed, epochs);
    }

    private static NeuralPredictionResult FitPredictCore(string algorithm,
        IReadOnlyList<string> trainTexts, IReadOnlyList<string> trainLabels,
        IReadOnlyList<string> validationTexts, int seed, int epochs)
    {
        // Runtime initialization is separate from per-model training time, and never uses a corpus.
        torch.set_num_threads(1);
        torch.random.manual_seed(seed);
        var labels = trainLabels.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (labels.Length < 2) throw new ArgumentException("At least two training classes are required.");
        var labelIds = labels.Select((label, index) => (label, index)).ToDictionary(pair => pair.label, pair => (long)pair.index, StringComparer.Ordinal);
        using var process = Process.GetCurrentProcess();
        var peakWorkingSet = SampleWorkingSet(process);
        var watch = Stopwatch.StartNew();
        var vocabulary = FitVocabulary(trainTexts);
        var trainEncoded = trainTexts.Select(text => Encode(text, vocabulary)).ToArray();
        var targets = trainLabels.Select(label => labelIds[label]).ToArray();
        using var model = new CompactClassifier(algorithm, vocabulary.Count + 2, labels.Length);
        var parameterCount = model.parameters().Sum(parameter => parameter.numel());
        using var optimizer = torch.optim.Adam(model.parameters(), lr: 0.001);
        using var criterion = torch.nn.CrossEntropyLoss();
        model.train();
        var random = new Random(seed);
        var indices = Enumerable.Range(0, trainTexts.Count).ToArray();
        var epochLosses = new List<double>();
        for (var epoch = 0; epoch < epochs; epoch++)
        {
            Shuffle(indices, random);
            double lossSum = 0;
            for (var offset = 0; offset < indices.Length; offset += BatchSize)
            {
                using var scope = torch.NewDisposeScope();
                var batchIndices = indices.Skip(offset).Take(BatchSize).ToArray();
                var input = MakeInput(algorithm, batchIndices.Select(index => trainEncoded[index]).ToArray(), vocabulary.Count + 2);
                var target = torch.tensor(batchIndices.Select(index => targets[index]).ToArray(), dtype: ScalarType.Int64);
                optimizer.zero_grad();
                var output = model.call(input);
                var loss = criterion.call(output, target);
                if (!double.IsFinite(loss.item<float>())) throw new InvalidOperationException("Neural training loss is not finite.");
                lossSum += loss.item<float>() * batchIndices.Length;
                loss.backward();
                optimizer.step();
                peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
            }
            epochLosses.Add(lossSum / trainTexts.Count);
        }
        var trainingSeconds = watch.Elapsed.TotalSeconds;
        model.eval();
        using var noGradient = torch.no_grad();
        // Warm up using already encoded training inputs; validation remains entirely unobserved.
        using (var scope = torch.NewDisposeScope())
        {
            var warmup = MakeInput(algorithm, trainEncoded.Take(Math.Min(BatchSize, trainEncoded.Length)).ToArray(), vocabulary.Count + 2);
            _ = model.call(warmup);
        }
        watch.Restart();
        var validationEncoded = validationTexts.Select(text => Encode(text, vocabulary)).ToArray();
        var predictions = new string[validationTexts.Count];
        for (var offset = 0; offset < predictions.Length; offset += BatchSize)
        {
            using var scope = torch.NewDisposeScope();
            var rows = validationEncoded.Skip(offset).Take(BatchSize).ToArray();
            var input = MakeInput(algorithm, rows, vocabulary.Count + 2);
            var logits = model.call(input);
            if (!logits.shape.SequenceEqual(new long[] { rows.Length, labels.Length })
                || !logits.isfinite().all().item<bool>())
                throw new InvalidOperationException("Neural validation logits have an unexpected shape or nonfinite values.");
            var ids = logits.argmax(1).data<long>().ToArray();
            for (var row = 0; row < ids.Length; row++) predictions[offset + row] = labels[checked((int)ids[row])];
            peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
        }
        var predictionSeconds = watch.Elapsed.TotalSeconds;
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["algorithm"] = algorithm, ["framework"] = "TorchSharp-cpu 0.107.0 / LibTorch CPU 2.10.0 / C# .NET 10",
            ["pretrained_weights"] = false, ["mlnet_native_trainer"] = false,
            ["seed"] = seed, ["cpu_threads"] = 1, ["epochs"] = epochs, ["batch_size"] = BatchSize,
            ["optimizer"] = "Adam", ["learning_rate"] = 0.001, ["loss"] = "cross_entropy",
            ["vocabulary_fit"] = "training texts only; descending token frequency then ordinal tie-break",
            ["label_inventory_fit"] = "training labels only", ["label_inventory"] = labels,
            ["tokenization"] = "NFC, invariant lowercase, Unicode letters/marks/digits with internal apostrophe or hyphen",
            ["maximum_vocabulary_size_including_pad_and_unknown"] = MaximumVocabularySize,
            ["padding_token_id"] = 0, ["unknown_token_id"] = 1, ["maximum_sequence_length"] = SequenceLength,
            ["truncation"] = "first 64 tokens; empty texts become one unknown token",
            ["training_token_retention"] = RetentionCoverage(trainTexts),
            ["validation_token_retention"] = RetentionCoverage(validationTexts),
            ["mlp_feature_scope"] = "vocabulary uses every training token; bag-of-words counts use the same first-64-token inputs as sequence models",
            ["embedding_dimensions"] = algorithm == "mlp" ? 0 : EmbeddingSize,
            ["architecture"] = ArchitectureDescription(algorithm),
            ["epoch_training_losses"] = epochLosses,
            ["validation_labels_used"] = false, ["early_stopping"] = false,
            ["timing"] = "training includes vocabulary/encoding/model/optimizer; prediction includes unseen text encoding; native init and one training-input warmup excluded",
            ["memory_measure"] = "maximum process working-set sample observed at batch boundaries; not isolated model memory",
            ["model_exported"] = false,
            ["limitation"] = "Compact scratch architecture with fixed settings; not a pretrained language model or reproduced historical CNN."
        };
        return new(predictions, trainingSeconds, predictionSeconds, parameterCount, vocabulary.Count + 2,
            SequenceLength, peakWorkingSet, metadata);
    }

    private static Dictionary<string, long> FitVocabulary(IEnumerable<string> trainTexts) =>
        trainTexts.SelectMany(Tokenize).GroupBy(token => token, StringComparer.Ordinal)
            .Select(group => (Token: group.Key, Count: group.Count()))
            .OrderByDescending(item => item.Count).ThenBy(item => item.Token, StringComparer.Ordinal)
            .Take(MaximumVocabularySize - 2).Select((item, index) => (item.Token, Id: (long)index + 2))
            .ToDictionary(item => item.Token, item => item.Id, StringComparer.Ordinal);

    private static IEnumerable<string> Tokenize(string text) =>
        Word.Matches(text.Normalize(NormalizationForm.FormC).ToLowerInvariant()).Select(match => match.Value);

    private static long[] Encode(string text, IReadOnlyDictionary<string, long> vocabulary)
    {
        var result = new long[SequenceLength];
        var ids = Tokenize(text).Take(SequenceLength).Select(token => vocabulary.GetValueOrDefault(token, 1)).ToArray();
        if (ids.Length == 0) result[0] = 1;
        else Array.Copy(ids, result, ids.Length);
        return result;
    }

    private static Tensor MakeInput(string algorithm, IReadOnlyList<long[]> rows, int vocabularySize)
    {
        if (algorithm == "mlp")
        {
            var values = new float[rows.Count * vocabularySize];
            for (var row = 0; row < rows.Count; row++)
                foreach (var token in rows[row].Where(id => id != 0)) values[row * vocabularySize + checked((int)token)]++;
            return torch.tensor(values, dtype: ScalarType.Float32).reshape(rows.Count, vocabularySize);
        }
        return torch.tensor(rows.SelectMany(row => row).ToArray(), dtype: ScalarType.Int64).reshape(rows.Count, SequenceLength);
    }

    private static void Shuffle(int[] indices, Random random)
    {
        for (var index = indices.Length - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (indices[index], indices[other]) = (indices[other], indices[index]);
        }
    }

    private static long SampleWorkingSet(Process process) { process.Refresh(); return process.WorkingSet64; }
    private static object RetentionCoverage(IReadOnlyList<string> texts)
    {
        long total = 0, retained = 0;
        var truncatedRows = 0;
        foreach (var text in texts)
        {
            var count = Tokenize(text).Count();
            total += count;
            retained += Math.Min(count, SequenceLength);
            if (count > SequenceLength) truncatedRows++;
        }
        return new { rows = texts.Count, truncated_rows = truncatedRows, original_tokens = total,
            retained_tokens = retained, token_retention_fraction = total == 0 ? 1.0 : (double)retained / total };
    }
    private static string ArchitectureDescription(string algorithm) => algorithm switch
    {
        "mlp" => "Normalized token-count bag of words -> Linear(vocab,64) -> ReLU -> Linear(64,classes)",
        "cnn" => "Embedding(vocab,32,pad=0) -> Conv1d(32,32,kernel=3,padding=1) -> ReLU -> masked global max -> Linear(32,classes)",
        "rnn" => "Embedding(vocab,32,pad=0) -> unidirectional LSTM(32,32,1layer) -> last nonpad hidden output -> Linear(32,classes)",
        "transformer" => "Embedding(vocab,32,pad=0) + learned positions64 -> 1 TransformerEncoderLayer(32,4heads,feedforward64,dropout0) with key-padding mask -> masked mean -> Linear(32,classes)",
        _ => throw new ArgumentException("Unknown architecture.", nameof(algorithm))
    };

    private sealed class CompactClassifier : torch.nn.Module<Tensor, Tensor>
    {
        private readonly string algorithm;
        private readonly Embedding? embedding;
        private readonly Embedding? position;
        private readonly Linear? hidden;
        private readonly Conv1d? convolution;
        private readonly LSTM? recurrent;
        private readonly TransformerEncoderLayer? encoder;
        private readonly Linear output;

        public CompactClassifier(string algorithm, int vocabularySize, int classes) : base("CompactTextClassifier")
        {
            this.algorithm = algorithm;
            if (algorithm == "mlp") hidden = torch.nn.Linear(vocabularySize, 64);
            else
            {
                embedding = torch.nn.Embedding(vocabularySize, EmbeddingSize, padding_idx: 0);
                if (algorithm == "cnn") convolution = torch.nn.Conv1d(EmbeddingSize, EmbeddingSize, 3, padding: 1);
                if (algorithm == "rnn") recurrent = torch.nn.LSTM(EmbeddingSize, EmbeddingSize, numLayers: 1, batchFirst: true);
                if (algorithm == "transformer")
                {
                    position = torch.nn.Embedding(SequenceLength, EmbeddingSize);
                    encoder = torch.nn.TransformerEncoderLayer(EmbeddingSize, 4, 64, dropout: 0);
                }
            }
            output = torch.nn.Linear(algorithm == "mlp" ? 64 : EmbeddingSize, classes);
            RegisterComponents();
        }

        public override Tensor forward(Tensor input)
        {
            if (algorithm == "mlp")
            {
                var normalized = input / input.sum(1, keepdim: true).clamp(min: 1);
                return output.call(hidden!.call(normalized).relu());
            }
            var mask = input.ne(0);
            var embedded = embedding!.call(input);
            Tensor pooled;
            if (algorithm == "cnn")
            {
                var features = convolution!.call(embedded.transpose(1, 2)).relu();
                features = features.masked_fill(mask.logical_not().unsqueeze(1), double.NegativeInfinity);
                pooled = features.max(2).values;
            }
            else if (algorithm == "rnn")
            {
                var (sequence, _, _) = recurrent!.call(embedded);
                var last = (mask.sum(1) - 1).clamp(min: 0).view(-1, 1, 1).expand(-1, 1, EmbeddingSize);
                pooled = sequence.gather(1, last).squeeze(1);
            }
            else
            {
                var positions = torch.arange(SequenceLength, dtype: ScalarType.Int64);
                var source = (embedded + position!.call(positions).unsqueeze(0)).transpose(0, 1);
                var encoded = encoder!.call(source, null!, mask.logical_not()).transpose(0, 1);
                var weights = mask.to_type(ScalarType.Float32).unsqueeze(-1);
                pooled = (encoded * weights).sum(1) / weights.sum(1).clamp(min: 1);
            }
            return output.call(pooled);
        }
    }
}
