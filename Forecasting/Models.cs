using System.ComponentModel.DataAnnotations;

namespace HisabDo.Forecasting;

public enum TransactionType { Income, Expense }
public enum ForecastFrequency { Monthly, Weekly }

public sealed record FinancialTransaction(
    Guid UserId,
    Guid TransactionId,
    TransactionType Type,
    decimal Amount,
    DateTime DateUtc,
    string? Category = null);

public sealed record Budget(
    Guid UserId,
    string Category,
    decimal MonthlyLimit);

public sealed record BudgetAnalysisRequest(
    Guid UserId,
    DateOnly From,
    DateOnly To);

public sealed record CategoryBudgetAnalysis(
    string Category,
    decimal AverageMonthlySpend,
    decimal CurrentBudget,
    decimal UtilizationPercent,
    decimal RecommendedBudget,
    bool NeedsAdjustment,
    string AdjustmentReason);

public sealed record BudgetAnalysisResult(
    Guid UserId,
    DateOnly From,
    DateOnly To,
    string Status,
    int MonthsWithData,
    decimal TotalIncome,
    decimal TotalExpense,
    IReadOnlyList<CategoryBudgetAnalysis> Categories);

public sealed record ForecastRequest(
    Guid UserId,
    DateOnly From,
    DateOnly To,
    ForecastFrequency Frequency = ForecastFrequency.Monthly,
    int ForecastPeriods = 1);

public sealed record ForecastPeriod(
    string Period,
    decimal Income,
    decimal Expense,
    decimal Savings,
    decimal? OpeningBalance,
    decimal? ClosingBalance);

public sealed record ForecastResult(
    Guid UserId,
    ForecastFrequency Frequency,
    IReadOnlyList<ForecastPeriod> Historical,
    IReadOnlyList<ForecastPeriod> Expected);

public sealed record ForecastInput(
    string Period,
    decimal Income,
    decimal Expense,
    decimal Savings,
    decimal? OpeningBalance,
    decimal? ClosingBalance);

public sealed record ForecastEngineResult(
    IReadOnlyList<ForecastInput> Periods);

public sealed class ForecastValidationException(string message) : Exception(message);
public sealed class ForecastUnavailableException(string message) : Exception(message);
public sealed class ForecastEngineException(string message, Exception? inner = null) : Exception(message, inner);
