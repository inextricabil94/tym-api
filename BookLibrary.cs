using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed record BookSection(string Id, string Label, string Kind, int Start, int End, int CharacterCount);
public sealed record LibraryBook(string Id, string Label, string Language, string Source, int CharacterCount,
    int OriginalCharacterCount, string Sha256Utf8, IReadOnlyList<BookSection> Sections);
public sealed record BookAnalysisRequest(string? SectionId = null, DiagramOptions? Options = null);
public sealed record BookSource(string BookId, string? SectionId, string Language, string Sha256Utf8,
    int Start, int End, string Text);

public sealed class BookLibrary
{
    // Source stays outside wwwroot; access policy is applied by the source endpoint.
    private readonly IReadOnlyDictionary<string, (LibraryBook Book, string Text)> books;

    public BookLibrary(IConfiguration configuration)
    {
        var folder = configuration["TYM_LIBRARY_PATH"];
        var loaded = new Dictionary<string, (LibraryBook, string)>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var catalog = JsonSerializer.Deserialize<LibraryBook[]>(File.ReadAllText(Path.Combine(folder, "catalog.json")), json)
                ?? throw new InvalidDataException("Invalid book library catalog.");
            foreach (var book in catalog)
            {
                if (!Regex.IsMatch(book.Id, "\\A[a-z0-9-]+\\z") || book.Language is not ("en" or "ro"))
                    throw new InvalidDataException("Invalid book metadata.");
                var text = File.ReadAllText(Path.Combine(folder, book.Id + ".txt"), Encoding.UTF8);
                var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                if (text.Length != book.CharacterCount || hash != book.Sha256Utf8)
                    throw new InvalidDataException("Book integrity check failed.");
                var end = 0;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var section in book.Sections)
                {
                    if (section.Start != end || section.End <= section.Start || section.End > text.Length
                        || section.CharacterCount != section.End - section.Start || !ids.Add(section.Id))
                        throw new InvalidDataException("Invalid chapter boundaries.");
                    end = section.End;
                }
                if (book.Sections.Count > 0 && end != text.Length) throw new InvalidDataException("Incomplete chapters.");
                loaded.Add(book.Id, (book, text));
            }
        }
        books = loaded;
    }

    public IEnumerable<LibraryBook> Catalog => books.Values.Select(value => value.Book);

    public BookSource? ReadSource(string id, string? sectionId)
    {
        if (!books.TryGetValue(id, out var entry)) return null;
        var section = sectionId is null ? null : entry.Book.Sections.FirstOrDefault(item => item.Id == sectionId);
        if (sectionId is not null && section is null) return null;
        var start = section?.Start ?? 0;
        var end = section?.End ?? entry.Text.Length;
        return new BookSource(id, section?.Id, entry.Book.Language, entry.Book.Sha256Utf8, start, end, entry.Text[start..end]);
    }

    public DiagramRequest? Read(string id, BookAnalysisRequest request)
    {
        if (!books.TryGetValue(id, out var entry)) return null;
        var text = entry.Text;
        if (request.SectionId is { } sectionId)
        {
            var section = entry.Book.Sections.FirstOrDefault(section => section.Id == sectionId);
            if (section is null) return null;
            text = text[section.Start..section.End];
        }
        return new DiagramRequest(text, (request.Options ?? new DiagramOptions()) with { Language = entry.Book.Language });
    }
}
