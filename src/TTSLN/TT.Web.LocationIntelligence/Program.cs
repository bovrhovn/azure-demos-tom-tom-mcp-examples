using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using TT.Web.LocationIntelligence.Configuration;
using TT.Web.LocationIntelligence.Features.Chat;
using TT.Web.LocationIntelligence.Features.Operations;

var builder = WebApplication.CreateBuilder(args);
builder.AddLocationIntelligence();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStaticFiles();
app.UseRouting();
app.UseMiddleware<BrowserSessionMiddleware>();
app.UseAntiforgery();

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapChatFeature();
app.MapOperationsFeature();
app.MapHub<ChatHub>("/hubs/chat");
app.MapRazorPages();
app.Run();

/// <summary>Exposes the application entry point to integration tests.</summary>
public partial class Program;
