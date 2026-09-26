using System.Security.Claims; 
using System.Text.Json.Serialization; 
using Microsoft.AspNetCore.Authentication; 
using Microsoft.Extensions.Options; 
using HisabDo.AI.Day16; 

var builder = WebApplication.CreateBuilder(args); 

builder.Services.AddControllers() 
    .AddJsonOptions(options => 
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter())); 

// Add Swagger/OpenAPI services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

const string DevCorsPolicy = "DevCors"; 
builder.Services.AddCors(options => 
{ 
    options.AddPolicy(DevCorsPolicy, policy => 
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()); 
}); 

builder.Services.AddSingleton<TahaRecommendationEngine>(); 
builder.Services.AddSingleton<RecommendationDemoDataAggregator>(); 

builder.Services.AddAuthentication(DemoAuthenticationHandler.Scheme) 
    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>( 
        DemoAuthenticationHandler.Scheme, _ => { }); 
builder.Services.AddAuthorization(); 

var app = builder.Build(); 

// Enable Swagger UI across all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
    c.RoutePrefix = "swagger"; // Serves Swagger UI at http://localhost:5000/swagger
});

app.UseCors(DevCorsPolicy); 
app.UseAuthentication(); 
app.UseAuthorization(); 

// Direct Root Endpoint — prevents 404 on http://localhost:5000/
app.MapGet("/", () => Results.Ok(new 
{ 
    Status = "Online", 
    Message = "Recommendation API is running successfully.",
    SwaggerUI = "/swagger"
}));

app.MapControllers(); 

app.Run(); 

/// <summary> 
/// DEMO-ONLY auth handler — impersonates whichever value is sent in the 
/// `X-Demo-User` header, defaulting to the "AtRisk" demo user. Real auth 
/// will replace this entirely. 
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
            : RecDemoUsers.AtRisk; 

        var identity = new ClaimsIdentity( 
            new[] { new Claim(ClaimTypes.NameIdentifier, requestedUser) }, Scheme); 
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket( 
            new ClaimsPrincipal(identity), Scheme))); 
    } 
}