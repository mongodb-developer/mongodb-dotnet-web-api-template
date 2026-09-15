using Microsoft.Extensions.Options;
using MongoDB.Driver;
using WebApiProjectWithMongoDB.Models;

namespace WebApiProjectWithMongoDB.Services;

/// <summary>
/// Creates the indexes the app relies on, once, at startup.
///
/// Why this exists: an unindexed query forces a collection scan. It looks fine on
/// 100 documents in development and falls over on 10 million in production. Create
/// indexes deliberately, alongside the queries that need them.
///
/// CreateManyAsync is idempotent - an index that already exists with the same
/// definition is a no-op, so this is safe to run on every boot.
///
/// A caveat worth knowing before you ship: on a large existing collection, building
/// an index takes real time and resources. For a brand new app (the case this
/// template targets) doing it at startup is ideal. Once you have serious data
/// volume, move index creation into a migration step or run it from Atlas so a
/// deploy never blocks on an index build.
/// </summary>
public sealed class MongoDbIndexInitializer : IHostedService
{
    private readonly IMongoDatabase _database;
    private readonly MongoDbSettings _settings;
    private readonly ILogger<MongoDbIndexInitializer> _logger;

    public MongoDbIndexInitializer(
        IMongoDatabase database,
        IOptions<MongoDbSettings> options,
        ILogger<MongoDbIndexInitializer> logger)
    {
        _database = database;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var books = _database.GetCollection<Book>(_settings.BooksCollectionName);
        var builder = Builders<Book>.IndexKeys;

        var indexes = new[]
        {
            // Supports sorting and range queries on publication date. A descending
            // index also serves ascending sorts - MongoDB can walk an index either
            // way - so direction only matters for compound indexes.
            new CreateIndexModel<Book>(
                builder.Descending(book => book.PublishedOn),
                new CreateIndexOptions { Name = "publishedOn_desc" }),

            // A multikey index: indexing an array field indexes every element, so
            // "find books tagged X" is an index lookup rather than a scan.
            new CreateIndexModel<Book>(
                builder.Ascending(book => book.Tags),
                new CreateIndexOptions { Name = "tags_asc" }),

            // A compound index. Field order matters enormously: this serves queries
            // on author alone, and on author + title together, but NOT on title
            // alone. That's the "prefix rule" - a compound index can only be used
            // from the left. Design the order around your actual query patterns.
            new CreateIndexModel<Book>(
                builder.Ascending(book => book.Author).Ascending(book => book.Title),
                new CreateIndexOptions { Name = "author_title" }),
        };

        try
        {
            await books.Indexes.CreateManyAsync(indexes, cancellationToken);
            _logger.LogInformation("MongoDB indexes verified on collection {Collection}.", _settings.BooksCollectionName);
        }
        catch (Exception exception)
        {
            // Don't take the whole app down over indexes. Queries still work without
            // them, just slower - and the operator gets a loud warning to act on.
            _logger.LogError(exception, "Failed to create MongoDB indexes. The API will still start, but queries may be slow.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
