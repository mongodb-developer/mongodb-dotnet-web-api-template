# WebApiProjectWithMongoDB

An ASP.NET Core Web API wired up to MongoDB, generated from the `mongodb-webapi` template.

Everything here is heavily commented on purpose - the code is meant to be read, not
just run. Once a file has taught you what it needs to, trim the comments back.

## Getting started

```bash
dotnet run
```

Then open `http://localhost:5241/scalar/v1` for a browsable API explorer.
<!--#if (!SkipSample) -->
There's also a ready-made set of requests in `WebApiProjectWithMongoDB.http`, which
Visual Studio and VS Code (with the REST Client extension) can run directly.
<!--#endif -->

Check the database connection first:

```bash
curl http://localhost:5241/health
```

## What's in the box

| Path | What it does |
| --- | --- |
| `Program.cs` | App startup, controller registration, HTTP pipeline, health checks |
| `Services/MongoDbRegistration.cs` | **Start here.** Singleton `MongoClient`, pool and timeout tuning, serialization conventions |
| `Services/MongoDbSettings.cs` | Strongly typed, validated config binding |
| `Services/MongoDbHealthCheck.cs` | Pings the cluster for readiness probes |
<!--#if (!SkipSample) -->
| `Services/BookService.cs` | Data access: find, projection, insert, replace, `$set` update, delete, aggregation |
| `Services/MongoDbIndexInitializer.cs` | Creates indexes at startup, idempotently |
| `Models/Book.cs` | Example document with BSON attributes explained |
| `Models/BookDtos.cs` | Request/response types, kept separate from the document |
| `Controllers/BooksController.cs` | CRUD controller - HTTP concerns only, no driver code |
<!--#endif -->

## Securing your connection string

The template wrote your connection string into `appsettings.Development.json`, which
`.gitignore` excludes. That's fine for local work. **Never commit a connection
string containing credentials** - an Atlas URI embeds your cluster username and
password.

For anything beyond local development, pick one of these instead.

### .NET user secrets (local, keeps it out of the repo entirely)

```bash
dotnet user-secrets set "MongoDB:ConnectionString" "mongodb+srv://user:pass@cluster.mongodb.net"
```

### Environment variable

The app reads `MONGODB_URI` and lets it override everything else, because that's the
name most hosting platforms and MongoDB tooling already use:

```bash
export MONGODB_URI="mongodb+srv://user:pass@cluster.mongodb.net"
```

The double-underscore form also works, if you prefer to mirror the config path:

```bash
export MongoDB__ConnectionString="mongodb+srv://user:pass@cluster.mongodb.net"
```

### Production

Use your platform's secret store - Azure Key Vault, AWS Secrets Manager, Kubernetes
secrets. Better still, use passwordless auth: MongoDB Atlas supports workload
identity via AWS IAM, Azure Managed Identity and GCP service accounts, so there's no
credential to leak in the first place.

## The MongoDB practices this template bakes in

**One `MongoClient`, registered as a singleton.** This is the big one. `MongoClient`
owns the connection pool and the background topology monitoring threads. It's
thread-safe and built to live for the whole process. Creating one per request opens
a fresh pool every time - each connection costs a TCP handshake, a TLS handshake and
an auth round trip - and you'll exhaust your cluster's connection limit under load.

**Explicit pool and timeout settings.** `MongoDbRegistration.cs` sets
`MaxConnectionPoolSize`, `MinConnectionPoolSize`, `MaxConnectionIdleTime`,
`ServerSelectionTimeout`, `ConnectTimeout`, `SocketTimeout` and `WaitQueueTimeout`,
each with a comment explaining the reasoning. The values assume a long-running web
server doing short OLTP-style operations. **They're a starting point, not an
answer** - measure your real concurrency and adjust. Budget your total connections
as `instances × (MaxPoolSize + 2) × replica set members` and keep that under your
cluster's limit.

**Config validated at startup.** A missing connection string throws while the app
boots, with a clear message, instead of surfacing as a mystery timeout on the first
request.

<!--#if (!SkipSample) -->
**Indexes created deliberately.** An unindexed query is a collection scan. It looks
fine on 100 documents and falls over on 10 million. `MongoDbIndexInitializer.cs`
creates them at startup and explains the compound-index prefix rule.
<!--#endif -->

**Lenient deserialization.** `IgnoreExtraElementsConvention` means a field appearing
in your documents before the matching code ships won't break every read. Essential
for rolling deploys.

**UTC dates, camelCase fields, `ObjectId` as `string`.** Small conventions, applied
globally so you don't repeat them per property.

**An `appName` the server can see.** The client reports `devrel-github-csharp-webapi-template`,
which appears in Atlas connection metrics, `db.currentOp()` and slow-query logs -
so you can tell which service opened a connection or ran a slow query. It also lets
MongoDB see how much this template is used, which is the main reason it exists;
leaving it is appreciated. To report your own name instead, set
`MongoDB:ApplicationName` in configuration or add `?appName=your-service` to the
connection string - either takes precedence.

**Cancellation tokens everywhere.** When a client disconnects, the query is
cancelled rather than left running.

**Validation at the edge.** `[ApiController]` runs the DataAnnotations attributes
on the request records automatically, so a malformed body is rejected with a 400
problem+json before any controller action - and before any database call - runs.

<!--#if (!SkipSample) -->
**Pagination that can't be abused.** Every read is bounded and the controller clamps
`limit`. Never let a caller decide how much of your database to load into memory.
`BookService.GetAsync` also explains when to abandon `Skip` for range-based paging -
the server walks and discards every skipped document, so deep pages get expensive.

**Projections.** `GetTitlesAsync` fetches only the fields it needs, so MongoDB never
sends the rest. On wide documents it's one of the cheapest wins available, and if
every projected field lives in an index the query is *covered* and never touches the
documents at all.
<!--#endif -->

**Stable API v1.** Pinned via `ServerApi`, so a cluster version upgrade can't
silently change the behaviour of the commands you rely on. `Strict` is left false, so
non-versioned commands still work.

**Driver logging wired into ASP.NET Core.** The driver's own diagnostics go to your
normal logger under the `MongoDB.Command`, `MongoDB.Connection`, `MongoDB.SDAM`,
`MongoDB.ServerSelection` and `MongoDB.Internal` categories. It's Debug level, so
silent by default - add `"MongoDB": "Debug"` under `Logging:LogLevel` to see every
command the driver sends, which is the fastest way to answer "what is it actually
doing?". `MongoDbRegistration.cs` notes the environment-variable gotcha.

**Majority read and write concern.** Left at the replica set / Atlas defaults on
purpose - durable and correct. Weaken per operation only where you've decided the
trade is worth it.

## Using AI assistants on this project

Two files set this up, and both are committed on purpose:

- **`.claude/settings.json`** enables [MongoDB's official Claude Code plugin](https://github.com/anthropics/claude-plugins-official).
  It brings skills for connection management, schema design, indexing, query
  optimisation and Atlas Search, plus the MongoDB MCP server - so an assistant
  working here follows MongoDB's own current guidance instead of improvising from
  half-remembered blog posts. Claude Code will offer to install it the first time you
  open the project; the marketplace it comes from is built in, so there's nothing
  else to configure.
- **`CLAUDE.md`** records this codebase's conventions - singleton `MongoClient`,
  bounded reads, UTC dates, where data access is allowed to live - so generated code
  matches the project rather than fighting it.

The plugin is referenced by name and version range rather than copied into the
project, so you get updates as MongoDB ships them. Delete either file if you'd rather
not have them.

### The MCP server

`.mcp.json` points the MongoDB MCP server at the connection string you passed to
`dotnet new`, so an assistant can inspect your *actual* database rather than guessing.
That makes a few things genuinely pleasant:

- "Look at the `products` collection and generate a C# model class for it" - it reads
  the real schema, including nested documents and array fields, and writes a model
  that matches the conventions in this project.
- "Why is this query slow?" - it can run `explain()` against your data and see which
  index was chosen.
- "What do the documents in this collection actually look like?" - it samples them
  instead of trusting your description.

Two deliberate choices in that file:

**`MDB_MCP_READ_ONLY` is `true`.** The agent can read schemas, sample documents and
explain queries, but cannot insert, update, drop or modify anything. That's the right
default when the point is code generation. Set it to `false` if you want it managing
indexes and collections for you, and be deliberate about pointing it at production.

**`.mcp.json` is gitignored,** because it holds your connection string - an Atlas URI
embeds your cluster username and password. Each developer generates their own. If you
want it committed instead, replace the value with `"${MDB_MCP_CONNECTION_STRING}"`
and set that variable in your shell.

The server runs on demand via `npx`, so there's nothing to install - though the first
run downloads the package and takes a moment. You'll be asked to approve the server
the first time you open the project.

Note that the plugin above *also* registers a MongoDB MCP server, so you may see two
sets of the same tools. The one in `.mcp.json` is the one carrying your connection
string; the plugin's expects `MDB_MCP_CONNECTION_STRING` from your environment. If
the duplication bothers you, delete `.mcp.json` and export that variable instead -
the `mongodb-mcp-setup` skill walks through it.

If you're on Atlas and would rather sign in with your Atlas account than manage a
connection string, there's a `mongodb-atlas@claude-plugins-official` plugin that uses
the Atlas Managed MCP Server and can also manage clusters, users and network access.
Swap the name in `.claude/settings.json` if you prefer it.

## Where to go next

<!--#if (!SkipSample) -->
- Swap `Book` for your own domain model, then revisit the indexes to match your real
  query patterns.
<!--#endif -->
- Add [Atlas Search](https://www.mongodb.com/docs/atlas/atlas-search/) if you need
  full-text search rather than exact matching.
- Add authentication - this template intentionally ships with none.
- Point the readiness probe (`/health`) at your container orchestrator so an
  instance that can't reach MongoDB is pulled out of rotation.

## Useful links

- [MongoDB .NET/C# driver docs](https://www.mongodb.com/docs/drivers/csharp/current/)
- [Connection pool tuning](https://www.mongodb.com/docs/drivers/csharp/current/connect/connection-options/connection-pools/)
- [Data modelling guide](https://www.mongodb.com/docs/manual/data-modeling/)
- [Indexing strategies](https://www.mongodb.com/docs/manual/applications/indexes/)
