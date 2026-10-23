using Microsoft.Data.Sqlite;

internal static class CacheChecks
{
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UnixEpoch; public override DateTimeOffset GetUtcNow() => Now; }
    public static void Run(DiagramResponse response)
    {
        var directory = Path.Combine(Path.GetTempPath(), "tym-cache-check-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "diagrams.db"); var clock = new Clock();
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        try
        {
            var cache = new DiagramDatabaseCache(path, maxEntries: 2, ttl: TimeSpan.FromMinutes(1), clock: clock);
            var request = new DiagramRequest("🙂 Ana a venit.", new DiagramOptions(Language: "ro"));
            var key = DiagramDatabaseCache.Key(request, "client-a", "v1");
            Check(key != DiagramDatabaseCache.Key(request, "client-b", "v1"), "Cache leaks across clients");
            Check(key != DiagramDatabaseCache.Key(request, "client-a", "v2"), "Model namespace not keyed");
            foreach (var changed in new[] { request with { Text = request.Text + " " }, request with { Options = request.Options! with { Width = 900 } },
                request with { Options = request.Options! with { Language = "en" } }, request with { Options = request.Options! with { TrackDensity = 100 } },
                request with { Options = request.Options! with { SegmentationAlgorithm = "mlnet_logistic" } } })
                Check(key != DiagramDatabaseCache.Key(changed, "client-a", "v1"), "Cache identity collision");
            cache.Put(key, response with { DensityCache = new DensityCacheInfo([], new Dictionary<string, CachedDensityView>(), "never-persist", [10]) });
            var restored = new DiagramDatabaseCache(path, clock: clock).Get(key)!;
            Check(restored.Render.Svg == response.Render.Svg && restored.DensityCache!.SessionId is null
                && restored.DensityCache.PendingDensities!.Length == 0, "Database persistence/session sanitization failed");
            clock.Now = clock.Now.AddSeconds(1); cache.Put("second", response);
            clock.Now = clock.Now.AddSeconds(1); cache.Put("third", response);
            Check(cache.Get(key) is null && cache.Get("third") is not null, "Database bound not enforced");
            clock.Now = clock.Now.AddMinutes(2); Check(cache.Get("third") is null, "Expired entry restored");
            new DiagramDatabaseCache(path, maxBytes: 1).Put("oversized", response);
            Check(cache.Get("oversized") is null, "Oversized response stored");
            Check(new DiagramDatabaseCache(null).Get(key) is null, "Disabled cache should miss");
            File.WriteAllText(Path.Combine(directory, "not-directory"), "fixture");
            var unavailable = new DiagramDatabaseCache(Path.Combine(directory, "not-directory", "bad.db"));
            unavailable.Put(key, response); Check(unavailable.Get(key) is null, "Unavailable storage did not fail open");
            Console.WriteLine("SQLite diagram cache: restart persistence, TTL, bounds, identity, session removal and unavailable-storage checks passed.");
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
