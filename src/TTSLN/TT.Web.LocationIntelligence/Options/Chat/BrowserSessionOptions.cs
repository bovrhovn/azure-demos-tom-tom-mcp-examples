using System.ComponentModel.DataAnnotations;

namespace TT.Web.LocationIntelligence.Options.Chat;

public sealed class BrowserSessionOptions
{
    public const string Section = "BrowserSession";
    [Required] public string CookieName { get; set; } = "tt-browser-session";
}
