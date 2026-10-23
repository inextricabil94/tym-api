using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public sealed class DiagramDatabaseCache
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private readonly object gate = new();
    private readonly string? path;
    private readonly long maxBytes;
    private readonly int maxEntries;
    private readonly TimeSpan ttl;
    private readonly TimeProvider clock;
    private readonly ILogger? logger;
    public bool Enabled => path is not null;

    public DiagramDatabaseCache(string? path, long maxBytes = 128 * 1024 * 1024, int maxEntries = 100,
        TimeSpan? ttl = null, TimeProvider? clock = null, ILogger? logger = null)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        this.maxBytes = maxBytes; this.maxEntries = maxEntries;
        this.ttl = ttl ?? TimeSpan.FromDays(1); this.clock = clock ?? TimeProvider.System; this.logger = logger;
    }

    public static string Key(DiagramRequest request, string client, string version)
    {
        // Hash the exact source and client separately; neither is stored as a database column.
        static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var options = request.Options ?? new DiagramOptions();
        return Hash(JsonSerializer.Serialize(new { schema = 1, version, client = Hash(client), source = Hash(request.Text),
            options, omitXml = options.OmitXmlExports }, Json));
    }

    public DiagramResponse? Get(string key)
    {
        if (!Enabled) return null;
        lock (gate)
        {
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT payload FROM diagrams WHERE key=$key AND expires>$now";
                command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$now", Now);
                var payload = command.ExecuteScalar() as string;
                if (payload is null) return null;
                var result = JsonSerializer.Deserialize<DiagramResponse>(payload, Json);
                using var touch = connection.CreateCommand();
                touch.CommandText = "UPDATE diagrams SET accessed=$now WHERE key=$key";
                touch.Parameters.AddWithValue("$now", Now); touch.Parameters.AddWithValue("$key", key); touch.ExecuteNonQuery();
                return result;
            }
            catch (Exception error) when (CacheFailure(error)) { Warn(error); return null; }
        }
    }

    public void Put(string key, DiagramResponse response)
    {
        if (!Enabled) return;
        // IP-bound, short-lived density session identifiers must never survive in persistent storage.
        var safe = response with { DensityCache = response.DensityCache is null ? null
            : response.DensityCache with { SessionId = null, PendingDensities = [] } };
        var payload = JsonSerializer.Serialize(safe, Json);
        var bytes = Encoding.UTF8.GetByteCount(payload);
        if (bytes > Math.Min(maxBytes, 16 * 1024 * 1024) || maxEntries < 1) return;
        lock (gate)
        {
            try
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                using var write = connection.CreateCommand(); write.Transaction = transaction;
                write.CommandText = "INSERT OR REPLACE INTO diagrams(key,payload,bytes,expires,accessed) VALUES($key,$payload,$bytes,$expires,$now)";
                write.Parameters.AddWithValue("$key", key); write.Parameters.AddWithValue("$payload", payload);
                write.Parameters.AddWithValue("$bytes", bytes); write.Parameters.AddWithValue("$expires", Now + (long)ttl.TotalMilliseconds);
                write.Parameters.AddWithValue("$now", Now); write.ExecuteNonQuery();
                using var expire = connection.CreateCommand(); expire.Transaction = transaction;
                expire.CommandText = "DELETE FROM diagrams WHERE expires<=$now";
                expire.Parameters.AddWithValue("$now", Now); expire.ExecuteNonQuery();
                while (true)
                {
                    using var size = connection.CreateCommand(); size.Transaction = transaction;
                    size.CommandText = "SELECT COUNT(*), COALESCE(SUM(bytes),0) FROM diagrams";
                    bool fits;
                    using (var reader = size.ExecuteReader()) { reader.Read(); fits = reader.GetInt64(0) <= maxEntries && reader.GetInt64(1) <= maxBytes; }
                    if (fits) break;
                    using var evict = connection.CreateCommand(); evict.Transaction = transaction;
                    evict.CommandText = "DELETE FROM diagrams WHERE key=(SELECT key FROM diagrams ORDER BY accessed,key LIMIT 1)";
                    evict.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception error) when (CacheFailure(error)) { Warn(error); }
        }
    }

    private long Now => clock.GetUtcNow().ToUnixTimeMilliseconds();
    private SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path!)!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path!, Pooling = false, DefaultTimeout = 2 }.ToString());
        try
        {
            connection.Open();
            using var schema = connection.CreateCommand();
            schema.CommandText = "PRAGMA auto_vacuum=FULL; CREATE TABLE IF NOT EXISTS diagrams(key TEXT PRIMARY KEY,payload TEXT NOT NULL,bytes INTEGER NOT NULL,expires INTEGER NOT NULL,accessed INTEGER NOT NULL);";
            schema.ExecuteNonQuery(); return connection;
        }
        catch { connection.Dispose(); throw; }
    }
    private static bool CacheFailure(Exception error) => error is SqliteException or IOException or UnauthorizedAccessException or JsonException;
    private void Warn(Exception error) => logger?.LogWarning("Diagram database cache unavailable ({ErrorType}); generation continues.", error.GetType().Name);
}
