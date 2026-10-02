using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Tym.Corpus.Data;

/// <summary>Private corpus I/O. ZIP entries are read in place and never extracted.</summary>
public static class CorpusFiles
{
    public const long MaximumArchiveBytes = 100 * 1024 * 1024;
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Hash(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string NormalizeSpaces(string text) => Regex.Replace(text.Normalize(), @"\s+", " ").Trim();

    public static string Slug(string text)
    {
        var ascii = new string(text.Normalize(NormalizationForm.FormKD)
            .Where(character => character <= 127 && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());
        var slug = Regex.Replace(ascii.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "document" : slug;
    }

    public static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > MaximumArchiveBytes)
            throw new InvalidDataException("Archive exceeds the entry or expanded-size limit.");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.StartsWith('/') || name.Contains('\\') || name.Contains(':') || name.Split('/').Any(part => part == ".."))
                throw new InvalidDataException($"Unsafe ZIP entry: {name}");
            if (entry.Length / Math.Max(1.0, entry.CompressedLength) > 100)
                throw new InvalidDataException($"ZIP entry exceeds the compression-ratio limit: {name}");
        }
    }

    public static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaximumArchiveBytes) throw new InvalidDataException("ZIP entry is too large.");
        using var input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        if (output.Length != entry.Length) throw new InvalidDataException("ZIP entry length does not match its metadata.");
        return output.ToArray();
    }

    public static XDocument ParseXml(string text)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumArchiveBytes,
            MaxCharactersFromEntities = 1024
        };
        using var reader = XmlReader.Create(new StringReader(text), settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    public static void WriteJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(Json) { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }

    public static void WriteJsonLines<T>(string path, IEnumerable<T> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var row in rows) writer.WriteLine(JsonSerializer.Serialize(row, Json));
    }
}

/// <summary>Common task row consumed by ML.NET; source review status is never upgraded automatically.</summary>
public sealed record CorpusRow(string Id, string Task, string Text, string Label, string Language,
    string SourceType, string SourceGroup, string ReviewStatus, string? ParentId, string DocumentId, string ChapterId);

public sealed record BookRow(string Id, string Text, string Language, string SourceType,
    string SourceGroup, string DocumentId, string ChapterId);

public sealed record BookSource(string Source, string DocumentId, string ChapterId, string Text,
    string Encoding, int Bytes, string Sha256);
