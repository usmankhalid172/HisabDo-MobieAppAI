using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using HisabDo.Web.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddHttpContextAccessor();

const string DevCorsPolicy = "DevCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

builder.Services.AddSingleton<IWebAiDashboardService, WebAiDashboardDemoService>();
builder.Services.AddScoped<ICurrentUser, DemoCurrentUser>();

builder.Services.AddAuthentication(DemoAuthenticationHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
        DemoAuthenticationHandler.Scheme, _ => { });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseCors(DevCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Simple test endpoint so you can confirm the API is running
app.MapGet("/", () => Results.Ok(new
{
    status = "success",
    message = "HisabDo Web AI API is running"
}));

app.Run();

/// <summary>
/// DEMO-ONLY auth handler — uses the X-Demo-User header when supplied.
/// If no header is supplied, it uses a default demo user.
/// Real authentication will replace this entirely.
/// </summary>
internal sealed class DemoAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "Demo";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Use X-Demo-User if the UI/API client sends it.
        // Otherwise use a default demo user for local browser testing.
        var requestedUser =
            Request.Headers.TryGetValue("X-Demo-User", out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue)
                ? headerValue.ToString()
                : "Rich";

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    requestedUser)
            },
            Scheme);

        var principal = new ClaimsPrincipal(identity);

        var ticket = new AuthenticationTicket(
            principal,
            Scheme);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }
}