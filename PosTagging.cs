using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed record PosToken(int SpanStart, int SpanEnd, string Pos, string Tag, bool Possessive);
public sealed record PosFilterInfo(string Provider, string Model, IReadOnlyList<string> AllowedPos,
    IReadOnlyDictionary<string, int> ExcludedTokenCounts);

internal sealed class PosTaggingUnavailableException : Exception { }

internal sealed record PosTaggedText(string Provider, string Model, int CharacterCount, IReadOnlyList<PosToken> Tokens)
{
    public IEnumerable<PosToken> Overlapping(int start, int end)
    {
        var low = 0;
        var high = Tokens.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Tokens[middle].SpanEnd <= start) low = middle + 1;
            else high = middle;
        }
        for (var index = low; index < Tokens.Count && Tokens[index].SpanStart < end; index++) yield return Tokens[index];
    }
}

internal static class TymPosTagger
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static Process? worker;
    private static readonly Dictionary<string, PosTaggedText> Cache = new();
    private static readonly Queue<string> CacheKeys = new();
    private static int cachedCharacters;

    static TymPosTagger() => AppDomain.CurrentDomain.ProcessExit += (_, _) => StopWorker();

    public static PosTaggedText Tag(string text, string language)
    {
        var key = language + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            try
            {
                if (worker is null || worker.HasExited) StartWorker();
                worker!.StandardInput.WriteLine(JsonSerializer.Serialize(new { text, language }, Json));
                worker.StandardInput.Flush();
                var line = worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)).GetAwaiter().GetResult();
                if (line is null) throw new PosTaggingUnavailableException();
                using var response = JsonDocument.Parse(line);
                if (response.RootElement.TryGetProperty("error", out _)) throw new PosTaggingUnavailableException();
                var tagged = JsonSerializer.Deserialize<PosTaggedText>(line, Json)!;
                if (tagged.Provider != "spaCy" || tagged.CharacterCount != text.Length) throw new PosTaggingUnavailableException();
                var end = 0;
                foreach (var token in tagged.Tokens)
                {
                    if (token.SpanStart < end || token.SpanEnd <= token.SpanStart || token.SpanEnd > text.Length || string.IsNullOrEmpty(token.Pos))
                        throw new PosTaggingUnavailableException();
                    end = token.SpanEnd;
                }
                Cache[key] = tagged;
                CacheKeys.Enqueue(key);
                cachedCharacters += text.Length;
                while (CacheKeys.Count > 8 || cachedCharacters > 2_000_000)
                {
                    var remove = CacheKeys.Dequeue();
                    cachedCharacters -= Cache[remove].CharacterCount;
                    Cache.Remove(remove);
                }
                return tagged;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                StopWorker();
                throw new PosTaggingUnavailableException();
            }
        }
    }

    private static void StartWorker()
    {
        var settingsPath = Environment.GetEnvironmentVariable("TYM_POS_SETTINGS_FILE")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "private-models", "pos-settings.json");
        Dictionary<string, string>? settings = File.Exists(settingsPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsPath)) : null;
        string? Setting(string name) => Environment.GetEnvironmentVariable(name) ?? settings?.GetValueOrDefault(name);
        var python = Setting("TYM_POS_PYTHON");
        if (string.IsNullOrWhiteSpace(python)) throw new PosTaggingUnavailableException();
        var script = Path.Combine(AppContext.BaseDirectory, "tools", "tym_pos_worker.py");
        if (!File.Exists(script)) script = Path.Combine(Directory.GetCurrentDirectory(), "tools", "tym_pos_worker.py");
        var info = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "-X", "utf8", "-B", "-u", script }) info.ArgumentList.Add(argument);
        foreach (var name in new[] { "TYM_POS_EN_MODEL", "TYM_POS_RO_MODEL" })
            if (Setting(name) is { } value) info.Environment[name] = value;
        info.Environment["TOKENIZERS_PARALLELISM"] = "false";
        worker = Process.Start(info) ?? throw new PosTaggingUnavailableException();
        // Drain diagnostics without recording private input or model exception messages in API logs.
        worker.ErrorDataReceived += (_, _) => { };
        worker.BeginErrorReadLine();
    }

    private static void StopWorker()
    {
        try { if (worker is { HasExited: false }) worker.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        worker?.Dispose();
        worker = null;
    }
}
