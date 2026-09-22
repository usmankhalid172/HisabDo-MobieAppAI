using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;

namespace HisabDo.WebIntegration;

public sealed record FinancialHealthDto(
    int Score, string Status, decimal SavingRate, decimal ExpenseRatio,
    string BudgetStatus, decimal CashFlow, decimal ExpenseGrowth,
    string[] Insights);

public sealed record SpendingPatternDto(
    string Category, decimal CurrentAmount, decimal? PreviousAmount,
    decimal? ChangePercent, string Direction);

public sealed record AnomalyDto(
    string Id, string Type, string Category, decimal Amount,
    string Severity, string Reason);

public sealed record ForecastDto(
    string Period, decimal? ExpectedIncome, decimal? ExpectedExpense,
    decimal? ExpectedSaving, decimal? ExpectedClosingBalance,
    string Confidence);

public sealed record BudgetRecommendationDto(
    string Category, decimal? ExistingBudget, decimal RecommendedBudget,
    decimal HistoricalAverage, string Reason);

public sealed record RecommendationDto(
    string Id, string Type, string Summary, string Reason,
    string Action, string Priority, string? Severity,
    decimal? ExpectedBenefit, bool ExpectedBenefitVerified);

public sealed record AskRequest(string Question);

public sealed record WebAiDashboardDto(
    FinancialHealthDto? FinancialHealth,
    IReadOnlyList<SpendingPatternDto> SpendingPatterns,
    IReadOnlyList<AnomalyDto> Anomalies,
    IReadOnlyList<ForecastDto> Forecasts,
    IReadOnlyList<BudgetRecommendationDto> BudgetRecommendations,
    IReadOnlyList<RecommendationDto> Recommendations);

public interface IHisabDoAiService
{
    Task<WebAiDashboardDto> GetDashboardAsync(
        string userId, CancellationToken ct);
    Task<string> AskAsync(
        string userId, string question, CancellationToken ct);
}

[ApiController]
[Authorize]
[Route("api/web/ai")]
public sealed class WebAiController : ControllerBase
{
    private readonly IHisabDoAiService _service;

    public WebAiController(IHisabDoAiService service)
    {
        _service = service;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<WebAiDashboardDto>> Dashboard(
        CancellationToken ct)
    {
        // IMPORTANT: derive userId from authenticated claims/service,
        // not from a client-supplied query parameter.
        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        return Ok(await _service.GetDashboardAsync(userId, ct));
    }

    [HttpPost("ask")]
    public async Task<ActionResult<object>> Ask(
        [FromBody] AskRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "Question is required." });

        var userId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var answer = await _service.AskAsync(userId, request.Question, ct);

        return Ok(new
        {
            answer,
            source = "verified-hisabdo-data"
        });
    }
}
