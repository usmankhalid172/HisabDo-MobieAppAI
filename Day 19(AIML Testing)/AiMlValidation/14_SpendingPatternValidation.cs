using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Validation;

internal sealed class InMemorySpendingRepo : ISpendingTransactionRepository
{
    private readonly List<SpendingTransaction> _all;
    public InMemorySpendingRepo(List<SpendingTransaction> all) => _all = all;
    public Task<IReadOnlyList<SpendingTransaction>> GetExpensesAsync(Guid userId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SpendingTransaction>>(_all.Where(t => t.UserId == userId && t.DateUtc >= startUtc && t.DateUtc < endUtc).ToList());
}

public static class SpendingPatternValidation
{
    private static readonly Guid User = Guid.NewGuid();

    private static SpendingTransaction Tx(decimal amount, string? category, DateTime date, string type = "Expense", string? desc = null) =>
        new(Guid.NewGuid(), User, type, amount, category, desc, date);

    private static async Task<SpendingPatternResult> Run(List<SpendingTransaction> txns, DateTime start, DateTime end)
    {
        var service = new SpendingPatternIntelligenceService(new InMemorySpendingRepo(txns));
        return await service.AnalyzeAsync(User, start, end);
    }

    public static async Task RunAll()
    {
        Report.Suite("SPENDING PATTERN INTELLIGENCE — SpendingPatternIntelligenceService (Day 11)");
        await TopCategoryTest();
        await TrendClassificationBoundaryTests();
        await RecurringDetectionTests();
        await ZeroAndTypeFilteringTests();
    }

    private static async Task TopCategoryTest()
    {
        Report.Info("--- Top categories and percentages ---");
        var start = new DateTime(2026, 9, 1); var end = new DateTime(2026, 10, 1);
        var txns = new List<SpendingTransaction>
        {
            Tx(6000, "Food", new DateTime(2026, 9, 10)),
            Tx(3000, "Transport", new DateTime(2026, 9, 12)),
            Tx(1000, "Bills", new DateTime(2026, 9, 15)),
        };
        var result = await Run(txns, start, end);
        Report.Check("Total expense sums correctly", result.TotalExpense == 10000m, $"got {result.TotalExpense}");
        Report.Check("Top category ordered by amount descending", result.TopCategories.First().Category == "Food");
        Report.Check("Percentages sum to 100% across all categories", result.TopCategories.Sum(c => c.Percentage) == 100m,
            $"got {result.TopCategories.Sum(c => c.Percentage)}");
        Report.Check("Food's percentage is correctly 60%", result.TopCategories.First(c => c.Category == "Food").Percentage == 60m);
    }

    private static async Task TrendClassificationBoundaryTests()
    {
        Report.Info("--- Trend classification boundary tests (threshold = ±5%, strict) ---");
        var start = new DateTime(2026, 9, 1); var end = new DateTime(2026, 10, 1); // previous = Aug 1 - Sep 1

        async Task<string> TrendFor(decimal previous, decimal current)
        {
            var txns = new List<SpendingTransaction>
            {
                Tx(previous, "Food", new DateTime(2026, 8, 15)),
                Tx(current, "Food", new DateTime(2026, 9, 15)),
            };
            var result = await Run(txns, start, end);
            return result.CategoryChanges.Single(c => c.Category == "Food").Trend;
        }

        Report.Check("Exactly +5% change -> Stable (rule is strictly '>5', not '>=5')", await TrendFor(1000, 1050) == "Stable");
        Report.Check("+5.01% change -> Increasing", await TrendFor(10000, 10501) == "Increasing");
        Report.Check("Exactly -5% change -> Stable (strictly '<-5', not '<=-5')", await TrendFor(1000, 950) == "Stable");
        Report.Check("-5.01% change -> Decreasing", await TrendFor(10000, 9499) == "Decreasing");
        Report.Check("No change (0%) -> Stable", await TrendFor(1000, 1000) == "Stable");

        var newCatTxns = new List<SpendingTransaction> { Tx(2000, "Subscriptions", new DateTime(2026, 9, 15)) };
        var newCatResult = await Run(newCatTxns, start, end);
        Report.Check("Previous=0, current>0 -> 'New Category' (not a percentage-based trend, avoids a divide-by-zero %)",
            newCatResult.CategoryChanges.Single(c => c.Category == "Subscriptions").Trend == "New Category");
        Report.Check("New Category: ChangePercentage is null, never a fabricated infinite/huge %",
            newCatResult.CategoryChanges.Single(c => c.Category == "Subscriptions").ChangePercentage == null);

        // Category existed before but has zero spend in both periods should never appear at all
        // (only categories present in current OR previous are included) — a category with
        // previous=0 AND current=0 simply won't show up, which is correct (nothing to report).
    }

    private static async Task RecurringDetectionTests()
    {
        Report.Info("--- Recurring-expense detection (>=3 occurrences, interval near 7/14/30/90 days) ---");
        var start = new DateTime(2026, 6, 1); var end = new DateTime(2026, 10, 1);

        var monthly = new List<SpendingTransaction>
        {
            Tx(1200, "Subscriptions", new DateTime(2026, 6, 15), desc: "Netflix"),
            Tx(1200, "Subscriptions", new DateTime(2026, 7, 15), desc: "Netflix"),
            Tx(1200, "Subscriptions", new DateTime(2026, 8, 15), desc: "Netflix"),
            Tx(1200, "Subscriptions", new DateTime(2026, 9, 15), desc: "Netflix"),
        };
        var r1 = await Run(monthly, start, end);
        Report.Check("4 monthly (~30-day interval) same-description transactions -> flagged recurring",
            r1.RecurringCandidates.Any(c => c.Description == "netflix"),
            $"candidates={string.Join(",", r1.RecurringCandidates.Select(c => c.Description))}");

        var onlyTwo = new List<SpendingTransaction>
        {
            Tx(500, "Food", new DateTime(2026, 8, 1), desc: "Grocery run"),
            Tx(500, "Food", new DateTime(2026, 9, 1), desc: "Grocery run"),
        };
        var r2 = await Run(onlyTwo, start, end);
        Report.Check("Only 2 occurrences (below MinimumRecurringOccurrences=3) -> NOT flagged", r2.RecurringCandidates.Count == 0);

        var irregular = new List<SpendingTransaction>
        {
            Tx(500, "Shopping", new DateTime(2026, 6, 3), desc: "Random buy"),
            Tx(500, "Shopping", new DateTime(2026, 7, 22), desc: "Random buy"), // 49 days later
            Tx(500, "Shopping", new DateTime(2026, 9, 30), desc: "Random buy"), // 70 days later — irregular
        };
        var r3 = await Run(irregular, start, end);
        Report.Check("3 occurrences but irregular intervals (not near 7/14/30/90 days) -> NOT flagged",
            r3.RecurringCandidates.Count == 0, "irregular spacing correctly does not get mistaken for a subscription pattern");

        var weekly = new List<SpendingTransaction>
        {
            Tx(300, "Food", new DateTime(2026, 9, 1), desc: "Weekly groceries"),
            Tx(300, "Food", new DateTime(2026, 9, 8), desc: "Weekly groceries"),
            Tx(300, "Food", new DateTime(2026, 9, 15), desc: "Weekly groceries"),
            Tx(300, "Food", new DateTime(2026, 9, 22), desc: "Weekly groceries"),
        };
        var r4 = await Run(weekly, start, end);
        Report.Check("4 weekly (~7-day interval) transactions -> flagged recurring", r4.RecurringCandidates.Any(c => c.Description == "weekly groceries"));

        var noDescription = new List<SpendingTransaction>
        {
            Tx(1200, "Subscriptions", new DateTime(2026, 6, 15)),
            Tx(1200, "Subscriptions", new DateTime(2026, 7, 15)),
            Tx(1200, "Subscriptions", new DateTime(2026, 8, 15)),
        };
        var r5 = await Run(noDescription, start, end);
        Report.Check("Transactions with NO description are excluded from recurring detection entirely (can't group them meaningfully)",
            r5.RecurringCandidates.Count == 0);
    }

    private static async Task ZeroAndTypeFilteringTests()
    {
        Report.Info("--- Zero/negative value and transaction-type filtering ---");
        var start = new DateTime(2026, 9, 1); var end = new DateTime(2026, 10, 1);

        var mixed = new List<SpendingTransaction>
        {
            Tx(5000, "Salary", new DateTime(2026, 9, 5), type: "Income"), // must be excluded
            Tx(1000, "Food", new DateTime(2026, 9, 10)),
            Tx(0, "Food", new DateTime(2026, 9, 11)),      // zero -> excluded
            Tx(-200, "Food", new DateTime(2026, 9, 12)),   // negative -> excluded
            Tx(500, null, new DateTime(2026, 9, 13)),      // null category -> "Uncategorized"
        };
        var result = await Run(mixed, start, end);
        Report.Check("Income-type transactions are excluded from spending analysis", result.TotalExpense == 1500m, $"got {result.TotalExpense} (expected 1000 Food + 500 Uncategorized)");
        Report.Check("Null category normalizes to 'Uncategorized', not a crash or blank key",
            result.TopCategories.Any(c => c.Category == "Uncategorized"));

        var empty = await Run(new List<SpendingTransaction>(), start, end);
        Report.Check("Zero transactions at all -> TotalExpense 0, empty lists, no crash",
            empty.TotalExpense == 0 && empty.TopCategories.Count == 0 && empty.CategoryChanges.Count == 0 && empty.RecurringCandidates.Count == 0);
    }
}
