using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Options.Chat;

namespace TT.Web.LocationIntelligence.Features.Chat;

/// <summary>Keeps anonymous demo conversations together in one browser; this is not authentication.</summary>
public sealed class BrowserSessionMiddleware(
    RequestDelegate next, IDataProtectionProvider protection, IOptions<BrowserSessionOptions> options,
    ILogger<BrowserSessionMiddleware> logger)
{
    private readonly IDataProtector protector = protection.CreateProtector("LocationIntelligence.BrowserSession");

    public async Task InvokeAsync(HttpContext context)
    {
        string? owner = null;
        if (context.Request.Cookies.TryGetValue(options.Value.CookieName, out var cookie))
        {
            try { owner = protector.Unprotect(cookie); }
            catch (CryptographicException)
            {
                logger.LogWarning("Invalid browser session cookie; starting a new anonymous session");
            }
        }

        if (owner is null)
        {
            owner = Guid.NewGuid().ToString("N");
            context.Response.Cookies.Append(options.Value.CookieName, protector.Protect(owner), new CookieOptions
            {
                HttpOnly = true, Secure = context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                IsEssential = true, Path = "/"
            });
        }

        context.Items[typeof(BrowserSessionMiddleware)] = owner;
        await next(context);
    }

    public static string Owner(HttpContext context) =>
        context.Items[typeof(BrowserSessionMiddleware)] as string
        ?? throw new InvalidOperationException("The browser session middleware must run before chat endpoints.");
}
