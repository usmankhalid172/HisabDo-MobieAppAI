using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HisabDo.AI.Day14;

namespace HisabDo.AI.Validation;

internal sealed class InMemoryBudgetRepo : IBudgetRecommendationRepository
{
    private readonly List<BudgetTransaction> _txns;
    private readonly List<ExistingBudget> _budgets;
    public InMemoryBudgetRepo(List<BudgetTransaction> txns, List<ExistingBudget> budgets) { _txns = txns; _budgets = budgets; }

    public Task<IReadOnlyList<BudgetTransaction>> GetExpensesAsync(Guid userId, DateTime start, DateTime end, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<BudgetTransaction>>(_txns.Where(t => t.UserId == userId && t.Date >= start && t.Date <= end).ToList());

    public Task<IReadOnlyList<ExistingBudget>> GetBudgetsAsync(Guid userId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExistingBudget>>(_budgets.Where(b => b.UserId == userId).ToList());
}

public static class BudgetRecommendationValidation
{
    private static readonly Guid User = Guid.NewGuid();

    private static BudgetTransaction Exp(decimal amount, string category, DateTime date) =>
        new() { UserId = User, Amount = amount, Category = category, Date = date, IsExpense = true };

    private static async Task<BudgetRecommendationResult> Run(List<BudgetTransaction> txns, List<ExistingBudget> budgets, DateTime start, DateTime end)
    {
        var service = new BudgetRecommendationService(new InMemoryBudgetRepo(txns, budgets));
        return await service.GenerateAsync(User, start, end);
    }

    public static async Task RunAll()
    {
        Report.Suite("BUDGET RECOMMENDATION — BudgetRecommendationService (Day 14)");
        await CategoryRuleTests();
        await ZeroBudgetGuardTest();
        await InsufficientMonthsBoundaryTest();
        await NegativeAndZeroValueFilteringTest();
        await AveragingDilutionTest();
    }

    private static async Task CategoryRuleTests()
    {
        Report.Info("--- Category-wise rule tests (3-month window, Jun-Aug 2026) ---");
        var start = new DateTime(2026, 6, 1);
        var end = new DateTime(2026, 8, 31);

        var txns = new List<BudgetTransaction>
        {
            Exp(14000, "Food", new DateTime(2026, 6, 15)), Exp(15500, "Food", new DateTime(2026, 7, 15)), Exp(15500, "Food", new DateTime(2026, 8, 15)), // avg 15000
            Exp(4000, "Transport", new DateTime(2026, 6, 20)), Exp(4500, "Transport", new DateTime(2026, 7, 20)), Exp(4500, "Transport", new DateTime(2026, 8, 20)), // avg 4333.33
            Exp(3000, "Shopping", new DateTime(2026, 8, 12)), // avg 1000 (no budget at all)
            Exp(4500, "Bills", new DateTime(2026, 6, 5)), Exp(4500, "Bills", new DateTime(2026, 7, 5)), Exp(4500, "Bills", new DateTime(2026, 8, 5)), // avg exactly = budget
        };
        var budgets = new List<ExistingBudget>
        {
            new() { UserId = User, Category = "Food", Amount = 12000 },     // below average -> "above existing budget" reason
            new() { UserId = User, Category = "Transport", Amount = 6000 }, // above average -> baseline reason, no forced reduction
            new() { UserId = User, Category = "Bills", Amount = 4500 },     // exactly equal
        };
        var result = await Run(txns, budgets, start, end);

        var food = result.Recommendations.First(r => r.Category == "Food");
        Report.Check("Food: historical average computed correctly (15000)", food.HistoricalAverage == 15000m, $"got {food.HistoricalAverage}");
        Report.Check("Food: over-budget reason used when average > existing budget", food.RecommendationReason.Contains("above the existing budget"));
        Report.Check("Food: recommended budget equals historical average, not the lower existing budget", food.RecommendedBudget == 15000m);

        var transport = result.Recommendations.First(r => r.Category == "Transport");
        Report.Check("Transport: baseline reason used when existing budget already has headroom (not forced down)",
            transport.RecommendationReason.Contains("historical spending baseline") && !transport.RecommendationReason.Contains("above"));
        Report.Check("Transport: variance is positive (budget > average = headroom)", transport.BudgetVariance > 0);

        var shopping = result.Recommendations.First(r => r.Category == "Shopping");
        Report.Check("Shopping: no-existing-budget reason used", shopping.RecommendationReason.Contains("No existing budget"));
        Report.Check("Shopping: utilization is null when there's no budget to divide by (never a fabricated %)", shopping.BudgetUtilizationPercent == null);

        var bills = result.Recommendations.First(r => r.Category == "Bills");
        Report.Check("Bills: exactly-equal average/budget -> zero variance, baseline reason (not flagged over)",
            bills.BudgetVariance == 0m && !bills.RecommendationReason.Contains("above"));
    }

    private static async Task ZeroBudgetGuardTest()
    {
        Report.Info("--- Zero-existing-budget divide-by-zero guard ---");
        var start = new DateTime(2026, 6, 1);
        var end = new DateTime(2026, 8, 31);
        var txns = new List<BudgetTransaction> { Exp(1000, "Entertainment", new DateTime(2026, 6, 10)), Exp(1200, "Entertainment", new DateTime(2026, 7, 10)), Exp(800, "Entertainment", new DateTime(2026, 8, 10)) };
        var budgets = new List<ExistingBudget> { new() { UserId = User, Category = "Entertainment", Amount = 0 } };
        var result = await Run(txns, budgets, start, end);
        var rec = result.Recommendations.Single();
        Report.Check("Zero existing budget: utilization is null, not Infinity/NaN/crash", rec.BudgetUtilizationPercent == null, $"got {rec.BudgetUtilizationPercent}");
        Report.Check("Zero existing budget: variance is still computed (0 - average)", rec.BudgetVariance == -rec.HistoricalAverage);
    }

    private static async Task InsufficientMonthsBoundaryTest()
    {
        Report.Info("--- Insufficient-months boundary (PreferredMinimumMonths=2) ---");

        // Exactly 1 calendar month requested -> below the 2-month preferred minimum -> "Limited"
        var oneMonthStart = new DateTime(2026, 8, 1);
        var oneMonthEnd = new DateTime(2026, 8, 31);
        var txns1 = new List<BudgetTransaction> { Exp(9000, "Food", new DateTime(2026, 8, 15)) };
        var r1 = await Run(txns1, new List<ExistingBudget>(), oneMonthStart, oneMonthEnd);
        Report.Check("1-month window -> DataSufficiency 'Limited'", r1.Recommendations.Single().DataSufficiency == "Limited");
        Report.Check("1-month window -> limitation explicitly states the reason", r1.Recommendations.Single().Limitations.Any(l => l.Contains("preferred historical period")));

        // Exactly 2 calendar months -> meets the minimum -> "Sufficient"
        var twoMonthStart = new DateTime(2026, 7, 1);
        var twoMonthEnd = new DateTime(2026, 8, 31);
        var txns2 = new List<BudgetTransaction> { Exp(9000, "Food", new DateTime(2026, 7, 15)), Exp(9000, "Food", new DateTime(2026, 8, 15)) };
        var r2 = await Run(txns2, new List<ExistingBudget>(), twoMonthStart, twoMonthEnd);
        Report.Check("Boundary: exactly 2 months -> DataSufficiency 'Sufficient' (not 'Limited')", r2.Recommendations.Single().DataSufficiency == "Sufficient");

        // Note: DataSufficiency here reflects the requested calendar window
        // length, not whether the category actually has data in every
        // month of it — same class of consideration as Day 14's earlier
        // finding. Confirm this is understood, not assumed away.
        Report.Warn("DataSufficiency methodology note",
            "\"Limited\" is driven purely by the requested date-range length (months < 2), not by how many of those months the category actually has transactions in. " +
            "A category with real data in only 1 of a 3-month requested window still reports \"Sufficient\".");
    }

    private static async Task NegativeAndZeroValueFilteringTest()
    {
        Report.Info("--- Negative / zero value filtering ---");
        var start = new DateTime(2026, 6, 1);
        var end = new DateTime(2026, 8, 31);
        var txns = new List<BudgetTransaction>
        {
            Exp(9000, "Food", new DateTime(2026, 8, 15)),
            Exp(0, "Food", new DateTime(2026, 8, 16)),      // zero -> filtered (Amount > 0 guard)
            Exp(-500, "Food", new DateTime(2026, 8, 17)),   // negative -> filtered
        };
        var result = await Run(txns, new List<ExistingBudget>(), start, end);
        var food = result.Recommendations.Single();
        Report.Check("Zero/negative-amount transactions excluded from the average",
            food.HistoricalAverage == 3000m, // 9000/3 months, not diluted or inflated by the 0/-500 entries
            $"got {food.HistoricalAverage}");
    }

    private static async Task AveragingDilutionTest()
    {
        Report.Info("--- Re-confirming known issue: average divides by full requested window, not months with data ---");
        var start = new DateTime(2026, 6, 1);
        var end = new DateTime(2026, 8, 31); // 3-month window
        var txns = new List<BudgetTransaction>
        {
            Exp(4500, "Shopping", new DateTime(2026, 7, 10)), // only 1 of the 3 months has data
        };
        var result = await Run(txns, new List<ExistingBudget>(), start, end);
        var rec = result.Recommendations.Single();
        if (rec.HistoricalAverage == 1500m) // 4500 / 3, diluted
            Report.Warn("Category with data in only 1 of 3 requested months",
                $"historical average reported as Rs {rec.HistoricalAverage} (= 4500/3), noticeably lower than the true single-month figure of Rs 4,500 — " +
                "same averaging-dilution pattern re-confirmed across Days 12-14. See validation report Finding #2.");
        else
            Report.Check("Historical average is NOT diluted by empty months in the window", rec.HistoricalAverage == 4500m);
    }
}
