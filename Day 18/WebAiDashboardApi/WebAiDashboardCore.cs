using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.Web.AI;

// .NET 8 / ASP.NET Core web-facing API + state models.
// Adapted directly from the teammate's real Day 17 mobile reference
// (Mobile AI Dashboard/Day17_HisabDo_AI_Mobile_Dashboard_Implementation.cs)
// — same DTOs, same state machine, same controller shape — renamed
// Mobile -> Web and re-routed /api/mobile/ai -> /api/web/ai, per the
// task's own request to follow that established concept for the web
// dashboard. The web client must use the API response as the source of
// truth. It must not calculate financial scores, forecasts,
// recommendations, priority, or severity locally.

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

public enum WebLoadState
{
    Idle,
    Loading,
    Success,
    Empty,
    Error
}

public sealed record WebScreenState<T>(
    WebLoadState State,
    T? Data,
    string? Message,
    bool CanRetry)
{
    public static WebScreenState<T> Loading() =>
        new(WebLoadState.Loading, default, null, false);

    public static WebScreenState<T> Success(T data) =>
        new(WebLoadState.Success, data, null, false);

    public static WebScreenState<T> Empty(string message) =>
        new(WebLoadState.Empty, default, message, true);

    public static WebScreenState<T> Error(string message, bool canRetry = true) =>
        new(WebLoadState.Error, default, message, canRetry);
}

public interface ICurrentUser
{
    string UserId { get; }
}

public interface IWebAiDashboardService
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
[Route("api/web/ai")]
public sealed class AiDashboardController : ControllerBase
{
    private readonly IWebAiDashboardService _service;
    private readonly ICurrentUser _currentUser;

    public AiDashboardController(
        IWebAiDashboardService service,
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

// Framework-neutral ViewModel/state coordinator — usable from the plain
// JS client the same way the mobile reference intended it for MAUI/RN.
public sealed class AiDashboardViewModel
{
    private readonly Func<CancellationToken, Task<DashboardResponse>> _loadApi;
    private readonly Func<string, CancellationToken, Task<AskHisabDoResponse>> _askApi;

    public WebScreenState<DashboardResponse> Dashboard { get; private set; }
        = new(WebLoadState.Idle, null, null, false);

    public WebScreenState<AskHisabDoResponse> AskState { get; private set; }
        = new(WebLoadState.Idle, null, null, false);

    public AiDashboardViewModel(
        Func<CancellationToken, Task<DashboardResponse>> loadApi,
        Func<string, CancellationToken, Task<AskHisabDoResponse>> askApi)
    {
        _loadApi = loadApi;
        _askApi = askApi;
    }

    public async Task LoadDashboardAsync(CancellationToken cancellationToken = default)
    {
        Dashboard = WebScreenState<DashboardResponse>.Loading();

        try
        {
            var data = await _loadApi(cancellationToken);

            if (IsDashboardEmpty(data))
            {
                Dashboard = WebScreenState<DashboardResponse>.Empty(
                    "No AI financial insights are available yet.");
                return;
            }

            Dashboard = WebScreenState<DashboardResponse>.Success(data);
        }
        catch (OperationCanceledException)
        {
            Dashboard = WebScreenState<DashboardResponse>.Error(
                "Loading was cancelled.", true);
        }
        catch (Exception)
        {
            Dashboard = WebScreenState<DashboardResponse>.Error(
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
            AskState = WebScreenState<AskHisabDoResponse>.Error(
                "Please enter a question.", false);
            return;
        }

        AskState = WebScreenState<AskHisabDoResponse>.Loading();

        try
        {
            var answer = await _askApi(
                question.Trim(), cancellationToken);

            if (answer is null ||
                string.IsNullOrWhiteSpace(answer.Answer))
            {
                AskState = WebScreenState<AskHisabDoResponse>.Empty(
                    "No answer is available for this question.");
                return;
            }

            AskState = WebScreenState<AskHisabDoResponse>.Success(answer);
        }
        catch (Exception)
        {
            AskState = WebScreenState<AskHisabDoResponse>.Error(
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

public static class WebUiContract
{
    public static string RenderDashboardState(
        WebScreenState<DashboardResponse> state) =>
        state.State switch
        {
            WebLoadState.Loading => "Show skeleton/loading indicators.",
            WebLoadState.Success => "Render all available AI cards.",
            WebLoadState.Empty => "Show empty-state guidance.",
            WebLoadState.Error => "Show error and Retry action.",
            _ => "Idle."
        };
}

public static class WebUiValidator
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
