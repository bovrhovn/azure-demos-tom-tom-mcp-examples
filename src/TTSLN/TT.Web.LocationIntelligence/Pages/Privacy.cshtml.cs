using Microsoft.AspNetCore.Mvc.RazorPages;

namespace TT.Web.LocationIntelligence.Pages;

public class PrivacyModel(ILogger<PrivacyModel> logger) : PageModel
{
    public void OnGet() => logger.LogInformation("Privacy page requested at {Time}", DateTimeOffset.UtcNow);
}