using System.ComponentModel.DataAnnotations;

namespace TT.Web.LocationIntelligence.Options;

/// <summary>Configures the Cosmos account and container; keys are emulator-only.</summary>
public sealed class CosmosOptions
{
    public const string Section = "Cosmos";
    [Required] public string Endpoint { get; set; } = "";
    [Required] public string Database { get; set; } = "LocationIntelligence";
    [Required] public string Container { get; set; } = "Conversations";
    public bool UseEmulator { get; set; }
    public string EmulatorKey { get; set; } = "";
}
