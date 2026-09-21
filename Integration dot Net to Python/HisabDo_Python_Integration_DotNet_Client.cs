using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HisabDo.AI.Integration;

/// <summary>
/// .NET -> Python FastAPI -> Gemini integration.
/// IMPORTANT:
/// .NET remains the source of truth for financial calculations.
/// Python/Gemini only explains verified values.
/// </summary>
public sealed record VerifiedFinancialContext(
    string UserId,
    decimal? HealthScore,
    decimal? SavingRate,
    decimal? ExpenseGrowthPercent,
    decimal? CashFlow,
    string? CashFlowRisk,
    IReadOnlyList<VerifiedRecommendation> Recommendations,
    IReadOnlyList<VerifiedSpendingPattern> SpendingPatterns,
    IReadOnlyList<VerifiedAnomaly> Anomalies,
    IReadOnlyList<VerifiedBudgetRecommendation> BudgetRecommendations,
    IReadOnlyList<VerifiedForecast> Forecasts,
    string? Limitations
);

public sealed record VerifiedRecommendation(
    string Id,
    string Type,
    string Summary,
    string Reason,
    string Action,
    string Priority,
    string? Severity,
    decimal? ExpectedBenefit,
    bool ExpectedBenefitVerified,
    bool IsVerified
);

public sealed record VerifiedSpendingPattern(
    string Category,
    decimal CurrentAmount,
    decimal? PreviousAmount,
    decimal? ChangePercent,
    string Direction,
    bool IsVerified
);

public sealed record VerifiedAnomaly(
    string Id,
    string Type,
    string Category,
    decimal Amount,
    string Severity,
    string Reason,
    bool IsVerified
);

public sealed record VerifiedBudgetRecommendation(
    string Category,
    decimal? ExistingBudget,
    decimal RecommendedBudget,
    decimal HistoricalAverage,
    string Reason,
    bool IsVerified
);

public sealed record VerifiedForecast(
    string Period,
    decimal? ForecastIncome,
    decimal? ForecastExpense,
    decimal? ExpectedSaving,
    decimal? ExpectedClosingBalance,
    string Confidence,
    bool IsVerified
);

public sealed record PythonAiExplainRequest(
    string UserId,
    string Question,
    VerifiedFinancialContext Context
);

public sealed record PythonAiExplainResponse(
    string Answer,
    bool IsVerified,
    string[] Limitations
);

public interface IPythonAiClient
{
    Task<PythonAiExplainResponse> ExplainAsync(
        string userId,
        string question,
        VerifiedFinancialContext context,
        CancellationToken cancellationToken = default);
}

public sealed class PythonAiClient : IPythonAiClient
{
    private readonly HttpClient _httpClient;

    public PythonAiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PythonAiExplainResponse> ExplainAsync(
        string userId,
        string question,
        VerifiedFinancialContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Question is required.", nameof(question));

        if (context.UserId != userId)
            throw new UnauthorizedAccessException("Financial context does not belong to the authenticated user.");

        // Only verified records are sent to the Python/Gemini layer.
        var safeContext = context with
        {
            Recommendations = context.Recommendations
                .Where(x => x.IsVerified)
                .ToList(),
            SpendingPatterns = context.SpendingPatterns
                .Where(x => x.IsVerified)
                .ToList(),
            Anomalies = context.Anomalies
                .Where(x => x.IsVerified)
                .ToList(),
            BudgetRecommendations = context.BudgetRecommendations
                .Where(x => x.IsVerified)
                .ToList(),
            Forecasts = context.Forecasts
                .Where(x => x.IsVerified)
                .ToList()
        };

        var request = new PythonAiExplainRequest(
            userId,
            question,
            safeContext);

        using var response = await _httpClient.PostAsJsonAsync(
            "api/ai/explain",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PythonAiExplainResponse>(
            cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException(
            "Python AI service returned an empty response.");
    }
}
