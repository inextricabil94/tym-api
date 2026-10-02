using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Tym.Corpus.Data;

/// <summary>Builds unlabeled passages, retaining document/chapter groups for later analysis.</summary>
public static class BookImporter
{
    private static readonly Regex SentenceBoundary = new("(?<=[.!?])\\s+(?=[A-ZĂÂÎȘȚ0-9\"„«])", RegexOptions.CultureInvariant);

    public static BookSource Decode(byte[] bytes, string source, string documentName, string chapterName)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        var encoding = "utf-8-sig";
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            encoding = "cp1250";
            text = Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
        text = text.Normalize().Replace('\u00A0', ' ').Replace("\0", string.Empty);
        return new(source, CorpusFiles.Slug(documentName), CorpusFiles.Slug(chapterName), text, encoding, bytes.Length, CorpusFiles.Hash(bytes));
    }

    public static IReadOnlyList<BookSource> ReadArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        CorpusFiles.ValidateArchive(archive);
        var sources = new List<BookSource>();
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')))
        {
            if (!Path.GetExtension(entry.FullName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The books archive contains a non-text entry.");
            var parts = entry.FullName.Split('/');
            var chapterName = Path.GetFileNameWithoutExtension(parts[^1]);
            var documentName = parts.Length > 2 ? parts[^2] : chapterName;
            sources.Add(Decode(CorpusFiles.ReadEntry(entry), entry.FullName, documentName, chapterName));
        }
        if (sources.Count == 0) throw new InvalidDataException("No book text files were found.");
        return sources;
    }

    public static BookSource ReadFile(string path) => Decode(File.ReadAllBytes(path), Path.GetFileName(path),
        Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(path));

    private static IEnumerable<string> SplitLong(string unit, int maximum)
    {
        var sentences = SentenceBoundary.Split(unit);
        var pieces = sentences.Length == 1 ? Regex.Split(unit, @"\s+") : sentences;
        var current = string.Empty;
        foreach (var piece in pieces)
        {
            if (piece.Length > maximum)
            {
                if (current.Length > 0) yield return current;
                current = string.Empty;
                if (sentences.Length == 1)
                {
                    // A token without word boundaries is sliced, never allowed to defeat the size limit.
                    for (var index = 0; index < piece.Length;)
                    {
                        var length = Math.Min(maximum, piece.Length - index);
                        if (index + length < piece.Length && char.IsHighSurrogate(piece[index + length - 1])
                            && char.IsLowSurrogate(piece[index + length])) length--;
                        if (length == 0) throw new ArgumentException("The passage limit cannot split a UTF-16 surrogate pair.");
                        yield return piece.Substring(index, length);
                        index += length;
                    }
                }
                else foreach (var fragment in SplitLong(piece, maximum)) yield return fragment;
            }
            else if (current.Length > 0 && current.Length + 1 + piece.Length > maximum)
            {
                yield return current;
                current = piece;
            }
            else current = (current + " " + piece).Trim();
        }
        if (current.Length > 0) yield return current;
    }

    /// <summary>Limits a private display sample without splitting a UTF-16 surrogate pair.</summary>
    public static string Prefix(string text, int maximum)
    {
        if (maximum < 1) throw new ArgumentException("A positive sample limit is required.");
        var length = Math.Min(text.Length, maximum);
        if (length < text.Length && char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length])) length--;
        return text[..length];
    }

    public static IReadOnlyList<string> Passages(string text, int target = 1800, int maximum = 2400, int minimum = 300)
    {
        if (minimum < 1 || target < minimum || maximum < target) throw new ArgumentException("Invalid passage limits.");
        var units = Regex.Split(text, @"(?:\r?\n\s*){2,}").Select(CorpusFiles.NormalizeSpaces)
            .Where(paragraph => paragraph.Length > 0)
            .SelectMany(paragraph => paragraph.Length > maximum ? SplitLong(paragraph, maximum) : [paragraph]);
        var result = new List<string>();
        var current = string.Empty;
        foreach (var unit in units)
        {
            if (current.Length > 0 && current.Length + 2 + unit.Length > target)
            {
                result.Add(current);
                current = unit;
            }
            else current = (current + "\n\n" + unit).Trim();
        }
        if (current.Length > 0)
        {
            if (result.Count > 0 && current.Length < minimum && result[^1].Length + 2 + current.Length <= maximum)
                result[^1] += "\n\n" + current;
            else result.Add(current);
        }
        return result.Where(passage => passage.Length >= minimum).ToArray();
    }

    public static (IReadOnlyList<BookRow> Rows, object Report) Prepare(IEnumerable<BookSource> sources, string language)
    {
        var rows = new List<BookRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var summaries = new List<object>();
        var skipped = 0;
        foreach (var source in sources)
        {
            var accepted = 0;
            var passages = Passages(source.Text);
            for (var index = 0; index < passages.Count; index++)
            {
                var passage = passages[index];
                if (!seen.Add(CorpusFiles.Hash(passage))) { skipped++; continue; }
                accepted++;
                rows.Add(new($"book:{language}:{source.DocumentId}:{source.ChapterId}:{index + 1:D5}", passage, language,
                    "unlabeled_user_provided", "user-provided-books-20261002", source.DocumentId, source.ChapterId));
            }
            summaries.Add(new { source.Source, source.DocumentId, source.ChapterId, source.Encoding, source.Bytes, source.Sha256, passages = accepted });
        }
        if (rows.Count == 0 || rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw new InvalidDataException("Empty corpus or duplicate generated passage IDs.");
        return (rows, new
        {
            language, passages = rows.Count, documents = rows.Select(row => row.DocumentId).Distinct().Count(),
            source_files = summaries.Count, characters = rows.Sum(row => row.Text.Length),
            exact_duplicate_passages_skipped = skipped, sources = summaries,
            provenance = "unlabeled_user_provided; no supervised or gold-label claim"
        });
    }
}
