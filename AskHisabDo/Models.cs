namespace HisabDo.Forecasting;

public sealed record AskHisabDoRequest(
    Guid UserId,
    DateOnly From,
    DateOnly To,
    string? Question = null);

public sealed record AskHisabDoResponse(
    Guid UserId,
    string? Question,
    string Status,
    VerifiedFinancialContext Context);

public sealed record VerifiedFinancialContext(
    DateOnly From,
    DateOnly To,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetCashFlow,
    IReadOnlyDictionary<string, decimal> CategorySpending,
    IReadOnlyList<Budget> Budgets,
    FinancialHealthScore HealthScore,
    IReadOnlyList<SpendingAnomaly> Anomalies,
    ForecastResult? CashFlowForecast,
    IReadOnlyList<BudgetRecommendation> BudgetRecommendations,
    IReadOnlyDictionary<string, string> DataAvailability,
    string Verification);

public sealed record FinancialHealthScore(
    int Score,
    string Status,
    IReadOnlyList<string> Factors);

public sealed record SpendingAnomaly(
    string Category,
    decimal Amount,
    decimal TypicalAmount,
    DateOnly Date,
    string Reason);

public sealed record BudgetRecommendation(
    string Category,
    decimal CurrentBudget,
    decimal AverageMonthlySpend,
    decimal RecommendedBudget,
    string Reason);
