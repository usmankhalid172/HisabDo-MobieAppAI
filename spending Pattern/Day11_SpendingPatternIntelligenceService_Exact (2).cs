using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed record SpendingTransaction(
    Guid Id, Guid UserId, string Type, decimal Amount,
    string? Category, string? Description, DateTime DateUtc);

public sealed record CategorySpending(
    string Category, decimal Amount, decimal Percentage);

public sealed record CategoryChange(
    string Category, decimal CurrentAmount, decimal PreviousAmount,
    decimal ChangeAmount, decimal? ChangePercentage, string Trend);

public sealed record RecurringCandidate(
    string Category, string? Description, decimal AverageAmount,
    int Occurrences, string Pattern);

public sealed record SpendingPatternResult(
    decimal TotalExpense,
    IReadOnlyList<CategorySpending> TopCategories,
    IReadOnlyList<CategoryChange> CategoryChanges,
    IReadOnlyList<RecurringCandidate> RecurringCandidates);

public interface ISpendingTransactionRepository
{
    Task<IReadOnlyList<SpendingTransaction>> GetExpensesAsync(
        Guid userId, DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);
}

public sealed class SpendingPatternIntelligenceService
{
    private const decimal TrendThresholdPercent = 5m;
    private const int TopCategoryCount = 5;
    private const int MinimumRecurringOccurrences = 3;

    private readonly ISpendingTransactionRepository _repository;

    public SpendingPatternIntelligenceService(
        ISpendingTransactionRepository repository)
    {
        _repository = repository;
    }

    public async Task<SpendingPatternResult> AnalyzeAsync(
        Guid userId, DateTime currentStartUtc, DateTime currentEndUtc,
        CancellationToken cancellationToken = default)
    {
        if (currentEndUtc <= currentStartUtc)
            throw new ArgumentException("End date must be after start date.");

        var current = await _repository.GetExpensesAsync(
            userId, currentStartUtc, currentEndUtc, cancellationToken);

        var duration = currentEndUtc - currentStartUtc;
        var previousStartUtc = currentStartUtc - duration;
        var previousEndUtc = currentStartUtc;

        var previous = await _repository.GetExpensesAsync(
            userId, previousStartUtc, previousEndUtc, cancellationToken);

        var validCurrent = current.Where(IsValidExpense).ToList();
        var validPrevious = previous.Where(IsValidExpense).ToList();

        var totalExpense = validCurrent.Sum(x => x.Amount);

        return new SpendingPatternResult(
            totalExpense,
            BuildTopCategories(validCurrent, totalExpense),
            BuildCategoryChanges(validCurrent, validPrevious),
            DetectRecurringCandidates(validCurrent));
    }

    private static bool IsValidExpense(SpendingTransaction t) =>
        t.Amount > 0 &&
        string.Equals(t.Type, "Expense",
            StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<CategorySpending> BuildTopCategories(
        IReadOnlyList<SpendingTransaction> transactions,
        decimal totalExpense)
    {
        return transactions
            .GroupBy(x => NormalizeCategory(x.Category))
            .Select(g => new
            {
                Category = g.Key,
                Amount = g.Sum(x => x.Amount)
            })
            .OrderByDescending(x => x.Amount)
            .Take(TopCategoryCount)
            .Select(x => new CategorySpending(
                x.Category,
                x.Amount,
                totalExpense == 0
                    ? 0
                    : Math.Round(x.Amount / totalExpense * 100m, 2)))
            .ToList();
    }

    private static IReadOnlyList<CategoryChange> BuildCategoryChanges(
        IReadOnlyList<SpendingTransaction> current,
        IReadOnlyList<SpendingTransaction> previous)
    {
        var currentByCategory = current
            .GroupBy(x => NormalizeCategory(x.Category))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var previousByCategory = previous
            .GroupBy(x => NormalizeCategory(x.Category))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var categories = currentByCategory.Keys
            .Union(previousByCategory.Keys)
            .OrderBy(x => x);

        return categories.Select(category =>
        {
            currentByCategory.TryGetValue(category, out var currentAmount);
            previousByCategory.TryGetValue(category, out var previousAmount);

            var changeAmount = currentAmount - previousAmount;
            decimal? changePercentage = previousAmount == 0
                ? null
                : Math.Round(changeAmount / previousAmount * 100m, 2);

            return new CategoryChange(
                category,
                currentAmount,
                previousAmount,
                changeAmount,
                changePercentage,
                ClassifyTrend(
                    currentAmount,
                    previousAmount,
                    changePercentage));
        }).ToList();
    }

    private static string ClassifyTrend(
        decimal currentAmount,
        decimal previousAmount,
        decimal? changePercentage)
    {
        if (previousAmount == 0 && currentAmount > 0)
            return "New Category";

        if (changePercentage > TrendThresholdPercent)
            return "Increasing";

        if (changePercentage < -TrendThresholdPercent)
            return "Decreasing";

        return "Stable";
    }

    private static IReadOnlyList<RecurringCandidate>
        DetectRecurringCandidates(
            IReadOnlyList<SpendingTransaction> transactions)
    {
        var result = new List<RecurringCandidate>();

        var groups = transactions
            .Where(x => !string.IsNullOrWhiteSpace(x.Description))
            .GroupBy(x => new
            {
                Category = NormalizeCategory(x.Category),
                Description = NormalizeDescription(x.Description)
            });

        foreach (var group in groups)
        {
            var ordered = group.OrderBy(x => x.DateUtc).ToList();

            if (ordered.Count < MinimumRecurringOccurrences)
                continue;

            var intervals = ordered
                .Zip(ordered.Skip(1),
                    (a, b) => (b.DateUtc - a.DateUtc).TotalDays)
                .ToList();

            var averageInterval = intervals.Count == 0
                ? 0
                : intervals.Average();

            var regular =
                IsNear(averageInterval, 7, 2) ||
                IsNear(averageInterval, 14, 3) ||
                IsNear(averageInterval, 30, 5) ||
                IsNear(averageInterval, 90, 10);

            if (!regular)
                continue;

            var averageAmount = ordered.Average(x => x.Amount);

            result.Add(new RecurringCandidate(
                group.Key.Category,
                group.Key.Description,
                Math.Round(averageAmount, 2),
                ordered.Count,
                $"Approximately every {Math.Round(averageInterval, 1)} days"));
        }

        return result;
    }

    private static bool IsNear(double value, double target, double tolerance) =>
        Math.Abs(value - target) <= tolerance;

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category)
            ? "Uncategorized"
            : category.Trim();

    private static string NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? string.Empty
            : description.Trim().ToLowerInvariant();
}