using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day12.AnomalyDetection;

public enum TransactionType { Income, Expense }
public enum AnomalyType { UnusualAmount, SuddenSpendingSpike, UnusualCategory, DuplicateTransaction }
public enum AnomalySeverity { Low, Medium, High }

public sealed record ExpenseTransaction(
    string UserId, string TransactionId, TransactionType TransactionType,
    decimal Amount, string? Category, DateTime Date, string? Description);

public sealed record AnomalyResult(
    string? TransactionId, AnomalyType Type, AnomalySeverity Severity,
    decimal Amount, string? Category, DateTime Date,
    decimal? BaselineValue, decimal? DeviationPercent,
    string ReasonCode, bool IsDuplicateCandidate);

public sealed record CategorySpikeResult(
    string Category, decimal CurrentAmount, decimal HistoricalAverage,
    decimal? GrowthPercent, AnomalySeverity Severity);

public sealed record SpendingAnomalyResult(
    string UserId, DateOnly AnalysisStart, DateOnly AnalysisEnd,
    int BaselinePeriods, IReadOnlyList<AnomalyResult> Anomalies,
    IReadOnlyList<CategorySpikeResult> PeriodSpikes);

public interface IExpenseTransactionRepository
{
    Task<IReadOnlyList<ExpenseTransaction>> GetExpensesAsync(
        string userId, DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default);
}

public sealed class SpendingAnomalyDetectionService
{
    // MVP defaults. Move these to validated configuration for production.
    private const decimal UnusualAmountMultiplier = 2.0m;
    private const decimal MediumSpikeThresholdPercent = 20m;
    private const decimal HighSpikeThresholdPercent = 50m;
    private const int MinimumBaselinePeriods = 3;
    private const int DuplicateWindowDays = 0;

    private readonly IExpenseTransactionRepository _repository;

    public SpendingAnomalyDetectionService(IExpenseTransactionRepository repository)
        => _repository = repository;

    public async Task<SpendingAnomalyResult> DetectAsync(
        string authenticatedUserId, DateOnly analysisStart, DateOnly analysisEnd,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authenticatedUserId))
            throw new ArgumentException("Authenticated user ID is required.", nameof(authenticatedUserId));

        if (analysisEnd < analysisStart)
            throw new ArgumentException("Analysis end date cannot be before analysis start date.");

        var periodDays = analysisEnd.DayNumber - analysisStart.DayNumber + 1;
        var currentStart = analysisStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var currentEndExclusive = analysisEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var historyStart = analysisStart.AddDays(-periodDays * MinimumBaselinePeriods)
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var loaded = await _repository.GetExpensesAsync(
            authenticatedUserId, historyStart, currentEndExclusive, cancellationToken);

        // Defense-in-depth user isolation.
        var transactions = loaded
            .Where(x => x.UserId == authenticatedUserId)
            .Where(x => x.TransactionType == TransactionType.Expense)
            .Where(x => x.Amount > 0)
            .OrderBy(x => x.Date)
            .ToList();

        var current = transactions
            .Where(x => x.Date >= currentStart && x.Date < currentEndExclusive)
            .ToList();

        var historical = transactions
            .Where(x => x.Date < currentStart)
            .ToList();

        var anomalies = new List<AnomalyResult>();

        DetectUnusualAmounts(current, historical, anomalies);
        DetectDuplicateCandidates(current, anomalies);
        var spikes = DetectCategorySpikes(current, historical, anomalies);

        return new SpendingAnomalyResult(
            authenticatedUserId, analysisStart, analysisEnd,
            MinimumBaselinePeriods, anomalies, spikes);
    }

    private static void DetectUnusualAmounts(
        IReadOnlyList<ExpenseTransaction> current,
        IReadOnlyList<ExpenseTransaction> historical,
        ICollection<AnomalyResult> anomalies)
    {
        var byCategory = historical
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => Normalize(x.Category!))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Amount).ToList());

        var overall = historical.Select(x => x.Amount).ToList();

        foreach (var tx in current)
        {
            var key = Normalize(tx.Category);
            var baseline = byCategory.TryGetValue(key, out var categoryAmounts)
                ? categoryAmounts : overall;

            if (baseline.Count < 2) continue;

            var avg = baseline.Average();
            var sd = PopulationStdDev(baseline);
            var threshold = Math.Max(avg * UnusualAmountMultiplier, avg + 2m * sd);

            if (tx.Amount < threshold) continue;

            var deviation = avg == 0
                ? (decimal?)null
                : (tx.Amount - avg) / avg * 100m;

            anomalies.Add(new AnomalyResult(
                tx.TransactionId, AnomalyType.UnusualAmount, AnomalySeverity.High,
                tx.Amount, tx.Category, tx.Date, avg, deviation,
                "AMOUNT_ABOVE_BASELINE", false));
        }
    }

    private static List<CategorySpikeResult> DetectCategorySpikes(
        IReadOnlyList<ExpenseTransaction> current,
        IReadOnlyList<ExpenseTransaction> historical,
        ICollection<AnomalyResult> anomalies)
    {
        var result = new List<CategorySpikeResult>();

        // Historical daily totals provide comparable observations without assuming
        // a specific production DB schema.
        var historyByCategory = historical
            .GroupBy(x => Normalize(x.Category))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Date.Date).Select(d => d.Sum(x => x.Amount)).ToList());

        var currentByCategory = current
            .GroupBy(x => Normalize(x.Category))
            .Select(g => new
            {
                Key = g.Key,
                Name = g.Select(x => x.Category).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                       ?? "Uncategorized",
                Amount = g.Sum(x => x.Amount)
            });

        foreach (var category in currentByCategory)
        {
            if (!historyByCategory.TryGetValue(category.Key, out var values) || values.Count == 0)
                continue;

            var average = values.Average();
            if (average <= 0) continue;

            var growth = (category.Amount - average) / average * 100m;
            if (growth < MediumSpikeThresholdPercent) continue;

            var severity = growth >= HighSpikeThresholdPercent
                ? AnomalySeverity.High : AnomalySeverity.Medium;

            result.Add(new CategorySpikeResult(
                category.Name, category.Amount, average, growth, severity));

            anomalies.Add(new AnomalyResult(
                null, AnomalyType.SuddenSpendingSpike, severity,
                category.Amount, category.Name, DateTime.UtcNow,
                average, growth, "CATEGORY_SPENDING_SPIKE", false));

            if (category.Amount >= average * UnusualAmountMultiplier)
            {
                anomalies.Add(new AnomalyResult(
                    null, AnomalyType.UnusualCategory, severity,
                    category.Amount, category.Name, DateTime.UtcNow,
                    average, growth, "CATEGORY_ABOVE_HISTORICAL_BASELINE", false));
            }
        }

        return result;
    }

    private static void DetectDuplicateCandidates(
        IReadOnlyList<ExpenseTransaction> current,
        ICollection<AnomalyResult> anomalies)
    {
        var ordered = current.OrderBy(x => x.Date).ThenBy(x => x.TransactionId).ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var first = ordered[i];
                var second = ordered[j];

                if ((second.Date.Date - first.Date.Date).TotalDays > DuplicateWindowDays)
                    break;

                if (first.Amount != second.Amount) continue;
                if (!CategoriesMatch(first.Category, second.Category)) continue;
                if (!DescriptionsMatch(first.Description, second.Description)) continue;

                anomalies.Add(new AnomalyResult(
                    second.TransactionId, AnomalyType.DuplicateTransaction,
                    AnomalySeverity.Medium, second.Amount, second.Category,
                    second.Date, first.Amount, null,
                    "POSSIBLE_DUPLICATE_TRANSACTION", true));
            }
        }
    }

    private static bool CategoriesMatch(string? a, string? b)
        => string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)
           || Normalize(a) == Normalize(b);

    private static bool DescriptionsMatch(string? a, string? b)
        => string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)
           || Normalize(a) == Normalize(b);

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "__UNCATEGORIZED__"
            : string.Join(" ", value.Trim().ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static decimal PopulationStdDev(IReadOnlyCollection<decimal> values)
    {
        if (values.Count == 0) return 0m;
        var avg = values.Average();
        var variance = values.Select(v => Math.Pow((double)(v - avg), 2)).Average();
        return (decimal)Math.Sqrt(variance);
    }
}
