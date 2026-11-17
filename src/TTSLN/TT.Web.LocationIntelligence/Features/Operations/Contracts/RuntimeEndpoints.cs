using TT.Web.LocationIntelligence.Features.Chat;

namespace TT.Web.LocationIntelligence.Features.Operations.Contracts;

/// <summary>Server-advertised API and realtime routes.</summary>
public sealed record RuntimeEndpoints(
    string Runtime = OperationsFeature.RuntimePath,
    string Sessions = ChatEndpoints.SessionsPath,
    string ChatHub = ChatEndpoints.HubPath);
