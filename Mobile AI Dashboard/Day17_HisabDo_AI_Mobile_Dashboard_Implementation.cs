using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.Mobile.AI;

// .NET 8 / ASP.NET Core reference API + mobile-facing state models.
// The mobile client should use the API response as the source of truth.
// It must not calculate financial scores, forecasts, recommendations,
// priority, or severity locally.

public sealed record FinancialHealthDto(
    bool IsVerified,
    decimal Score,
    string Classification,
    IReadOnlyDictionary<string, decimal> Factors,
    string? Explanation);

public sealed record SpendingPatternDto(
    bool IsVerified,
    string Category,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal PercentageChange,
    string Trend,
    string? Explanation);

public sealed record AnomalyDto(
    bool IsVerified,
    string Type,
    decimal? Amount,
    string Severity,
    string Reason,
    string? Explanation);

public sealed record CashFlowForecastDto(
    bool IsVerified,
    decimal ForecastIncome,
    decimal ForecastExpense,
    decimal ExpectedSaving,
    decimal? ExpectedClosingBalance,
    string Confidence,
    string? Explanation);

public sealed record BudgetRecommendationDto(
    bool IsVerified,
    string Category,
    decimal HistoricalAverage,
    decimal RecommendedBudget,
    decimal? ExistingBudget,
    decimal? BudgetVariance,
    string Reason,
    string DataSufficiency,
    string? Explanation);

public sealed record RecommendationDto(
    string RecommendationId,
    bool IsVerified,
    string Type,
    string Priority,
    string? Severity,
    string Reason,
    string Action,
    decimal? VerifiedAmount,
    decimal? VerifiedPercentage,
    string? VerifiedExpectedBenefit,
    string? Limitation);

public sealed record DashboardResponse(
    FinancialHealthDto? FinancialHealth,
    IReadOnlyList<SpendingPatternDto> SpendingPatterns,
    IReadOnlyList<AnomalyDto> Anomalies,
    CashFlowForecastDto? CashFlowForecast,
    IReadOnlyList<BudgetRecommendationDto> BudgetRecommendations,
    IReadOnlyList<RecommendationDto> Recommendations);

public sealed record AskHisabDoRequest(string Question);

public sealed record AskHisabDoResponse(
    bool IsVerified,
    string Answer,
    string? Intent,
    string? Limitation);

public sealed record ApiError(
    string Code,
    string Message,
    bool Retryable);

public enum MobileLoadState
{
    Idle,
    Loading,
    Success,
    Empty,
    Error
}

public sealed record MobileScreenState<T>(
    MobileLoadState State,
    T? Data,
    string? Message,
    bool CanRetry)
{
    public static MobileScreenState<T> Loading() =>
        new(MobileLoadState.Loading, default, null, false);

    public static MobileScreenState<T> Success(T data) =>
        new(MobileLoadState.Success, data, null, false);

    public static MobileScreenState<T> Empty(string message) =>
        new(MobileLoadState.Empty, default, message, true);

    public static MobileScreenState<T> Error(string message, bool canRetry = true) =>
        new(MobileLoadState.Error, default, message, canRetry);
}

public interface ICurrentUser
{
    string UserId { get; }
}

public interface IMobileAiDashboardService
{
    Task<DashboardResponse> GetDashboardAsync(
        string authenticatedUserId,
        CancellationToken cancellationToken);

    Task<AskHisabDoResponse> AskAsync(
        string authenticatedUserId,
        string question,
        CancellationToken cancellationToken);
}

[ApiController]
[Authorize]
[Route("api/mobile/ai")]
public sealed class AiDashboardController : ControllerBase
{
    private readonly IMobileAiDashboardService _service;
    private readonly ICurrentUser _currentUser;

    public AiDashboardController(
        IMobileAiDashboardService service,
        ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardResponse>> GetDashboard(
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new ApiError(
                "AUTH_REQUIRED",
                "Authenticated user context is required.",
                false));

        try
        {
            var dashboard = await _service.GetDashboardAsync(
                userId, cancellationToken);

            return Ok(dashboard);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ApiError(
                "REQUEST_CANCELLED",
                "The request was cancelled.",
                true));
        }
        catch (Exception)
        {
            return StatusCode(500, new ApiError(
                "DASHBOARD_ERROR",
                "Unable to load AI dashboard.",
                true));
        }
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskHisabDoResponse>> Ask(
        [FromBody] AskHisabDoRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized(new ApiError(
                "AUTH_REQUIRED",
                "Authenticated user context is required.",
                false));

        if (request is null || string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new ApiError(
                "QUESTION_REQUIRED",
                "Please enter a financial question.",
                false));

        try
        {
            var answer = await _service.AskAsync(
                userId,
                request.Question.Trim(),
                cancellationToken);

            return Ok(answer);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ApiError(
                "REQUEST_CANCELLED",
                "The request was cancelled.",
                true));
        }
        catch (Exception)
        {
            return StatusCode(500, new ApiError(
                "ASK_ERROR",
                "Unable to process the question.",
                true));
        }
    }
}

// Example mobile ViewModel/state coordinator.
// This is framework-neutral C# and can be adapted to .NET MAUI,
// Xamarin migration code, React Native bridge code, or another client.
public sealed class AiDashboardViewModel
{
    private readonly Func<CancellationToken, Task<DashboardResponse>> _loadApi;
    private readonly Func<string, CancellationToken, Task<AskHisabDoResponse>> _askApi;

    public MobileScreenState<DashboardResponse> Dashboard { get; private set; }
        = new(MobileLoadState.Idle, null, null, false);

    public MobileScreenState<AskHisabDoResponse> AskState { get; private set; }
        = new(MobileLoadState.Idle, null, null, false);

    public AiDashboardViewModel(
        Func<CancellationToken, Task<DashboardResponse>> loadApi,
        Func<string, CancellationToken, Task<AskHisabDoResponse>> askApi)
    {
        _loadApi = loadApi;
        _askApi = askApi;
    }

    public async Task LoadDashboardAsync(CancellationToken cancellationToken = default)
    {
        Dashboard = MobileScreenState<DashboardResponse>.Loading();

        try
        {
            var data = await _loadApi(cancellationToken);

            if (IsDashboardEmpty(data))
            {
                Dashboard = MobileScreenState<DashboardResponse>.Empty(
                    "No AI financial insights are available yet.");
                return;
            }

            Dashboard = MobileScreenState<DashboardResponse>.Success(data);
        }
        catch (OperationCanceledException)
        {
            Dashboard = MobileScreenState<DashboardResponse>.Error(
                "Loading was cancelled.", true);
        }
        catch (Exception)
        {
            Dashboard = MobileScreenState<DashboardResponse>.Error(
                "Unable to load AI dashboard. Please try again.", true);
        }
    }

    public Task RetryDashboardAsync(CancellationToken cancellationToken = default) =>
        LoadDashboardAsync(cancellationToken);

    public async Task AskHisabDoAsync(
        string question,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            AskState = MobileScreenState<AskHisabDoResponse>.Error(
                "Please enter a question.", false);
            return;
        }

        AskState = MobileScreenState<AskHisabDoResponse>.Loading();

        try
        {
            var answer = await _askApi(
                question.Trim(), cancellationToken);

            if (answer is null ||
                string.IsNullOrWhiteSpace(answer.Answer))
            {
                AskState = MobileScreenState<AskHisabDoResponse>.Empty(
                    "No answer is available for this question.");
                return;
            }

            AskState = MobileScreenState<AskHisabDoResponse>.Success(answer);
        }
        catch (Exception)
        {
            AskState = MobileScreenState<AskHisabDoResponse>.Error(
                "Unable to get an answer. Please try again.", true);
        }
    }

    private static bool IsDashboardEmpty(DashboardResponse data)
    {
        return data is null
            || (data.FinancialHealth is null
                && data.SpendingPatterns.Count == 0
                && data.Anomalies.Count == 0
                && data.CashFlowForecast is null
                && data.BudgetRecommendations.Count == 0
                && data.Recommendations.Count == 0);
    }
}

// UI rendering contract.
// Every screen should map to one of these states:
// Loading → Success / Empty / Error.
public static class MobileUiContract
{
    public static string RenderDashboardState(
        MobileScreenState<DashboardResponse> state) =>
        state.State switch
        {
            MobileLoadState.Loading => "Show skeleton/loading indicators.",
            MobileLoadState.Success => "Render all available AI cards.",
            MobileLoadState.Empty => "Show empty-state guidance.",
            MobileLoadState.Error => "Show error and Retry action.",
            _ => "Idle."
        };
}

// UI validation helpers.
public static class MobileUiValidator
{
    public static void ValidateDashboard(DashboardResponse data)
    {
        if (data is null)
            throw new InvalidOperationException("Dashboard response is null.");

        if (data.FinancialHealth is { } health)
        {
            if (health.IsVerified && (health.Score < 0 || health.Score > 100))
                throw new InvalidOperationException(
                    "Financial Health score must be between 0 and 100.");
        }

        foreach (var recommendation in data.Recommendations)
        {
            if (recommendation.IsVerified)
            {
                if (string.IsNullOrWhiteSpace(recommendation.Type))
                    throw new InvalidOperationException(
                        "Verified recommendation type is required.");

                if (string.IsNullOrWhiteSpace(recommendation.Priority))
                    throw new InvalidOperationException(
                        "Verified recommendation priority is required.");
            }
        }
    }
}

/*
Recommended mobile screen structure:

AI Dashboard
 ├── Financial Health Card
 ├── Spending Patterns Card
 ├── Anomalies Card
 ├── Cash-Flow Forecast Card
 ├── Budget Recommendations Card
 ├── AI Recommendations Card
 └── Ask HisabDo AI Entry

Recommendation Detail
 ├── Title
 ├── Why
 ├── Financial Pattern
 ├── Priority
 ├── Severity (if available)
 ├── Action
 ├── Expected Benefit (only if verified)
 └── Limitation (if required)

Every API screen:
 Loading → Success / Empty / Error → Retry
*/
