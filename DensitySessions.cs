using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;

internal sealed record PreparedAnalysis(DiagramResponse Response, Func<int, DiagramResponse> GenerateDensity);

public sealed class DensitySessions : IDisposable
{
    private sealed record Entry(string Client, Func<int, DiagramResponse> Generate)
    {
        public SemaphoreSlim Gate { get; } = new(1);
    }
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 4_000_000 });

    public string Add(string client, int characters, Func<int, DiagramResponse> generate)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        cache.Set(id, new Entry(client, generate), new MemoryCacheEntryOptions
        { Size = Math.Max(1, characters), SlidingExpiration = TimeSpan.FromMinutes(10) });
        return id;
    }

    public async Task<CachedDensityView?> Generate(string id, string client, int density, CancellationToken cancellation)
    {
        if (!cache.TryGetValue<Entry>(id, out var entry) || entry is null || entry.Client != client) return null;
        await entry.Gate.WaitAsync(cancellation);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            var result = entry.Generate(density);
            cancellation.ThrowIfCancellationRequested();
            return TymDensityCache.View(result);
        }
        finally { entry.Gate.Release(); }
    }

    public void Dispose() => cache.Dispose();
}
