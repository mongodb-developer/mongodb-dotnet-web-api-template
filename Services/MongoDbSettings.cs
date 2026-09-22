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

    /// <summary>
    /// Reported to MongoDB as the client application name. It shows up in Atlas
    /// connection metrics, in <c>db.currentOp()</c> and in server slow-query logs,
    /// which makes "which service is opening all these connections?" and "who ran
    /// this query?" answerable.
    ///
    /// The default follows MongoDB DevRel's naming convention
    /// (<c>devrel-medium-primary-secondary-optional</c>) and identifies the project
    /// as having come from this template, which is how template usage gets measured.
    /// Leaving it alone is genuinely helpful. If you'd rather report your own
    /// service name, set "MongoDB:ApplicationName" in configuration, or put
    /// <c>?appName=your-service</c> in the connection string - either wins over
    /// this default.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ApplicationName { get; set; } = "devrel-github-csharp-webapi-template";

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
