// HisabDo AI - Day 17 Mobile Integration
// .NET 8 / ASP.NET Core reference API contracts and integration layer.
// Source of truth: HisabDo backend. Mobile renders verified DTOs only.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace HisabDo.AI.MobileIntegration;

public sealed record FinancialHealthDto(
    double Score, string Classification, double SavingRate,
    double ExpenseRatio, double BudgetUtilization,
    double CashFlow, double ExpenseGrowthPercent);

public sealed record SpendingPatternDto(
    string Category, double CurrentAmount, double PreviousAmount,
    double ChangePercent, string Trend, bool IsUnusual, bool IsRecurring);

public sealed record AnomalyDto(
    string Type, string Category, string Severity, double Amount, string Explanation);

public sealed record CashFlowForecastDto(
    double ExpectedIncome, double ExpectedExpense, double ExpectedSaving,
    double? ExpectedClosingBalance, string Confidence, string Limitation);

public sealed record BudgetRecommendationDto(
    string Category, double HistoricalAverage, double? ExistingBudget,
    double RecommendedBudget, double? BudgetVariance, string Reason,
    string DataSufficiency);

public sealed record RecommendationDto(
    string RecommendationId, string Category, string Title, string Reason,
    string FinancialPattern, string Priority, string Severity,
    string SuggestedAction, string? ExpectedBenefit,
    bool ExpectedBenefitVerified, string? Limitation);

public sealed record AskHisabDoRequest([property: Required] string Question);

public sealed record AskHisabDoResponse(
    string Answer, string Intent, bool IsVerified, string? Limitation);

public sealed record DashboardResponse(
    FinancialHealthDto? FinancialHealth,
    IReadOnlyList<RecommendationDto> Recommendations,
    IReadOnlyList<SpendingPatternDto> SpendingPatterns,
    IReadOnlyList<AnomalyDto> Anomalies,
    CashFlowForecastDto? CashFlowForecast,
    IReadOnlyList<BudgetRecommendationDto> BudgetRecommendations);

public sealed record ApiError(string Code, string Message);

public interface IFinancialHealthService
{
    Task<FinancialHealthDto?> GetAsync(string userId, CancellationToken ct);
}
public interface ISpendingPatternService
{
    Task<IReadOnlyList<SpendingPatternDto>> GetAsync(string userId, CancellationToken ct);
}
public interface IAnomalyService
{
    Task<IReadOnlyList<AnomalyDto>> GetAsync(string userId, CancellationToken ct);
}
public interface ICashFlowForecastService
{
    Task<CashFlowForecastDto?> GetAsync(string userId, CancellationToken ct);
}
public interface IBudgetRecommendationService
{
    Task<IReadOnlyList<BudgetRecommendationDto>> GetAsync(string userId, CancellationToken ct);
}
public interface IRecommendationService
{
    Task<IReadOnlyList<RecommendationDto>> GetAsync(string userId, CancellationToken ct);
}
public interface IAskHisabDoService
{
    Task<AskHisabDoResponse> AskAsync(
        string userId, string question, CancellationToken ct);
}
public interface IMobileDashboardService
{
    Task<DashboardResponse> GetDashboardAsync(
        string userId, CancellationToken ct);
}

public sealed class MobileDashboardService : IMobileDashboardService
{
    private readonly IFinancialHealthService _health;
    private readonly IRecommendationService _recommendations;
    private readonly ISpendingPatternService _patterns;
    private readonly IAnomalyService _anomalies;
    private readonly ICashFlowForecastService _forecast;
    private readonly IBudgetRecommendationService _budgets;

    public MobileDashboardService(
        IFinancialHealthService health,
        IRecommendationService recommendations,
        ISpendingPatternService patterns,
        IAnomalyService anomalies,
        ICashFlowForecastService forecast,
        IBudgetRecommendationService budgets)
    {
        _health = health;
        _recommendations = recommendations;
        _patterns = patterns;
        _anomalies = anomalies;
        _forecast = forecast;
        _budgets = budgets;
    }

    public async Task<DashboardResponse> GetDashboardAsync(
        string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Authenticated user is required.");

        var healthTask = _health.GetAsync(userId, ct);
        var recommendationTask = _recommendations.GetAsync(userId, ct);
        var patternTask = _patterns.GetAsync(userId, ct);
        var anomalyTask = _anomalies.GetAsync(userId, ct);
        var forecastTask = _forecast.GetAsync(userId, ct);
        var budgetTask = _budgets.GetAsync(userId, ct);

        await Task.WhenAll(
            healthTask, recommendationTask, patternTask,
            anomalyTask, forecastTask, budgetTask);

        return new DashboardResponse(
            await healthTask,
            await recommendationTask,
            await patternTask,
            await anomalyTask,
            await forecastTask,
            await budgetTask);
    }
}

[ApiController]
[Authorize]
[Route("api/mobile/ai")]
public sealed class MobileAiController : ControllerBase
{
    private readonly IMobileDashboardService _dashboard;
    private readonly IAskHisabDoService _ask;

    public MobileAiController(
        IMobileDashboardService dashboard,
        IAskHisabDoService ask)
    {
        _dashboard = dashboard;
        _ask = ask;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardResponse>> GetDashboard(
        CancellationToken ct)
    {
        // IMPORTANT: userId must come from the authenticated identity,
        // never from a mobile query/body parameter.
        var userId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new ApiError(
                "AUTHENTICATION_REQUIRED",
                "Authenticated user context is required."));

        return Ok(await _dashboard.GetDashboardAsync(userId, ct));
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskHisabDoResponse>> Ask(
        [FromBody] AskHisabDoRequest request,
        CancellationToken ct)
    {
        var userId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        if (request == null || string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new ApiError(
                "EMPTY_QUESTION", "Please enter a financial question."));

        if (request.Question.Length > 1000)
            return BadRequest(new ApiError(
                "QUESTION_TOO_LONG",
                "Question must be 1000 characters or less."));

        return Ok(await _ask.AskAsync(
            userId, request.Question.Trim(), ct));
    }
}

public enum MobileLoadState
{
    Loading, Loaded, Empty, Error
}

public sealed record MobileScreenState<T>(
    MobileLoadState State, T? Data,
    string? UserMessage, string? ErrorCode);

/*
Mobile API contract:

GET  /api/mobile/ai/dashboard
POST /api/mobile/ai/ask

Ask request:
{
  "question": "Why is my saving rate low?"
}

Ask response:
{
  "answer": "...",
  "intent": "FinancialHealth",
  "isVerified": true,
  "limitation": null
}

Recommended mobile navigation:
AI Dashboard
  - Financial Health
  - Recommendations
  - Spending Patterns
  - Anomalies
  - Cash-Flow Forecast
  - Budget Recommendations

Ask HisabDo AI
  - Question input
  - Loading
  - Verified AI answer
  - Limitation/source status

Mobile rules:
- Never calculate financial values in the app.
- Never trust a user-supplied userId.
- Show ExpectedBenefit only when ExpectedBenefitVerified=true.
- Show verified AI status only when IsVerified=true.
- Empty arrays are valid empty states, not errors.
*/
