using Microsoft.ML;

/// <summary>
/// Loads a trained TYM text classifier and predicts one label for each supplied text.
/// Predictions are model outputs, not adjudicated annotations or evaluation results.
/// </summary>
public sealed class TextModelPredictor : IDisposable
{
    private readonly PredictionEngine<ModelInput, ScoredOutput> _engine;
    private bool _disposed;

    public TextModelPredictor(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("The ML.NET model file was not found.", modelPath);
        }

        var mlContext = new MLContext(seed: 42);
        var model = mlContext.Model.Load(modelPath, out _);
        _engine = mlContext.Model.CreatePredictionEngine<ModelInput, ScoredOutput>(model);
    }

    public TextModelPrediction Predict(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var output = _engine.Predict(new ModelInput
        {
            Text = text,
            // The training pipeline retains the Label column. An empty value keeps
            // the prediction input schema compatible without supplying a target.
            Label = string.Empty
        });

        if (string.IsNullOrWhiteSpace(output.PredictedLabel))
        {
            throw new InvalidDataException("The model returned an empty predicted label.");
        }

        return new TextModelPrediction(output.PredictedLabel);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _engine.Dispose();
            _disposed = true;
        }
    }
}

public sealed record TextModelPrediction(string PredictedLabel);
