using System.ComponentModel.DataAnnotations;

namespace TT.Web.LocationIntelligence.Options.Chat;

/// <summary>Configures bounded conversation sizes.</summary>
public sealed class ChatOptions
{
    public const string Section = "Chat";
    [Range(1, 8000)] public int MaxMessageLength { get; set; } = 8000;
    [Range(1, 200)] public int MaxTitleLength { get; set; } = 120;
    [Range(1, 40)] public int MaxTurns { get; set; } = 40;
    [Range(30, 600)] public int ReplyTimeoutSeconds { get; set; } = 180;
    [Range(1, 100)] public int PageSize { get; set; } = 30;
}
