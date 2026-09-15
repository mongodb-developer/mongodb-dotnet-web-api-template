using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using WebApiProjectWithMongoDB.Models;

namespace WebApiProjectWithMongoDB.Services;

/// <summary>
/// Data access for the example <see cref="Book"/> collection.
///
/// Keeping driver calls in a service like this (rather than inline in endpoints)
/// means your HTTP layer never has to know about filters, projections or BSON.
/// Swap this for a repository interface if you want to mock it in tests.
/// </summary>
public sealed class BookService
{
    private readonly IMongoCollection<Book> _books;

    public BookService(IMongoDatabase database, IOptions<MongoDbSettings> options)
    {
        // GetCollection<T> is cheap and thread-safe. It does not talk to the server -
        // MongoDB creates the collection lazily on first write.
        _books = database.GetCollection<Book>(options.Value.BooksCollectionName);
    }

    /// <summary>
    /// Lists books, newest first.
    ///
    /// Always paginate. An unbounded Find() will happily stream a million documents
    /// into your API process' memory. Note the Limit is applied server-side, so
    /// MongoDB only sends what you asked for.
    ///
    /// A caveat on Skip: the server still has to walk and discard every skipped
    /// document, so page 1,000 costs far more than page 1. It's fine for the first
    /// handful of pages, which covers most UIs. If you need to page deep, switch to
    /// range-based pagination - remember the sort value of the last item you showed
    /// and filter Lt(book =&gt; book.PublishedOn, lastSeen) instead of skipping. That
    /// uses the index directly and stays constant-time however far in you are.
    /// </summary>
    public async Task<IReadOnlyList<Book>> GetAsync(
        int skip = 0,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        return await _books
            // Builders<T> gives you a typed, refactor-safe way to write queries.
            // Filter.Empty is the equivalent of db.books.find({}).
            .Find(Builders<Book>.Filter.Empty)
            .SortByDescending(book => book.PublishedOn)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches one book by its id.
    ///
    /// Returns null rather than throwing when nothing matches - "not found" is an
    /// expected outcome here, not an exceptional one.
    /// </summary>
    public async Task<Book?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        // Guard before hitting the database. A malformed id can never match a real
        // ObjectId, and letting the driver throw a FormatException would surface as
        // a 500 when the honest answer is 404.
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        return await _books
            .Find(book => book.Id == id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Finds books carrying a given tag. Backed by the multikey index created in
    /// <see cref="MongoDbIndexInitializer"/>, so this stays fast as data grows.
    /// </summary>
    public async Task<IReadOnlyList<Book>> GetByTagAsync(
        string tag,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        // AnyEq matches documents where the array contains the value. MongoDB
        // applies array queries element-wise, so no $unwind is needed.
        var filter = Builders<Book>.Filter.AnyEq(book => book.Tags, tag);

        // Bounded, like every other read here. A popular tag could match the whole
        // collection, and "it was fine in testing" is not a capacity plan.
        return await _books
            .Find(filter)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Projection example: fetches only the fields needed for a title list.
    ///
    /// Projections are applied server-side, so MongoDB never sends the fields you
    /// left out. On wide documents this is one of the cheapest performance wins
    /// available - less disk read, less network, less deserialization.
    ///
    /// Take it further with a covered query: if every field you project is present
    /// in an index, MongoDB answers from the index alone and never touches the
    /// documents at all.
    /// </summary>
    public async Task<IReadOnlyList<BookTitle>> GetTitlesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _books
            .Find(Builders<Book>.Filter.Empty)
            .Project(book => new BookTitle(book.Id!, book.Title, book.Author))
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts a book. The driver assigns the generated ObjectId back onto the
    /// passed instance, so <paramref name="book"/>.Id is populated on return.
    /// </summary>
    public async Task<Book> CreateAsync(Book book, CancellationToken cancellationToken = default)
    {
        await _books.InsertOneAsync(book, cancellationToken: cancellationToken);
        return book;
    }

    /// <summary>
    /// Replaces a book wholesale. Returns false if no document matched.
    ///
    /// ReplaceOne overwrites the entire document. If you only mean to change a
    /// couple of fields, prefer UpdateOne with Builders.Update.Set - it sends less
    /// over the wire and won't clobber fields written by another process.
    /// </summary>
    public async Task<bool> UpdateAsync(string id, Book book, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return false;
        }

        book.Id = id;

        var result = await _books.ReplaceOneAsync(
            existing => existing.Id == id,
            book,
            cancellationToken: cancellationToken);

        // MatchedCount, not ModifiedCount: replacing a document with identical
        // content matches but modifies nothing, and that still means "found".
        return result.MatchedCount > 0;
    }

    /// <summary>
    /// Targeted partial update - only the page count changes. Shown as a contrast
    /// to the full replace above.
    /// </summary>
    public async Task<bool> UpdatePageCountAsync(
        string id,
        int pageCount,
        CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return false;
        }

        var update = Builders<Book>.Update.Set(book => book.PageCount, pageCount);

        var result = await _books.UpdateOneAsync(
            book => book.Id == id,
            update,
            cancellationToken: cancellationToken);

        return result.MatchedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return false;
        }

        var result = await _books.DeleteOneAsync(book => book.Id == id, cancellationToken);
        return result.DeletedCount > 0;
    }

    /// <summary>
    /// An aggregation pipeline: count books per author, busiest first.
    ///
    /// Aggregation is where MongoDB gets genuinely powerful - grouping, joining,
    /// reshaping and computing all happen on the server, so you transfer results
    /// rather than raw documents. This is the equivalent of:
    ///   SELECT author, COUNT(*) AS total FROM books GROUP BY author ORDER BY total DESC
    /// </summary>
    public async Task<IReadOnlyList<AuthorSummary>> GetBooksPerAuthorAsync(
        CancellationToken cancellationToken = default)
    {
        return await _books
            .Aggregate()
            .Group(
                book => book.Author,
                grouping => new AuthorSummary
                {
                    Author = grouping.Key,
                    BookCount = grouping.Count(),
                    TotalPages = grouping.Sum(book => book.PageCount),
                })
            .SortByDescending(summary => summary.BookCount)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Lightweight projection result. Not a stored document.</summary>
public sealed record BookTitle(string Id, string Title, string Author);

/// <summary>Shape returned by the aggregation above. Not a stored document.</summary>
public sealed class AuthorSummary
{
    public string Author { get; set; } = string.Empty;

    public int BookCount { get; set; }

    public int TotalPages { get; set; }
}
