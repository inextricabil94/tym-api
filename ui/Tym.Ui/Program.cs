using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Json(new { ok = true, service = "tym-ui" }));

app.MapGet("/config.js", (IConfiguration configuration) =>
{
    var apiBaseUrl = configuration["TYM_API_BASE_URL"]
        ?? "https://tym-api-serban.livelyrock-2726c024.eastus.azurecontainerapps.io";

    var config = JsonSerializer.Serialize(new
    {
        apiBaseUrl = apiBaseUrl.TrimEnd('/')
    });

    return Results.Text($"window.TYM_CONFIG = {config};", "application/javascript");
});

app.MapFallbackToFile("index.html");

app.Run();
