using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace WebApiProjectWithMongoDB.Services;

/// <summary>
/// Reports database reachability at /health.
///
/// Hand-rolled rather than pulling in a third-party health check package, so you
/// can see exactly what's happening: run the cheapest possible command ({ ping: 1 })
/// and report how long it took.
///
/// Wire this into your orchestrator's readiness probe so a container that can't
/// reach MongoDB is taken out of the load balancer rather than serving errors.
/// </summary>
public sealed class MongoDbHealthCheck(IMongoDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Keep the probe tighter than the driver's normal timeout. A health check
        // that hangs for 30 seconds is worse than one that fails quickly.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        var startedAt = TimeProvider.System.GetTimestamp();

        try
        {
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: timeout.Token);

            var elapsed = TimeProvider.System.GetElapsedTime(startedAt);

            return HealthCheckResult.Healthy(
                "MongoDB responded to ping.",
                new Dictionary<string, object> { ["roundTripMs"] = elapsed.TotalMilliseconds });
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("MongoDB did not respond to ping.", exception);
        }
    }
}
