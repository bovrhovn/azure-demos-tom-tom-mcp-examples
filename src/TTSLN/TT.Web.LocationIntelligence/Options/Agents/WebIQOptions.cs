using System.ComponentModel.DataAnnotations;

namespace TT.Web.LocationIntelligence.Options.Agents;

public sealed class WebIQOptions
{
    public const string Section = "WebIQ";
    [Url] public string Endpoint { get; set; } = "https://api.microsoft.ai/v3/search/web";
    public string Key { get; set; } = "";
    [Range(1, 10)] public int MaxResults { get; set; } = 4;
    [Range(500, 10000)] public int MaxContentLength { get; set; } = 3000;
}
