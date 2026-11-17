using Microsoft.Extensions.Diagnostics.HealthChecks;
using TT.Web.LocationIntelligence.Features.Chat.Persistence;

namespace TT.Web.LocationIntelligence.Features.Operations.Health;

/// <summary>Separates process liveness from Cosmos readiness.</summary>
public sealed class CosmosHealthCheck(IChatStore store) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await store.CheckHealthAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return HealthCheckResult.Unhealthy("Cosmos DB is unavailable.", ex);
        }
    }
}
