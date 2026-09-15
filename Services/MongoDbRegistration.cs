using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;
using MongoDB.Driver.Core.Compression;
using MongoDB.Driver.Core.Configuration;

namespace WebApiProjectWithMongoDB.Services;

/// <summary>
/// Wires MongoDB into the dependency injection container.
/// </summary>
public static class MongoDbRegistration
{
    public static IServiceCollection AddMongoDb(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + validate the "MongoDB" config section. ValidateOnStart() means a bad
        // config throws while the app is booting instead of on the first request.
        services
            .AddOptions<MongoDbSettings>()
            .Bind(configuration.GetSection(MongoDbSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        RegisterConventions();

        // ---------------------------------------------------------------------
        // MongoClient is registered as a SINGLETON. This is the single most
        // important MongoDB best practice in a web app.
        //
        // MongoClient owns the connection pool and the background threads that
        // monitor your cluster's topology. It is thread-safe and designed to be
        // created once and shared for the lifetime of the process. Creating one
        // per request opens a brand new pool every time - each connection costs a
        // TCP handshake, a TLS handshake and an auth round trip (50-500ms), and
        // you will exhaust the cluster's connection limit under load.
        // ---------------------------------------------------------------------
        services.AddSingleton<IMongoClient>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;

            // Parse the URI first, then override specific settings in code. This way
            // anything the user put in their connection string is respected, and we
            // only fill in the values we care about.
            var clientSettings = MongoClientSettings.FromConnectionString(settings.ConnectionString);

            // -----------------------------------------------------------------
            // Connection pool tuning.
            //
            // These values assume a traditional long-running ASP.NET Core server
            // doing short OLTP-style reads and writes - the default shape for a
            // Web API. They are a sane starting point, NOT a universal answer.
            // Measure your real concurrency and adjust. If you deploy this to a
            // serverless host (Azure Functions, AWS Lambda) you want something
            // very different: MaxConnectionPoolSize 3-5, MinConnectionPoolSize 0.
            // -----------------------------------------------------------------

            // Ceiling on concurrent connections per server, per client. Must exceed
            // your peak concurrent database operations or requests will queue.
            // Budget it: instances x (MaxPoolSize + 2) x replica set members must
            // stay under your cluster's connection limit. The "+ 2" is the driver's
            // per-member topology monitoring connections.
            clientSettings.MaxConnectionPoolSize = 100;

            // Keep a few connections pre-warmed so the first requests after an idle
            // period don't each pay the handshake cost. Each idle connection costs
            // roughly 1 MB of RAM on the server, so keep this modest.
            clientSettings.MinConnectionPoolSize = 10;

            // Prune connections that have sat unused this long. Long enough to get
            // real reuse, short enough that idle capacity is returned to the cluster.
            clientSettings.MaxConnectionIdleTime = TimeSpan.FromMinutes(5);

            // How long to wait for a suitable server (primary, secondary, mongos)
            // before giving up. Covers replica set failover - too low and a normal
            // election looks like an outage; too high and a genuinely dead cluster
            // makes your API hang.
            clientSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

            // Fail fast on a TCP/TLS connect that isn't going to succeed.
            clientSettings.ConnectTimeout = TimeSpan.FromSeconds(10);

            // How long a single socket read/write may take. Sized for short OLTP
            // operations - fail fast rather than letting a request hang. If you run
            // long analytical aggregations, raise this to two or three times the
            // duration of your slowest expected operation.
            clientSettings.SocketTimeout = TimeSpan.FromSeconds(30);

            // If the pool is saturated, wait this long for a free connection before
            // giving up. Failing fast here surfaces pool exhaustion as a clear error
            // instead of unbounded latency creep.
            clientSettings.WaitQueueTimeout = TimeSpan.FromSeconds(5);

            // Retries are on by default in modern drivers, but being explicit
            // documents the intent: transient blips and failovers are retried once
            // automatically. Safe because retryable writes are idempotent.
            clientSettings.RetryReads = true;
            clientSettings.RetryWrites = true;

            // Pin to the Stable API. The server then guarantees the behaviour of
            // these commands across upgrades, so a cluster version bump can't
            // silently change your query semantics. Strict defaults to false, which
            // means commands outside the versioned set still work - you get the
            // guarantee without losing anything. Set Strict = true if you want the
            // server to actively reject anything non-versioned.
            clientSettings.ServerApi = new ServerApi(ServerApiVersion.V1);

            // Read and write concern are deliberately left at their defaults. On a
            // replica set or Atlas that means majority write concern and majority
            // read concern - durable, and correct for almost every application.
            // Weakening them trades data safety for latency, so only do it on a
            // per-operation basis where you've decided that's an acceptable trade.

            // Compress the wire protocol. Snappy is a pure-managed dependency of the
            // driver, so there's no native library to deploy. Well worth it when app
            // and cluster are in different regions, or on chatty read-heavy work.
            clientSettings.Compressors = [new CompressorConfiguration(CompressorType.Snappy)];

            // Identify this app in Atlas's connection metrics and in server logs.
            // Makes "which service is opening all these connections?" answerable.
            clientSettings.ApplicationName = "WebApiProjectWithMongoDB";

            // Route the driver's own diagnostics through ASP.NET Core logging, so
            // command execution, server selection and connection pool events land in
            // the same place as the rest of your logs.
            //
            // The driver logs under these categories:
            //   MongoDB.Command          - every command sent and its reply
            //   MongoDB.Connection       - connection pool checkout, open, close
            //   MongoDB.SDAM             - topology and server discovery
            //   MongoDB.ServerSelection  - which server an operation was routed to
            //   MongoDB.Internal         - driver internals
            //
            // Most of it is Debug level, so it's silent by default. To see it, add
            // to the "Logging:LogLevel" section of appsettings - "MongoDB" turns on
            // all of them, or name one category to be selective:
            //
            //   "MongoDB": "Debug"          or   "MongoDB.Command": "Debug"
            //
            // Watch out if you set this via an environment variable: the category
            // contains a literal dot, so it's Logging__LogLevel__MongoDB.Command,
            // not ...__MongoDB__Command - the double underscore would nest it into
            // a section that never matches.
            clientSettings.LoggingSettings = new LoggingSettings(
                serviceProvider.GetRequiredService<ILoggerFactory>());

            return new MongoClient(clientSettings);
        });

        // IMongoDatabase is cheap to obtain and also thread-safe, so resolving it
        // once as a singleton saves every consumer from repeating GetDatabase().
        services.AddSingleton<IMongoDatabase>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<MongoDbSettings>>().Value;
            return serviceProvider.GetRequiredService<IMongoClient>().GetDatabase(settings.DatabaseName);
        });

#if (!SkipSample)
        // Your own data-access types. Singleton is fine - they hold no per-request
        // state, only a thread-safe IMongoCollection handle.
        services.AddSingleton<BookService>();

        // Creates indexes at startup. See MongoDbIndexInitializer for the caveats.
        services.AddHostedService<MongoDbIndexInitializer>();
#endif

        return services;
    }

    /// <summary>
    /// Conventions are global serialization rules, applied to every mapped type so
    /// you don't have to repeat the same attribute on every property.
    ///
    /// Register these once, before any serialization happens - the driver caches
    /// each type's class map the first time it sees it, and conventions registered
    /// after that point are ignored for already-mapped types.
    /// </summary>
    private static void RegisterConventions()
    {
        var pack = new ConventionPack
        {
            // Store C# PascalCase properties as camelCase BSON fields, the MongoDB
            // community norm. The [BsonElement] attributes on Book are then belt
            // and braces - kept there because they're useful to read.
            new CamelCaseElementNameConvention(),

            // Don't write properties that are null. Missing beats null in MongoDB:
            // it keeps documents smaller and sparse indexes work as you'd expect.
            new IgnoreIfNullConvention(true),

            // Tolerate fields in the database that don't exist on the C# class.
            // Without this, adding a field to your documents before you deploy the
            // matching code change breaks every read. Essential for rolling deploys
            // against an evolving schema.
            new IgnoreExtraElementsConvention(true),
        };

        ConventionRegistry.Register("WebApiProjectWithMongoDbConventions", pack, _ => true);

    }
}
