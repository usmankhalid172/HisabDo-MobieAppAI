namespace HisabDo.Forecasting;

public enum RecommendationType
{
    FinancialHealth,
    Savings,
    SpendingControl,
    AnomalyReview,
    CashFlow,
    Budget,
    BudgetAdjustment
}

public enum RecommendationPriority
{
    High,
    Medium,
    Low
}

public enum RecommendationSeverity
{
    Critical,
    High,
    Medium,
    Low
}

public sealed record RecommendationRequest(
    Guid UserId,
    DateOnly From,
    DateOnly To);

public sealed record Recommendation(
    RecommendationType Type,
    RecommendationPriority Priority,
    RecommendationSeverity Severity,
    string Category,
    string Reason,
    string Action,
    decimal? Amount = null,
    decimal? Percentage = null);

public sealed record RecommendationResponse(
    Guid UserId,
    string Status,
    IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<string> Limitations,
    IReadOnlyDictionary<string, string> DataAvailability);
