using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using HisabDo.AI.Day14;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

// CORS — required so the UI (opened as a local HTML file, or served from
// Live Server on a different port) can call this API from the browser.
// Wide open on purpose since this is a local dev/demo backend; replace
// with an explicit allow-list of real origins before this goes anywhere
// near production.
const string DevCorsPolicy = "DevCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy =>
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());
});

builder.Services.AddScoped<BudgetRecommendationService>();
builder.Services.AddScoped<TahaAIBudgetExplanationService>();
builder.Services.AddSingleton<IBudgetRecommendationRepository, DemoBudgetRecommendationRepository>();

builder.Services.AddAuthentication(DemoAuthenticationHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
        DemoAuthenticationHandler.Scheme, _ => { });

builder.Services.AddAuthorization();

var app = builder.Build();

// Swagger
app.UseSwagger();
app.UseSwaggerUI();

// CORS
app.UseCors(DevCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>
/// DEMO-ONLY auth handler — impersonates whichever GUID is sent in the
/// `X-Demo-User` header, defaulting to the "Normal" demo user.
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
            : DemoUsers.Normal.ToString();

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