using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Json(new { ok = true, service = "tym-ui" }));

app.MapGet("/local-examples/{id}/text", (string id, IConfiguration configuration, IWebHostEnvironment environment, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!IsLocalDevelopment(environment, context) || !Regex.IsMatch(id, "\\A[a-z0-9-]+\\z", RegexOptions.CultureInvariant))
    {
        return Results.NotFound();
    }

    var examplesPath = Path.GetFullPath(PrivateExamplesPath(configuration, environment));
    var path = Path.Combine(Path.GetDirectoryName(examplesPath)!, "full-text", id + ".txt");
    return File.Exists(path)
        ? Results.File(path, "text/plain; charset=utf-8")
        : Results.NotFound();
});

app.MapGet("/config.js", (IConfiguration configuration, IWebHostEnvironment environment, HttpContext context) =>
{
    var apiBaseUrl = configuration["TYM_API_BASE_URL"]
        ?? "https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io";

    JsonElement[] examples = [];
    if (IsLocalDevelopment(environment, context))
    {
        var examplesPath = PrivateExamplesPath(configuration, environment);

        if (File.Exists(examplesPath))
        {
            try
            {
                examples = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(examplesPath)) ?? [];
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
            {
                app.Logger.LogWarning("Private examples unavailable ({ErrorType}).", error.GetType().Name);
            }
        }
    }

    context.Response.Headers.CacheControl = "no-store";
    var config = JsonSerializer.Serialize(new
    {
        apiBaseUrl = apiBaseUrl.TrimEnd('/'),
        examples
    });

    return Results.Text($"window.TYM_CONFIG = {config};", "application/javascript");
});

app.MapFallbackToFile("index.html");

app.Run();

static bool IsLocalDevelopment(IWebHostEnvironment environment, HttpContext context)
{
    return environment.IsDevelopment() &&
        context.Connection.RemoteIpAddress is { } remoteAddress &&
        IPAddress.IsLoopback(remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress);
}

static string PrivateExamplesPath(IConfiguration configuration, IWebHostEnvironment environment) =>
    configuration["TYM_PRIVATE_EXAMPLES_FILE"]
    ?? Path.Combine(environment.ContentRootPath, "..", "..", "private-examples", "examples.json");
