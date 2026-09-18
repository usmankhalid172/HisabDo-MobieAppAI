using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.SpendingPatterns;

public sealed record Transaction(
    string UserId, string TransactionId, string TransactionType,
    decimal Amount, string? Category, DateTime Date, string? Description = null);

public sealed record CategorySpending(string Category, decimal Amount, decimal Percentage);

public sealed record CategoryChange(
    string Category, decimal CurrentAmount, decimal PreviousAmount,
    decimal ChangeAmount, decimal? ChangePercent, string Trend);

public sealed record RecurringCandidate(string Description, decimal AverageAmount, string Frequency);

public sealed record SpendingPatternResult(
    string UserId, DateTime From, DateTime To,
    DateTime PreviousFrom, DateTime PreviousTo,
    decimal TotalExpense, decimal PreviousTotalExpense,
    decimal? ExpenseGrowthPercent,
    IReadOnlyList<CategorySpending> TopCategories,
    CategoryChange? LargestIncreasingCategory,
    CategoryChange? LargestDecreasingCategory,
    IReadOnlyList<RecurringCandidate> RecurringCandidates);

public interface ITransactionRepository
{
    Task<IReadOnlyList<Transaction>> GetTransactionsAsync(
        string userId, DateTime from, DateTime to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic Day 11 analytics. No LLM is used for financial calculations.
/// Map the repository/DTOs to the real HisabDo codebase before production merge.
/// </summary>
public sealed class SpendingPatternIntelligenceService
{
    private readonly ITransactionRepository _repository;

    public SpendingPatternIntelligenceService(ITransactionRepository repository)
        => _repository = repository;

    public async Task<SpendingPatternResult> AnalyzeAsync(
        string authenticatedUserId, DateTime from, DateTime to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authenticatedUserId))
            throw new ArgumentException("Authenticated user ID is required.", nameof(authenticatedUserId));

        if (to.Date < from.Date)
            throw new ArgumentException("End date cannot be before start date.");

        var days = (to.Date - from.Date).Days + 1;
        var previousTo = from.Date.AddDays(-1);
        var previousFrom = previousTo.AddDays(-days + 1);

        // Repository MUST enforce user-level authorization.
        var current = await _repository.GetTransactionsAsync(
            authenticatedUserId, from.Date, to.Date, cancellationToken);
        var previous = await _repository.GetTransactionsAsync(
            authenticatedUserId, previousFrom, previousTo, cancellationToken);

        var currentExpenses = ValidExpenses(current);
        var previousExpenses = ValidExpenses(previous);

        var total = currentExpenses.Sum(x => x.Amount);
        var previousTotal = previousExpenses.Sum(x => x.Amount);

        var topCategories = CalculateCategories(currentExpenses, total);
        var currentByCategory = CategoryAmounts(currentExpenses);
        var previousByCategory = CategoryAmounts(previousExpenses);
        var changes = CategoryChanges(currentByCategory, previousByCategory);

        return new SpendingPatternResult(
            authenticatedUserId, from.Date, to.Date, previousFrom, previousTo,
            total, previousTotal, Growth(total, previousTotal),
            topCategories,
            changes.Where(x => x.ChangeAmount > 0)
                   .OrderByDescending(x => x.ChangeAmount).FirstOrDefault(),
            changes.Where(x => x.ChangeAmount < 0)
                   .OrderBy(x => x.ChangeAmount).FirstOrDefault(),
            DetectRecurringCandidates(currentExpenses));
    }

    private static List<Transaction> ValidExpenses(IEnumerable<Transaction> tx)
        => tx.Where(x => x.TransactionType.Equals("Expense", StringComparison.OrdinalIgnoreCase))
             .Where(x => x.Amount >= 0)
             .ToList();

    private static IReadOnlyList<CategorySpending> CalculateCategories(
        IEnumerable<Transaction> expenses, decimal total)
    {
        if (total <= 0) return Array.Empty<CategorySpending>();

        return expenses.GroupBy(x => NormalizeCategory(x.Category))
            .Select(g => new CategorySpending(
                g.Key, g.Sum(x => x.Amount),
                Math.Round(g.Sum(x => x.Amount) / total * 100m, 2)))
            .OrderByDescending(x => x.Amount)
            .ToList();
    }

    private static Dictionary<string, decimal> CategoryAmounts(IEnumerable<Transaction> expenses)
        => expenses.GroupBy(x => NormalizeCategory(x.Category))
                   .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount),
                       StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<CategoryChange> CategoryChanges(
        Dictionary<string, decimal> current, Dictionary<string, decimal> previous)
    {
        var names = new HashSet<string>(current.Keys.Concat(previous.Keys),
            StringComparer.OrdinalIgnoreCase);
        var result = new List<CategoryChange>();

        foreach (var name in names)
        {
            current.TryGetValue(name, out var c);
            previous.TryGetValue(name, out var p);
            var change = c - p;

            var trend = p == 0 && c > 0 ? "New" :
                        p > 0 && c == 0 ? "Stopped" :
                        change > 0 ? "Increased" :
                        change < 0 ? "Decreased" : "Unchanged";

            var percent = p == 0 ? null : Math.Round(change / p * 100m, 2);
            result.Add(new CategoryChange(name, c, p, change, percent, trend));
        }

        return result.OrderByDescending(x => Math.Abs(x.ChangeAmount)).ToList();
    }

    private static decimal? Growth(decimal current, decimal previous)
        => previous == 0 ? null : Math.Round((current - previous) / previous * 100m, 2);

    private static IReadOnlyList<RecurringCandidate> DetectRecurringCandidates(
        IEnumerable<Transaction> expenses)
    {
        // Conservative MVP: repeated descriptions occurring at least 3 times.
        // Production can add merchant normalization, interval/amount tolerance and confidence.
        return expenses.Where(x => !string.IsNullOrWhiteSpace(x.Description))
            .GroupBy(x => x.Description!.Trim().ToLowerInvariant())
            .Where(g => g.Count() >= 3)
            .Select(g =>
            {
                var dates = g.Select(x => x.Date.Date).OrderBy(x => x).ToList();
                var intervals = dates.Zip(dates.Skip(1), (a, b) => (b - a).TotalDays).ToList();
                var avg = intervals.Count == 0 ? 0 : intervals.Average();
                var frequency = avg is >= 25 and <= 35 ? "Monthly" :
                                 avg is >= 6 and <= 8 ? "Weekly" : "Repeated";
                return new RecurringCandidate(g.First().Description!.Trim(),
                    Math.Round(g.Average(x => x.Amount), 2), frequency);
            }).ToList();
    }

    private static string NormalizeCategory(string? category)
        => string.IsNullOrWhiteSpace(category) ? "Uncategorized" : category.Trim();
}
