using System.Diagnostics;
using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace Tym.NeuralBenchmark;

/// <summary>Unlabeled reconstruction diagnostics; MSE is not semantic classification accuracy.</summary>
public sealed record AutoencoderResult(
    double ValidationMeanSquaredError,
    double LastEpochTrainingMeanSquaredError,
    double TrainingSeconds,
    double PredictionSeconds,
    long ModelParameters,
    int InputDimensions,
    int BottleneckDimensions,
    long SampledPeakWorkingSetBytes,
    float[][] EncodedValidation,
    IReadOnlyDictionary<string, object> Metadata);

public static partial class NeuralAlgorithms
{
    /// <summary>
    /// Fit an unlabeled fully connected autoencoder on finite numeric training rows, then
    /// measure reconstruction MSE on validation rows. Fit any upstream text vectorizer/PCA
    /// using training data only before invoking this method. No implicit scaling occurs here.
    /// Adam .001, batch32, fixed epochs, deterministic row shuffle, CPU one thread.
    /// </summary>
    public static AutoencoderResult FitAutoencoder(float[][] train, float[][] validation, int seed, int epochs = 5)
    {
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(validation);
        if (train.Length == 0 || validation.Length == 0 || train[0] is null || train[0].Length is < 2 or > 4096)
            throw new ArgumentException("Nonempty numeric matrices with 2–4096 feature dimensions are required.");
        var dimensions = train[0].Length;
        if (train.Concat(validation).Any(row => row is null || row.Length != dimensions || row.Any(value => !float.IsFinite(value))))
            throw new ArgumentException("All autoencoder rows must have equal width and finite values.");
        if (epochs is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(epochs));
        lock (RuntimeLock) return FitAutoencoderCore(train, validation, seed, epochs);
    }

    private static AutoencoderResult FitAutoencoderCore(float[][] train, float[][] validation, int seed, int epochs)
    {
        torch.set_num_threads(1);
        torch.random.manual_seed(seed);
        var dimensions = train[0].Length;
        var bottleneck = Math.Max(1, Math.Min(32, dimensions / 2));
        using var process = Process.GetCurrentProcess();
        var peakWorkingSet = SampleWorkingSet(process);
        var watch = Stopwatch.StartNew();
        using var model = new CompactAutoencoder(dimensions, bottleneck);
        var parameterCount = model.parameters().Sum(parameter => parameter.numel());
        using var optimizer = torch.optim.Adam(model.parameters(), lr: 0.001);
        using var criterion = torch.nn.MSELoss();
        model.train();
        var random = new Random(seed);
        var indices = Enumerable.Range(0, train.Length).ToArray();
        var epochLosses = new List<double>();
        for (var epoch = 0; epoch < epochs; epoch++)
        {
            Shuffle(indices, random);
            double lossSum = 0;
            for (var offset = 0; offset < indices.Length; offset += BatchSize)
            {
                using var scope = torch.NewDisposeScope();
                var batchIndices = indices.Skip(offset).Take(BatchSize).ToArray();
                var input = NumericInput(batchIndices.Select(index => train[index]).ToArray(), dimensions);
                optimizer.zero_grad();
                var reconstructed = model.call(input);
                var loss = criterion.call(reconstructed, input);
                var value = loss.item<float>();
                if (!float.IsFinite(value)) throw new InvalidOperationException("Autoencoder training loss is not finite.");
                lossSum += value * batchIndices.Length;
                loss.backward();
                optimizer.step();
                peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
            }
            epochLosses.Add(lossSum / train.Length);
        }
        var trainingSeconds = watch.Elapsed.TotalSeconds;
        model.eval();
        using var noGradient = torch.no_grad();
        using (var scope = torch.NewDisposeScope())
        {
            var warmup = NumericInput(train.Take(Math.Min(BatchSize, train.Length)).ToArray(), dimensions);
            _ = model.call(warmup);
        }
        watch.Restart();
        var encoded = new float[validation.Length][];
        double squaredErrorSum = 0;
        for (var offset = 0; offset < validation.Length; offset += BatchSize)
        {
            using var scope = torch.NewDisposeScope();
            var rows = validation.Skip(offset).Take(BatchSize).ToArray();
            var input = NumericInput(rows, dimensions);
            var latent = model.Encode(input);
            if (!latent.shape.SequenceEqual(new long[] { rows.Length, bottleneck }))
                throw new InvalidOperationException("Autoencoder latent vectors have an unexpected shape.");
            var reconstructed = model.Decode(latent);
            var mse = criterion.call(reconstructed, input).item<float>();
            if (!float.IsFinite(mse)) throw new InvalidOperationException("Autoencoder validation loss is not finite.");
            squaredErrorSum += mse * rows.Length;
            var flattened = latent.data<float>().ToArray();
            if (flattened.Length != rows.Length * bottleneck || flattened.Any(value => !float.IsFinite(value)))
                throw new InvalidOperationException("Autoencoder latent vectors have an unexpected element count or nonfinite values.");
            for (var row = 0; row < rows.Length; row++) encoded[offset + row] = flattened.Skip(row * bottleneck).Take(bottleneck).ToArray();
            peakWorkingSet = Math.Max(peakWorkingSet, SampleWorkingSet(process));
        }
        var predictionSeconds = watch.Elapsed.TotalSeconds;
        return new(squaredErrorSum / validation.Length, epochLosses[^1], trainingSeconds, predictionSeconds,
            parameterCount, dimensions, bottleneck, peakWorkingSet, encoded,
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["algorithm"] = "autoencoder", ["framework"] = "TorchSharp-cpu 0.107.0 / LibTorch CPU 2.10.0 / C# .NET 10",
                ["architecture"] = $"Linear({dimensions},64)->ReLU->Linear(64,{bottleneck})->Linear({bottleneck},64)->ReLU->Linear(64,{dimensions})",
                ["pretrained_weights"] = false, ["mlnet_native_trainer"] = false,
                ["seed"] = seed, ["epochs"] = epochs, ["batch_size"] = BatchSize, ["cpu_threads"] = 1,
                ["optimizer"] = "Adam", ["learning_rate"] = 0.001, ["loss"] = "mean_squared_error",
                ["upstream_transform_contract"] = "caller fits vectorizer/PCA/scaling on training rows only",
                ["scaling"] = "none inside autoencoder", ["training_rows"] = train.Length, ["validation_rows"] = validation.Length,
                ["epoch_training_losses"] = epochLosses, ["early_stopping"] = false,
                ["validation_used_for_fit"] = false, ["classification_accuracy"] = "not applicable; unlabeled reconstruction",
                ["timing"] = "fit includes model creation/optimization; prediction includes reconstruction, MSE, and latent encoding; native init and one training-input warmup excluded",
                ["memory_measure"] = "maximum process working-set sample at batch boundaries; not isolated model memory",
                ["model_exported"] = false
            });
    }

    private static Tensor NumericInput(IReadOnlyList<float[]> rows, int dimensions) =>
        torch.tensor(rows.SelectMany(row => row).ToArray(), dtype: ScalarType.Float32).reshape(rows.Count, dimensions);

    private sealed class CompactAutoencoder : torch.nn.Module<Tensor, Tensor>
    {
        private readonly Sequential encoder;
        private readonly Sequential decoder;

        public CompactAutoencoder(int dimensions, int bottleneck) : base("CompactNumericAutoencoder")
        {
            encoder = torch.nn.Sequential(torch.nn.Linear(dimensions, 64), torch.nn.ReLU(), torch.nn.Linear(64, bottleneck));
            decoder = torch.nn.Sequential(torch.nn.Linear(bottleneck, 64), torch.nn.ReLU(), torch.nn.Linear(64, dimensions));
            RegisterComponents();
        }

        public Tensor Encode(Tensor input) => encoder.call(input);
        public Tensor Decode(Tensor latent) => decoder.call(latent);
        public override Tensor forward(Tensor input) => Decode(Encode(input));
    }
}
