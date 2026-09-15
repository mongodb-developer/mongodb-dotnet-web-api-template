using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WebApiProjectWithMongoDB.Models;

/// <summary>
/// An example document model. Replace this with your own domain type.
///
/// MongoDB stores BSON documents, and the .NET driver maps them to plain C# classes
/// for you. You only need attributes where the C# shape and the BSON shape differ.
/// </summary>
public sealed class Book
{
    /// <summary>
    /// Every MongoDB document needs an "_id". The driver maps a property called "Id"
    /// to "_id" automatically, but we spell it out here so it's obvious what's happening.
    ///
    /// [BsonRepresentation(BsonType.ObjectId)] lets you work with the id as a plain
    /// string in C# (handy for route parameters and JSON) while MongoDB still stores
    /// it as a real ObjectId. ObjectIds are 12 bytes and contain a creation timestamp,
    /// so they sort roughly by insertion order.
    ///
    /// Leave this null when inserting - the driver generates an ObjectId for you.
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    /// <summary>
    /// By default a C# property called "Title" is stored under the field name "Title".
    /// The MongoDB community convention is camelCase field names, so we map it.
    /// Shorter field names also mean smaller documents - field names are stored on
    /// every single document, so this genuinely adds up at scale.
    /// </summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("author")]
    public string Author { get; set; } = string.Empty;

    [BsonElement("pageCount")]
    public int PageCount { get; set; }

    /// <summary>
    /// Arrays are first-class in MongoDB - no join table needed. You can index them
    /// directly (a "multikey" index), which makes queries like
    /// "find all books tagged 'mongodb'" fast.
    ///
    /// Rule of thumb: embed arrays that are bounded. If a field could grow without
    /// limit (every review ever left on a book, say), reference a separate collection
    /// instead so you don't approach the 16 MB per-document limit.
    /// </summary>
    [BsonElement("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Store dates in UTC. MongoDB's BSON date type is always UTC milliseconds since
    /// the epoch, so this attribute makes the round-trip explicit and stops you
    /// getting local-time surprises when reading back.
    /// </summary>
    [BsonElement("publishedOn")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime PublishedOn { get; set; }

    /// <summary>
    /// An embedded sub-document. In a relational database this would be a join;
    /// in MongoDB it's just a nested object stored alongside the parent. Embedding
    /// is the right call when the data is always read together with its parent.
    /// </summary>
    [BsonElement("publisher")]
    [BsonIgnoreIfNull]
    public Publisher? Publisher { get; set; }
}

/// <summary>
/// Embedded sub-document. Note there's no Id here - it isn't a document in its own
/// right, it only exists inside a Book.
/// </summary>
public sealed class Publisher
{
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("country")]
    public string Country { get; set; } = string.Empty;
}
