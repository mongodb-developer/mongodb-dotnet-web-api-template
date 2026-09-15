using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using WebApiProjectWithMongoDB.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration
//
// ASP.NET Core layers configuration sources, last one wins:
//   appsettings.json -> appsettings.{Environment}.json -> user secrets (Dev only)
//   -> environment variables -> command line
//
// That ordering is what lets you keep a placeholder in appsettings.json and
// override it with a real secret at run time without touching the file.
//
// The template writes your connection string into appsettings.Development.json
// so you can F5 straight away. Before you commit anything, move it out - see
// the "Securing your connection string" section of the README.
// ---------------------------------------------------------------------------

// Accept the conventional MONGODB_URI environment variable as an override. Most
// hosting platforms and MongoDB tooling use this name, so honouring it means the
// app drops into a container or a PaaS with no extra config mapping.
var connectionStringFromEnvironment = builder.Configuration["MONGODB_URI"];
if (!string.IsNullOrWhiteSpace(connectionStringFromEnvironment))
{
    builder.Configuration["MongoDB:ConnectionString"] = connectionStringFromEnvironment;
}

// ---------------------------------------------------------------------------
// Services
// ---------------------------------------------------------------------------

// Registers MongoClient (singleton) and IMongoDatabase, plus the example
// BookService and index initializer. See Services/MongoDbRegistration.cs - that's
// where the interesting MongoDB configuration lives.
builder.Services.AddMongoDb(builder.Configuration);

// /health does a real ping against the cluster. Point your container orchestrator's
// readiness probe here.
builder.Services.AddHealthChecks()
    .AddCheck<MongoDbHealthCheck>("mongodb", tags: ["ready"]);

// Turns unhandled exceptions into RFC 9457 problem+json instead of an HTML error
// page, so API clients always get a parseable body.
builder.Services.AddProblemDetails();

// Discovers and registers the controllers in Controllers/. Note AddControllers,
// not AddControllersWithViews or AddRazorPages - this is an API, so we don't pull
// in the view engine.
//
// [ApiController] on each controller then gives you, for free:
//   - automatic 400 problem+json when a request model fails validation
//   - [FromBody] inferred for complex parameters
//   - attribute routing required, so every route is declared where you read it
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Don't emit nulls - keeps responses tight and mirrors the IgnoreIfNull
        // convention applied on the BSON side.
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------------

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Raw OpenAPI document at /openapi/v1.json.
    app.MapOpenApi();

    // Interactive API explorer at /scalar/v1. Handy for poking at the endpoints
    // without reaching for curl or Postman.
    app.MapScalarApiReference();
}

// Note for containerised deployments: this 307-redirects plain HTTP requests,
// including health probes. Either point your probes at HTTPS, or terminate TLS at
// the ingress and drop this line - don't leave probes chasing redirects.
app.UseHttpsRedirection();

// Liveness: is the process up? Deliberately does NOT touch MongoDB - a restart
// won't fix a database outage, so don't let one trigger a restart loop.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness: can this instance actually serve traffic? This one pings MongoDB.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});

// Maps every controller action found by AddControllers above.
#if (!SkipSample)
// The example BooksController lives in Controllers/ - delete it, along with
// Models/Book.cs, Models/BookDtos.cs and Services/BookService.cs, once you've read
// through them and you're ready to write your own.
#endif
app.MapControllers();

app.Run();
