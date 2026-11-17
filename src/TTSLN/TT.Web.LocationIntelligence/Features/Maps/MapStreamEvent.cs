namespace TT.Web.LocationIntelligence.Features.Maps;

public sealed record MapStreamEvent(string Type, object? Data);

public interface IMapsResponder
{
    Task RunAsync(string prompt, Func<MapStreamEvent, CancellationToken, Task> publish, CancellationToken ct);
    Task<object> CallAppToolAsync(string name, Dictionary<string, object?> arguments, CancellationToken ct);
}
