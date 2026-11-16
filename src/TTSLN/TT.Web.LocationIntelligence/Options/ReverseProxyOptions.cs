using Microsoft.AspNetCore.HttpOverrides;

namespace TT.Web.LocationIntelligence.Options;

public sealed class ReverseProxyOptions
{
    public const string Section = "ReverseProxy";
    public ForwardedHeaders ForwardedHeaders { get; set; } =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    public bool TrustAllProxies { get; set; }
}
