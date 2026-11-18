using System.ComponentModel.DataAnnotations;

namespace TT.Web.LocationIntelligence.Options.Agents;

public sealed class TravelOptions
{
    public const string Section = "Travel";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    [Range(30, 1200)] public int TimeoutSeconds { get; set; } = 600;
    [Range(15, 300)] public int TicketLifetimeSeconds { get; set; } = 60;
    [Range(1, 16)] public int MaxConcurrentRuns { get; set; } = 2;
    [Range(3, 30)] public int MaxRounds { get; set; } = 12;
    [Range(1, 5)] public int MaxStalls { get; set; } = 2;
    [Range(0, 5)] public int MaxResets { get; set; } = 1;
    [Range(1, 10)] public int ForecastDays { get; set; } = 7;
    [Range(10, 100)] public int MaxToolCalls { get; set; } = 40;
    [Range(1000, 100000)] public int MaxResultCharacters { get; set; } = 32000;
}
