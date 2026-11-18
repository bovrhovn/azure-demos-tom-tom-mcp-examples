using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Options.Agents;

namespace TT.Web.LocationIntelligence.Features.Travel;

public sealed class TravelRunLimiter(IOptions<TravelOptions> options) : IDisposable
{
    internal SemaphoreSlim Slots { get; } = new(options.Value.MaxConcurrentRuns);
    public void Dispose() => Slots.Dispose();
}
