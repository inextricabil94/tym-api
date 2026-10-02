using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Json(new { ok = true, service = "tym-corpus-ui" }));
app.MapGet("/config.js", (HttpContext context, IConfiguration configuration) =>
{
    var apiBaseUrl = (configuration["TYM_API_BASE_URL"] ?? "http://127.0.0.1:8871").TrimEnd('/');
    context.Response.Headers.CacheControl = "no-store";
    var config = JsonSerializer.Serialize(new
    {
        apiBaseUrl,
        diagramUiUrl = "https://tym-ui-serban.livelyrock-2726c024.eastus.azurecontainerapps.io",
        resultsBaseUrl = "/results"
    });
    return Results.Text($"window.TYM_CONFIG = {config};", "application/javascript; charset=utf-8");
});
app.MapFallbackToFile("index.html");
app.Run();
