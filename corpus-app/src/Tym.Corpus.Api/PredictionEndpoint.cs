namespace Tym.Corpus.Api;

public static class PredictionEndpoint
{
    public static IResult Handle(CorpusPredictionRequest? request, CorpusModelService models)
    {
        var validation = PredictionValidation.Validate(request);
        if (validation is not null)
        {
            return Results.BadRequest(validation);
        }

        try
        {
            return Results.Ok(models.Predict(request!.ModelId!, request.Text!));
        }
        catch (ArgumentException error)
        {
            return Results.BadRequest(new ApiError(error.Message, "invalid_task_input"));
        }
        catch (ModelUnavailableException)
        {
            // Paths and model internals are deliberately excluded from public errors.
            return Results.Json(new ApiError(
                "The selected corpus model is unavailable on this server.", "model_unavailable"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
