namespace TT.Web.LocationIntelligence.Features.Operations.Contracts;

/// <summary>Safe runtime bootstrap configuration exposed to the browser workspace.</summary>
public sealed record RuntimeResponse(
    string Environment,
    string Application,
    string Database,
    string Container,
    string DatabaseHost,
    bool Emulator,
    string Model,
    bool AgentConfigured,
    int MaxMessageLength,
    int MaxTitleLength,
    int MaxTurns,
    string AntiforgeryHeaderName,
    string CsrfToken,
    RuntimeEndpoints Endpoints);
