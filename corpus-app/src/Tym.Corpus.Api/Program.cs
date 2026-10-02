using System.Text.Json;
using Tym.Corpus.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new CorpusModelService(builder.Configuration["TYM_CORPUS_MODEL_DIR"]));
builder.Services.AddSingleton<DocumentAnalyzer>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});
var origins = (builder.Configuration["TYM_CORS_ORIGINS"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (origins.Length == 0)
    {
        policy.AllowAnyOrigin();
    }
    else
    {
        policy.WithOrigins(origins);
    }
    policy.AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();
app.UseCors();
app.MapGet("/", () => Results.Json(new
{
    service = "tym-corpus-api",
    endpoints = new[] { "GET /health", "GET /ready", "GET /v1/models", "POST /v1/predictions", "POST /v1/documents/analyze", "POST /v1/documents/validate" },
    research_limit = CorpusModelService.ResearchLimit
}));
app.MapGet("/health", (CorpusModelService models) => Results.Json(new
{
    ok = true,
    service = "tym-corpus-api",
    model_directory_configured = models.IsConfigured
}));
app.MapGet("/v1/models", (CorpusModelService models) => Results.Json(new { models = models.Catalog() }));
app.MapGet("/ready", (CorpusModelService models) =>
{
    var readiness = models.Ready();
    return Results.Json(readiness, statusCode: readiness.Ready ? 200 : 503);
});
app.MapPost("/v1/predictions", PredictionEndpoint.Handle);
app.MapPost("/v1/documents/analyze", DocumentEndpoints.Analyze);
app.MapPost("/v1/documents/validate", DocumentEndpoints.Validate);
app.Run();

public partial class Program;
