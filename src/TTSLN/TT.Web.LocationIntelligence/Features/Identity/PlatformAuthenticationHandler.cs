using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TT.Web.LocationIntelligence.Features.Identity;

/// <summary>Explicitly opts into the trusted Container Apps authentication boundary.</summary>
public sealed class IdentityOptions
{
    public const string Section = "Identity";
    public bool TrustEasyAuth { get; set; }
    public string DevelopmentScope { get; set; } = "https://management.azure.com/.default";
}

/// <summary>Gets the local operator's identity exclusively from an Azure CLI-issued token.</summary>
public sealed class DevelopmentIdentity(TokenCredential credential, IOptions<IdentityOptions> options)
{
    private readonly SemaphoreSlim identityLock = new(1, 1);
    private string? cachedOwner;
    private DateTimeOffset expiresAt;

    public async Task<string> GetOwnerAsync(CancellationToken cancellationToken)
    {
        await identityLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedOwner is not null && expiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return cachedOwner;
            var token = await credential.GetTokenAsync(new TokenRequestContext([options.Value.DevelopmentScope]),
                cancellationToken);
            var payload = token.Token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            var tenant = json.RootElement.GetProperty("tid").GetString();
            var subject = json.RootElement.GetProperty("oid").GetString();
            ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
            ArgumentException.ThrowIfNullOrWhiteSpace(subject);
            cachedOwner = HashOwner(tenant, subject);
            expiresAt = token.ExpiresOn;
            return cachedOwner;
        }
        finally { identityLock.Release(); }
    }

    internal static string HashOwner(string tenant, string subject) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant}:{subject}")));
}

/// <summary>Uses CLI identity locally and a platform-validated Entra principal in production.</summary>
public sealed class PlatformAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory loggerFactory, UrlEncoder encoder,
    IHostEnvironment environment, IOptions<IdentityOptions> identityOptions, DevelopmentIdentity developmentIdentity)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null)
            return AuthenticateResult.NoResult();
        string owner;
        if (environment.IsDevelopment())
        {
            try { owner = await developmentIdentity.GetOwnerAsync(Context.RequestAborted); }
            catch (Exception ex) when (ex is CredentialUnavailableException or AuthenticationFailedException)
            {
                Logger.LogWarning(ex, "Local Azure CLI authentication failed; run az login");
                return AuthenticateResult.Fail("Run az login before using the development application.");
            }
        }
        else
        {
            if (!identityOptions.Value.TrustEasyAuth) return AuthenticateResult.Fail("Easy Auth is not configured.");
            var header = Request.Headers["X-MS-CLIENT-PRINCIPAL"].ToString();
            if (string.IsNullOrEmpty(header)) return AuthenticateResult.NoResult();
            try
            {
                using var json = JsonDocument.Parse(Convert.FromBase64String(header));
                var root = json.RootElement;
                if (root.GetProperty("auth_typ").GetString() != "aad")
                    return AuthenticateResult.Fail("An Entra identity is required.");
                var claims = root.GetProperty("claims").EnumerateArray().ToArray();
                string? Find(params string[] types) => claims
                    .FirstOrDefault(claim => types.Contains(claim.GetProperty("typ").GetString()))
                    is var claim && claim.ValueKind != JsonValueKind.Undefined ? claim.GetProperty("val").GetString() : null;
                var subject = Find("http://schemas.microsoft.com/identity/claims/objectidentifier", "oid");
                var tenant = Find("http://schemas.microsoft.com/identity/claims/tenantid", "tid");
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(tenant))
                    return AuthenticateResult.Fail("The platform identity is missing tenant or object claims.");
                owner = DevelopmentIdentity.HashOwner(tenant, subject);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                Logger.LogWarning("Rejected malformed platform identity");
                return AuthenticateResult.Fail("Invalid platform identity.");
            }
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, owner)], Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
