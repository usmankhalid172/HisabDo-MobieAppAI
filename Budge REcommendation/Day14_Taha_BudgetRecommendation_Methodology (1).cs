using System;
using System.Collections.Generic;
using System.Linq;

namespace HisabDo.AI.Day14;

public sealed record BudgetTransaction(
    Guid UserId,
    string Category,
    decimal Amount,
    DateTime Date,
    bool IsExpense = true);

public sealed record ExistingBudget(
    Guid UserId,
    string Category,
    decimal Amount);

public sealed record BudgetRecommendation(
    Guid UserId,
    string Category,
    decimal HistoricalAverage,
    decimal? ExistingBudget,
    decimal RecommendedBudget,
    decimal? BudgetUtilizationPercent,
    string Status,
    string Reason,
    bool IsLimitedData);

public sealed class TahaBudgetRecommendationMethodology
{
    // Configurable MVP thresholds.
    public decimal SignificantOverspendingMultiplier { get; init; } = 1.20m;
    public decimal UnderspendingMultiplier { get; init; } = 0.80m;
    public int MinimumCompleteMonths { get; init; } = 2;

    public IReadOnlyList<BudgetRecommendation> Calculate(
        Guid userId,
        IEnumerable<BudgetTransaction> transactions,
        IEnumerable<ExistingBudget> budgets,
        DateTime analysisStart,
        DateTime analysisEnd)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required.", nameof(userId));

        if (analysisEnd <= analysisStart)
            throw new ArgumentException("Analysis end must be after analysis start.");

        var userTransactions = transactions
            .Where(x => x.UserId == userId
                     && x.IsExpense
                     && x.Amount > 0
                     && !string.IsNullOrWhiteSpace(x.Category)
                     && x.Date >= analysisStart
                     && x.Date < analysisEnd)
            .ToList();

        var userBudgets = budgets
            .Where(x => x.UserId == userId)
            .ToList();

        int monthCount = GetMonthCount(analysisStart, analysisEnd);

        return userTransactions
            .GroupBy(x => NormalizeCategory(x.Category))
            .Select(group =>
            {
                decimal historicalAverage = group.Sum(x => x.Amount) / monthCount;

                var budget = userBudgets.FirstOrDefault(x =>
                    NormalizeCategory(x.Category) == group.Key);

                decimal? existingBudget = budget?.Amount;

                decimal? utilization = existingBudget is > 0
                    ? historicalAverage / existingBudget.Value * 100m
                    : null;

                bool limited = monthCount < MinimumCompleteMonths;
                string status;
                string reason;

                if (limited)
                {
                    status = "Limited Data";
                    reason = "Less than the preferred historical period is available.";
                }
                else if (!existingBudget.HasValue)
                {
                    status = "New Budget";
                    reason = "No existing budget was found; recommendation uses historical average spending.";
                }
                else if (existingBudget.Value > 0 &&
                         historicalAverage >= existingBudget.Value * SignificantOverspendingMultiplier)
                {
                    status = "Significant Overspending";
                    reason = "Historical average spending is at least 20% above the current budget.";
                }
                else if (existingBudget.Value > 0 &&
                         historicalAverage > existingBudget.Value)
                {
                    status = "Overspending";
                    reason = "Historical average spending is above the current budget.";
                }
                else if (existingBudget.Value > 0 &&
                         historicalAverage < existingBudget.Value * UnderspendingMultiplier)
                {
                    status = "Underspending";
                    reason = "Historical average spending is below 80% of the current budget.";
                }
                else
                {
                    status = "Within/Near Budget";
                    reason = "Historical average spending is within the configured budget range.";
                }

                return new BudgetRecommendation(
                    userId,
                    group.Key,
                    decimal.Round(historicalAverage, 2),
                    existingBudget,
                    decimal.Round(historicalAverage, 2),
                    utilization.HasValue ? decimal.Round(utilization.Value, 2) : null,
                    status,
                    reason,
                    limited);
            })
            .OrderByDescending(x => x.HistoricalAverage)
            .ToList();
    }

    private static string NormalizeCategory(string value)
        => value.Trim().ToLowerInvariant();

    private static int GetMonthCount(DateTime start, DateTime end)
    {
        var months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
        return Math.Max(1, months);
    }
}

// Evaluation guidance:
// 1. Test known sample datasets.
// 2. Verify recommendation = historical average.
// 3. Verify threshold classifications.
// 4. Test zero budget and missing budget.
// 5. Test limited history.
// 6. Test multiple users for data isolation.
// 7. Compare backend results with expected test results.
//
// Production note:
// Map BudgetTransaction/ExistingBudget to the real HisabDo entities,
// repository, DbContext and authorization conventions before integration.
// The LLM must only explain these verified results and must not recalculate them.
