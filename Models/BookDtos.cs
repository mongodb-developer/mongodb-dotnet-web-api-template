using System.ComponentModel.DataAnnotations;

namespace WebApiProjectWithMongoDB.Models;

/// <summary>
/// Request body for creating or replacing a book.
///
/// Why a separate type instead of accepting <see cref="Book"/> directly? Because
/// binding straight to your document model lets a caller set fields you never
/// intended them to - Id being the obvious one. A dedicated request type makes the
/// public contract explicit and keeps your storage shape free to change.
/// </summary>
public sealed record BookRequest(
    // Note there is no "property:" target on these attributes. On a record's
    // primary constructor an attribute defaults to the *parameter*, and that is
    // what MVC requires - it deliberately throws an InvalidOperationException if
    // it finds validation metadata on a record's properties instead, because that
    // metadata would be silently ignored. Easy mistake to make, loud when you do.
    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    string Title,

    [Required(AllowEmptyStrings = false)]
    [StringLength(200, MinimumLength = 1)]
    string Author,

    [Range(1, 50_000)]
    int PageCount,

    DateTime PublishedOn,

    IReadOnlyList<string>? Tags,

    string? PublisherName,

    string? PublisherCountry)
{
    /// <summary>Maps the request onto a storable document.</summary>
    public Book ToBook() => new()
    {
        Title = Title,
        Author = Author,
        PageCount = PageCount,

        // Normalise to UTC on the way in. Do this once, at the boundary, so
        // everything downstream can assume UTC without checking.
        PublishedOn = DateTime.SpecifyKind(PublishedOn, DateTimeKind.Utc),

        Tags = Tags?.ToList() ?? [],
        Publisher = PublisherName is null
            ? null
            : new Publisher { Name = PublisherName, Country = PublisherCountry ?? string.Empty },
    };
}

/// <summary>
/// Response shape. Decoupled from the document so adding an internal field to
/// <see cref="Book"/> doesn't silently leak into your public API.
/// </summary>
public sealed record BookResponse(
    string Id,
    string Title,
    string Author,
    int PageCount,
    DateTime PublishedOn,
    IReadOnlyList<string> Tags,
    string? PublisherName,
    string? PublisherCountry)
{
    public static BookResponse From(Book book) => new(
        book.Id ?? string.Empty,
        book.Title,
        book.Author,
        book.PageCount,
        book.PublishedOn,
        book.Tags,
        book.Publisher?.Name,
        book.Publisher?.Country);
}
