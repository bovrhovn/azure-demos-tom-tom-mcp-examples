using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Features.Identity;
using TT.Web.LocationIntelligence.Features.Operations;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOptions<CosmosOptions>().BindConfiguration(CosmosOptions.Section)
    .ValidateDataAnnotations()
    .Validate(options => Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var endpoint)
        && endpoint.Scheme == "https" && (builder.Environment.IsDevelopment()
            ? options.UseEmulator : !options.UseEmulator && !endpoint.IsLoopback),
        "Development requires the emulator; non-development requires a production HTTPS Cosmos endpoint.")
    .ValidateOnStart();
builder.Services.AddOptions<AgentOptions>().BindConfiguration(AgentOptions.Section)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<IdentityOptions>().BindConfiguration(IdentityOptions.Section)
    .Validate(options => builder.Environment.IsDevelopment() || options.TrustEasyAuth,
        "Enable Identity:TrustEasyAuth only behind enforced Container Apps Easy Auth.").ValidateOnStart();
builder.Services.AddOptions<ChatOptions>().BindConfiguration(ChatOptions.Section)
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddSingleton<TokenCredential>(_ => builder.Environment.IsDevelopment()
    ? new AzureCliCredential()
    : new DefaultAzureCredential());
builder.Services.AddSingleton<DevelopmentIdentity>();
builder.Services.AddAuthentication("Platform")
    .AddScheme<AuthenticationSchemeOptions, PlatformAuthenticationHandler>("Platform", _ => { });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddSingleton(CosmosChatStore.CreateClient);
builder.Services.AddSingleton<IChatStore, CosmosChatStore>();
builder.Services.AddSingleton<IChatResponder, FoundryChatResponder>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddHealthChecks().AddCheck<CosmosHealthCheck>("cosmos", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("chat", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = context.RequestServices.GetRequiredService<IOptions<ChatOptions>>().Value.MutationsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});
var insights = builder.Configuration["ApplicationInsights:ConnectionString"];
if (!string.IsNullOrWhiteSpace(insights))
{
    builder.Services.AddApplicationInsightsTelemetry(options => options.ConnectionString = insights);
    builder.Logging.AddFilter<Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider>(
        "", LogLevel.Information);
}

var app = builder.Build();
app.UseExceptionHandler();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .AllowAnonymous();
app.MapChatFeature();
app.MapOperationsFeature();
app.MapHub<ChatHub>("/hubs/chat").RequireAuthorization();
app.MapRazorPages();
app.Run();

/// <summary>Exposes the application entry point to integration tests.</summary>
public partial class Program;
