using System.ComponentModel.DataAnnotations;

namespace WebApiProjectWithMongoDB.Services;

/// <summary>
/// Strongly typed binding for the "MongoDB" section of appsettings.json.
///
/// Bound with options validation in Program.cs so a missing or malformed connection
/// string fails at startup with a clear message, rather than at the first request
/// with a confusing timeout.
/// </summary>
public sealed class MongoDbSettings
{
    /// <summary>Configuration section name, kept here so it isn't a magic string.</summary>
    public const string SectionName = "MongoDB";

    /// <summary>
    /// Your MongoDB connection string (a "mongodb://" or "mongodb+srv://" URI).
    ///
    /// Do not commit a real connection string with credentials. In development use
    /// .NET user secrets or the MONGODB_URI environment variable; in production use
    /// your platform's secret store. See the README for the exact commands.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "A MongoDB connection string is required.")]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The database to use. MongoDB creates databases and collections lazily, on
    /// first write, so this does not need to exist up front.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DatabaseName { get; set; } = string.Empty;

#if (!SkipSample)
    /// <summary>
    /// Collection backing the example <see cref="Models.Book"/> endpoints.
    ///
    /// Defaulted here rather than in appsettings.json, since there's no reason to
    /// make you edit config for it. Add a "BooksCollectionName" key under the
    /// "MongoDB" section if you do want to override it.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string BooksCollectionName { get; set; } = "books";
#endif
}
