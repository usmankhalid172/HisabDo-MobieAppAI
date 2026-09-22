using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using HisabDo.AI.Day15;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

const string DevCorsPolicy = "DevCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

builder.Services.AddSingleton<IFinancialQueryClassifier, AskHisabDoQueryClassifier>();
builder.Services.AddSingleton<IAskHisabDoIntentClassifier, AskHisabDoIntentClassifierAdapter>();
builder.Services.AddSingleton<AskHisabDoDataService>();
builder.Services.AddSingleton<IAskHisabDoDataService>(
    sp => sp.GetRequiredService<AskHisabDoDataService>());
builder.Services.AddScoped<AskHisabDoService>();

builder.Services.AddAuthentication(DemoAuthenticationHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
        DemoAuthenticationHandler.Scheme, _ => { });

builder.Services.AddAuthorization();

var app = builder.Build();

// CORS
app.UseCors(DevCorsPolicy);

// Authentication
app.UseAuthentication();

// Authorization
app.UseAuthorization();

// Root endpoint
app.MapGet("/", () => Results.Ok(new
{
    status = "success",
    message = "HisabDo Day 15 API is running",
    api = "Ask HisabDo"
}));

// Controller endpoints
app.MapControllers();

app.Run();

/// <summary>
/// DEMO-ONLY auth handler — impersonates whichever GUID is sent in the
/// `X-Demo-User` header, defaulting to the primary demo user.
/// Real auth will replace this entirely.
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
        var requestedUser = Request.Headers.TryGetValue("X-Demo-User", out var headerValue)
            ? headerValue.ToString()
            : AskDemoUsers.Primary.ToString();

        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(
                    ClaimTypes.NameIdentifier,
                    requestedUser)
            },
            Scheme);

        return Task.FromResult(
            AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(identity),
                    Scheme)));
    }
}