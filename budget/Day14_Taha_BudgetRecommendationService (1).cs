using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day14;

public sealed class BudgetTransaction
{
    public Guid UserId { get; init; }
    public decimal Amount { get; init; }
    public string Category { get; init; } = "";
    public DateTime Date { get; init; }
    public bool IsExpense { get; init; }
}

public sealed class ExistingBudget
{
    public Guid UserId { get; init; }
    public string Category { get; init; } = "";
    public decimal Amount { get; init; }
}

public sealed class BudgetRecommendation
{
    public Guid UserId { get; init; }
    public string Category { get; init; } = "";
    public decimal HistoricalAverage { get; init; }
    public decimal? ExistingBudget { get; init; }
    public decimal RecommendedBudget { get; init; }
    public decimal? BudgetVariance { get; init; }
    public decimal? BudgetUtilizationPercent { get; init; }
    public string RecommendationReason { get; init; } = "";
    public string DataSufficiency { get; init; } = "";
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

public sealed class BudgetRecommendationResult
{
    public Guid UserId { get; init; }
    public DateTime AnalysisStart { get; init; }
    public DateTime AnalysisEnd { get; init; }
    public IReadOnlyList<BudgetRecommendation> Recommendations { get; init; }
        = Array.Empty<BudgetRecommendation>();
}

public interface IBudgetRecommendationRepository
{
    Task<IReadOnlyList<BudgetTransaction>> GetExpensesAsync(
        Guid userId, DateTime start, DateTime end,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExistingBudget>> GetBudgetsAsync(
        Guid userId, CancellationToken cancellationToken = default);
}

public sealed class BudgetRecommendationService
{
    private const int PreferredMinimumMonths = 2;
    private readonly IBudgetRecommendationRepository _repository;

    public BudgetRecommendationService(IBudgetRecommendationRepository repository)
        => _repository = repository;

    public async Task<BudgetRecommendationResult> GenerateAsync(
        Guid userId, DateTime analysisStart, DateTime analysisEnd,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required.", nameof(userId));

        if (analysisEnd <= analysisStart)
            throw new ArgumentException("Analysis end must be after analysis start.");

        var transactions = await _repository.GetExpensesAsync(
            userId, analysisStart, analysisEnd, cancellationToken);

        var budgets = await _repository.GetBudgetsAsync(
            userId, cancellationToken);

        var months = MonthCount(analysisStart, analysisEnd);

        var groups = transactions
            .Where(x => x.UserId == userId &&
                        x.IsExpense &&
                        x.Amount > 0 &&
                        !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Category.Trim(), StringComparer.OrdinalIgnoreCase);

        var recommendations = new List<BudgetRecommendation>();

        foreach (var group in groups)
        {
            var historicalAverage = group.Sum(x => x.Amount) / months;

            var budget = budgets.FirstOrDefault(x =>
                x.UserId == userId &&
                string.Equals(x.Category.Trim(), group.Key,
                    StringComparison.OrdinalIgnoreCase));

            decimal? existing = budget?.Amount;
            decimal? variance = existing.HasValue
                ? existing.Value - historicalAverage : null;

            decimal? utilization = existing.HasValue && existing.Value > 0
                ? historicalAverage / existing.Value * 100m : null;

            var limitations = new List<string>();
            string reason;
            string sufficiency;

            if (months < PreferredMinimumMonths)
            {
                sufficiency = "Limited";
                limitations.Add("Less than the preferred historical period is available.");
                reason = "Recommendation is based on limited historical spending data.";
            }
            else if (!existing.HasValue)
            {
                sufficiency = "Sufficient";
                reason = "No existing budget was found; recommendation uses the historical spending baseline.";
            }
            else if (existing.Value > 0 && historicalAverage > existing.Value)
            {
                sufficiency = "Sufficient";
                reason = "Verified historical spending is above the existing budget.";
            }
            else
            {
                sufficiency = "Sufficient";
                reason = "Recommendation is based on the historical spending baseline.";
            }

            recommendations.Add(new BudgetRecommendation
            {
                UserId = userId,
                Category = group.Key,
                HistoricalAverage = decimal.Round(historicalAverage, 2),
                ExistingBudget = existing,
                RecommendedBudget = decimal.Round(historicalAverage, 2),
                BudgetVariance = variance.HasValue ? decimal.Round(variance.Value, 2) : null,
                BudgetUtilizationPercent = utilization.HasValue
                    ? decimal.Round(utilization.Value, 2) : null,
                RecommendationReason = reason,
                DataSufficiency = sufficiency,
                Limitations = limitations
            });
        }

        return new BudgetRecommendationResult
        {
            UserId = userId,
            AnalysisStart = analysisStart,
            AnalysisEnd = analysisEnd,
            Recommendations = recommendations
                .OrderByDescending(x => x.HistoricalAverage)
                .ToList()
        };
    }

    private static int MonthCount(DateTime start, DateTime end)
    {
        var count = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
        return Math.Max(1, count);
    }
}

// AI boundary:
// Send only BudgetRecommendationResult to the explanation layer.
// The LLM may explain verified values but must not calculate, replace,
// or invent any recommendation, amount, category, or financial fact.
// Map the repository to the real HisabDo schema before production.
