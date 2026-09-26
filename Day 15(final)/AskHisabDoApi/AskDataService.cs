using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day15;

// NOTE FOR THE TEAM: IAskHisabDoDataService (Taha's interface) had NO
// implementation anywhere in the repo — this is it. In production this
// should call the ACTUAL Day 10 (financial health), Day 11 (spending
// patterns), Day 12 (anomaly), Day 13 (forecast) and Day 14 (budget
// recommendation) services already built and verified in those days'
// deliverables — not duplicate their logic. For this integration task
// (Day 15 is Laiba's API/UI integration work, not a rebuild of five
// other teammates' engines), this class reproduces the same MVP formulas
// from each of those specs directly over one shared demo dataset, so
// Ask HisabDo can be tested end-to-end without standing up five separate
// ASP.NET projects and wiring them together. Every number here is
// genuinely computed from the seeded data below, never hardcoded per
// question — but it is a stand-in, not the real integration.

public static class AskDemoUsers
{
    public static readonly Guid Primary = Guid.Parse("88888888-8888-8888-8888-888888888888");
}

internal sealed record DemoTxn(TransactionType Type, decimal Amount, string Category, DateTime Date);
internal enum TransactionType { Income, Expense }

public sealed record VerifiedValue(string Label, string Value);

public sealed class AskHisabDoDataService : IAskHisabDoDataService
{
    // --- Seed data -----------------------------------------------------
    // 3 full months of history (Jun-Aug 2026) plus one deliberately
    // anomalous Food transaction in August, and a Shopping expense with
    // NO budget on file (for the missing-data test case). September is
    // left empty on purpose so "this month" questions demonstrate the
    // no-data path honestly instead of always having something to show.
    private static readonly List<DemoTxn> Transactions = new()
    {
        new(TransactionType.Income, 50000, "Salary", new DateTime(2026, 6, 10)),
        new(TransactionType.Income, 50000, "Salary", new DateTime(2026, 7, 10)),
        new(TransactionType.Income, 50000, "Salary", new DateTime(2026, 8, 10)),

        new(TransactionType.Expense, 800, "Food", new DateTime(2026, 6, 15)),
        new(TransactionType.Expense, 850, "Food", new DateTime(2026, 7, 15)),
        new(TransactionType.Expense, 9000, "Food", new DateTime(2026, 8, 15)), // anomalous

        new(TransactionType.Expense, 3000, "Transport", new DateTime(2026, 6, 20)),
        new(TransactionType.Expense, 3200, "Transport", new DateTime(2026, 7, 20)),
        new(TransactionType.Expense, 3100, "Transport", new DateTime(2026, 8, 20)),

        new(TransactionType.Expense, 4500, "Bills", new DateTime(2026, 6, 5)),
        new(TransactionType.Expense, 4500, "Bills", new DateTime(2026, 7, 5)),
        new(TransactionType.Expense, 4500, "Bills", new DateTime(2026, 8, 5)),

        new(TransactionType.Expense, 2000, "Shopping", new DateTime(2026, 8, 12)), // no budget exists
    };

    private static readonly Dictionary<string, decimal> Budgets = new()
    {
        ["Food"] = 1000,
        ["Transport"] = 3500,
        ["Bills"] = 4500,
        // Shopping intentionally has no budget entry.
    };

    // --- Period resolution ----------------------------------------------
    private static (DateTime start, DateTime end, string label) ResolvePeriod(string question, DateTime nowUtc)
    {
        var q = question.ToLowerInvariant();
        var firstOfCurrentMonth = new DateTime(nowUtc.Year, nowUtc.Month, 1);

        if (q.Contains("this month"))
            return (firstOfCurrentMonth, nowUtc, firstOfCurrentMonth.ToString("MMMM yyyy"));

        // Default, and explicit "last month": the most recent complete calendar month.
        var lastMonthEnd = firstOfCurrentMonth.AddDays(-1);
        var lastMonthStart = new DateTime(lastMonthEnd.Year, lastMonthEnd.Month, 1);
        return (lastMonthStart, lastMonthEnd, lastMonthStart.ToString("MMMM yyyy"));
    }

    private static string? DetectCategory(string question)
    {
        var q = question.ToLowerInvariant();
        foreach (var category in new[] { "food", "transport", "bills", "shopping" })
            if (q.Contains(category)) return char.ToUpperInvariant(category[0]) + category[1..];
        return null;
    }

    public Task<VerifiedFinancialContext> GetVerifiedContextAsync(
        Guid userId, AskHisabDoIntent intent, string question, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow.Date;
        var context = intent switch
        {
            AskHisabDoIntent.IncomeExpense => IncomeExpense(question, now),
            AskHisabDoIntent.SpendingPattern => SpendingPattern(now),
            AskHisabDoIntent.Budget => Budget(question),
            AskHisabDoIntent.FinancialHealth => FinancialHealth(now),
            AskHisabDoIntent.Anomaly => Anomaly(now),
            AskHisabDoIntent.CashFlowForecast => CashFlowForecast(now),
            AskHisabDoIntent.BudgetRecommendation => BudgetRecommendation(question, now),
            _ => new VerifiedFinancialContext(intent, "", Array.Empty<string>(), false),
        };
        return Task.FromResult(context);
    }

    /// <summary>
    /// Structured values for the UI's "relevant verified financial values"
    /// display — not part of Taha's interface (VerifiedFinancialContext
    /// only carries the answer text), so the controller calls this
    /// directly on the concrete type after getting the final answer.
    /// </summary>
    public IReadOnlyList<VerifiedValue> BuildVerifiedValues(AskHisabDoIntent intent, string question)
    {
        var now = DateTime.UtcNow.Date;
        return intent switch
        {
            AskHisabDoIntent.IncomeExpense => IncomeExpenseValues(question, now),
            AskHisabDoIntent.SpendingPattern => SpendingPatternValues(now),
            AskHisabDoIntent.Budget => BudgetValues(question),
            AskHisabDoIntent.FinancialHealth => FinancialHealthValues(now),
            AskHisabDoIntent.Anomaly => AnomalyValues(now),
            AskHisabDoIntent.CashFlowForecast => CashFlowForecastValues(now),
            AskHisabDoIntent.BudgetRecommendation => BudgetRecommendationValues(question, now),
            _ => Array.Empty<VerifiedValue>(),
        };
    }

    // --- Income & Expense ------------------------------------------------
    private VerifiedFinancialContext IncomeExpense(string question, DateTime now)
    {
        var (start, end, label) = ResolvePeriod(question, now);
        var income = Transactions.Where(t => t.Type == TransactionType.Income && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
        var expense = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= start && t.Date <= end).Sum(t => t.Amount);

        if (income == 0 && expense == 0)
            return new VerifiedFinancialContext(AskHisabDoIntent.IncomeExpense, "", new[] { $"No verified transactions were found for {label}." }, false);

        return new VerifiedFinancialContext(
            AskHisabDoIntent.IncomeExpense,
            $"In {label} you earned Rs {income:N0} and spent Rs {expense:N0}, leaving Rs {income - expense:N0}.",
            Array.Empty<string>(), true);
    }

    private IReadOnlyList<VerifiedValue> IncomeExpenseValues(string question, DateTime now)
    {
        var (start, end, label) = ResolvePeriod(question, now);
        var income = Transactions.Where(t => t.Type == TransactionType.Income && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
        var expense = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
        return new[]
        {
            new VerifiedValue("Period", label),
            new VerifiedValue("Income", $"Rs {income:N0}"),
            new VerifiedValue("Expense", $"Rs {expense:N0}"),
            new VerifiedValue("Net", $"Rs {income - expense:N0}"),
        };
    }

    // --- Spending / Category ----------------------------------------------
    private static (DateTime start, DateTime end) LastMonthRange(DateTime now)
    {
        var firstOfCurrentMonth = new DateTime(now.Year, now.Month, 1);
        var end = firstOfCurrentMonth.AddDays(-1);
        return (new DateTime(end.Year, end.Month, 1), end);
    }

    private VerifiedFinancialContext SpendingPattern(DateTime now)
    {
        var (start, end) = LastMonthRange(now);
        var byCategory = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= start && t.Date <= end)
            .GroupBy(t => t.Category).Select(g => (Category: g.Key, Total: g.Sum(t => t.Amount)))
            .OrderByDescending(g => g.Total).ToList();

        if (!byCategory.Any())
            return new VerifiedFinancialContext(AskHisabDoIntent.SpendingPattern, "", new[] { "No verified expense transactions were found for the requested period." }, false);

        var top = byCategory.First();
        var totalExpense = byCategory.Sum(c => c.Total);
        var pct = totalExpense > 0 ? Math.Round(top.Total / totalExpense * 100, 1) : 0;
        return new VerifiedFinancialContext(
            AskHisabDoIntent.SpendingPattern,
            $"Your highest spending category in {start:MMMM yyyy} was {top.Category} at Rs {top.Total:N0} ({pct}% of total spending).",
            Array.Empty<string>(), true);
    }

    private IReadOnlyList<VerifiedValue> SpendingPatternValues(DateTime now)
    {
        var (start, end) = LastMonthRange(now);
        var byCategory = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= start && t.Date <= end)
            .GroupBy(t => t.Category).Select(g => (Category: g.Key, Total: g.Sum(t => t.Amount)))
            .OrderByDescending(g => g.Total).ToList();
        return byCategory.Select(c => new VerifiedValue(c.Category, $"Rs {c.Total:N0}")).ToList();
    }

    // --- Budget ------------------------------------------------------------
    private VerifiedFinancialContext Budget(string question)
    {
        var (start, end) = LastMonthRange(DateTime.UtcNow.Date);
        var category = DetectCategory(question);

        if (category != null)
        {
            if (!Budgets.TryGetValue(category, out var budget))
                return new VerifiedFinancialContext(AskHisabDoIntent.Budget, "", new[] { $"No budget exists for {category}." }, false);

            var spent = Transactions.Where(t => t.Type == TransactionType.Expense && t.Category == category && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
            var utilization = budget > 0 ? Math.Round(spent / budget * 100, 1) : 0;
            var status = spent > budget ? "over" : "within";
            return new VerifiedFinancialContext(
                AskHisabDoIntent.Budget,
                $"You are {status} your {category} budget: Rs {spent:N0} spent against a Rs {budget:N0} budget ({utilization}% utilization).",
                Array.Empty<string>(), true);
        }

        // No category detected — overall budget summary across all budgeted categories.
        var totalBudget = Budgets.Values.Sum();
        var totalSpent = Transactions.Where(t => t.Type == TransactionType.Expense && Budgets.ContainsKey(t.Category) && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
        var overallUtilization = totalBudget > 0 ? Math.Round(totalSpent / totalBudget * 100, 1) : 0;
        return new VerifiedFinancialContext(
            AskHisabDoIntent.Budget,
            $"Across your budgeted categories you used {overallUtilization}% of your budget in {start:MMMM yyyy}: Rs {totalSpent:N0} spent against Rs {totalBudget:N0} budgeted.",
            Array.Empty<string>(), true);
    }

    private IReadOnlyList<VerifiedValue> BudgetValues(string question)
    {
        var (start, end) = LastMonthRange(DateTime.UtcNow.Date);
        var category = DetectCategory(question);
        var categories = category != null ? new[] { category } : Budgets.Keys.ToArray();

        var values = new List<VerifiedValue>();
        foreach (var cat in categories)
        {
            if (!Budgets.TryGetValue(cat, out var budget)) continue;
            var spent = Transactions.Where(t => t.Type == TransactionType.Expense && t.Category == cat && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
            var utilization = budget > 0 ? Math.Round(spent / budget * 100, 1) : 0;
            values.Add(new VerifiedValue(cat, $"Rs {spent:N0} of Rs {budget:N0} ({utilization}%)"));
        }
        return values;
    }

    // --- Financial Health (simplified — Saving Behavior + Expense Control + Cash Flow only) ---
    private (decimal income, decimal expense, decimal savingRate, decimal expenseRatio) HealthInputs()
    {
        var (start, end) = ThreeMonthRange(DateTime.UtcNow.Date);
        var income = Transactions.Where(t => t.Type == TransactionType.Income && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
        var expense = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= start && t.Date <= end).Sum(t => t.Amount);
        var savingRate = income > 0 ? Math.Round((income - expense) / income * 100, 1) : 0;
        var expenseRatio = income > 0 ? Math.Round(expense / income * 100, 1) : 0;
        return (income, expense, savingRate, expenseRatio);
    }

    private static (DateTime start, DateTime end) ThreeMonthRange(DateTime now)
    {
        var (_, end) = LastMonthRange(now);
        return (new DateTime(end.Year, end.Month, 1).AddMonths(-2), end);
    }

    private VerifiedFinancialContext FinancialHealth(DateTime now)
    {
        var (income, expense, savingRate, expenseRatio) = HealthInputs();
        if (income == 0) return new VerifiedFinancialContext(AskHisabDoIntent.FinancialHealth, "", new[] { "No verified income data is available to compute a health snapshot." }, false);

        var savingScore = savingRate >= 25 ? 25 : savingRate >= 15 ? 18 : savingRate >= 5 ? 8 : 3;
        var expenseScore = expenseRatio <= 50 ? 20 : expenseRatio <= 70 ? 15 : expenseRatio <= 90 ? 7 : 3;
        var cashFlow = income - expense;
        var cashFlowScore = cashFlow > 0 ? 15 : cashFlow == 0 ? 7 : 3;
        var partialScore = savingScore + expenseScore + cashFlowScore;

        return new VerifiedFinancialContext(
            AskHisabDoIntent.FinancialHealth,
            $"Over the last 3 months your saving rate was {savingRate}% and your expense ratio was {expenseRatio}%, with positive cash flow of Rs {cashFlow:N0}. " +
            $"That gives a partial health snapshot of {partialScore}/60 across saving behavior, expense control and cash flow.",
            new[] { "This is a simplified 3-factor snapshot, not the full 6-factor Day 10 Financial Health Score (budget, debt/udhaar and expense growth aren't included here)." },
            true);
    }

    private IReadOnlyList<VerifiedValue> FinancialHealthValues(DateTime now)
    {
        var (income, expense, savingRate, expenseRatio) = HealthInputs();
        return new[]
        {
            new VerifiedValue("Saving Rate", $"{savingRate}%"),
            new VerifiedValue("Expense Ratio", $"{expenseRatio}%"),
            new VerifiedValue("Cash Flow (3 mo.)", $"Rs {income - expense:N0}"),
        };
    }

    // --- Anomaly (simplified — current vs. prior-2-month baseline per category) ---
    private (string category, decimal amount, decimal baseline, decimal deviationPct)? DetectFoodAnomaly(DateTime now)
    {
        var (_, end) = LastMonthRange(now);
        var currentMonthStart = new DateTime(end.Year, end.Month, 1);
        var baselineStart = currentMonthStart.AddMonths(-2);

        var currentByCategory = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= currentMonthStart && t.Date <= end)
            .GroupBy(t => t.Category).ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        var baselineByCategory = Transactions.Where(t => t.Type == TransactionType.Expense && t.Date >= baselineStart && t.Date < currentMonthStart)
            .GroupBy(t => t.Category).ToDictionary(g => g.Key, g => g.Average(t => t.Amount));

        foreach (var (category, amount) in currentByCategory)
        {
            if (!baselineByCategory.TryGetValue(category, out var baseline) || baseline <= 0) continue;
            var deviation = (amount - baseline) / baseline * 100;
            if (deviation >= 100) return (category, amount, baseline, Math.Round(deviation, 1));
        }
        return null;
    }

    private VerifiedFinancialContext Anomaly(DateTime now)
    {
        var found = DetectFoodAnomaly(now);
        if (found == null)
            return new VerifiedFinancialContext(AskHisabDoIntent.Anomaly, "No unusual expenses were detected in the most recent verified period.", Array.Empty<string>(), true);

        var (category, amount, baseline, deviationPct) = found.Value;
        var severity = deviationPct >= 300 ? "High" : deviationPct >= 100 ? "Medium" : "Low";
        return new VerifiedFinancialContext(
            AskHisabDoIntent.Anomaly,
            $"An unusual {category} expense of Rs {amount:N0} was detected — about {deviationPct}% above your typical Rs {baseline:N0} average. Severity: {severity}.",
            Array.Empty<string>(), true);
    }

    private IReadOnlyList<VerifiedValue> AnomalyValues(DateTime now)
    {
        var found = DetectFoodAnomaly(now);
        if (found == null) return new[] { new VerifiedValue("Anomalies found", "0") };
        var (category, amount, baseline, deviationPct) = found.Value;
        return new[]
        {
            new VerifiedValue("Category", category),
            new VerifiedValue("Amount", $"Rs {amount:N0}"),
            new VerifiedValue("Baseline average", $"Rs {baseline:N0}"),
            new VerifiedValue("Deviation", $"{deviationPct}%"),
        };
    }

    // --- Cash-Flow Forecast (simplified — 3-month average) ---
    private VerifiedFinancialContext CashFlowForecast(DateTime now)
    {
        var (start, end) = ThreeMonthRange(now);
        var months = Transactions.Where(t => t.Date >= start && t.Date <= end)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                Expense = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
            }).ToList();

        if (months.Count < 2)
            return new VerifiedFinancialContext(AskHisabDoIntent.CashFlowForecast, "", new[] { "Not enough historical data is available to forecast next month." }, false);

        var expectedIncome = Math.Round(months.Average(m => m.Income), 0);
        var expectedExpense = Math.Round(months.Average(m => m.Expense), 0);
        var expectedSaving = expectedIncome - expectedExpense;

        return new VerifiedFinancialContext(
            AskHisabDoIntent.CashFlowForecast,
            $"Based on {months.Count} months of history, next month's income is expected to be about Rs {expectedIncome:N0} against expenses of about Rs {expectedExpense:N0}, for an estimated saving of Rs {expectedSaving:N0}. " +
            "A closing balance isn't available without a trusted opening balance on file.",
            new[] { "This is a forecast, not an actual future value — treat it as an estimate." }, true);
    }

    private IReadOnlyList<VerifiedValue> CashFlowForecastValues(DateTime now)
    {
        var (start, end) = ThreeMonthRange(now);
        var months = Transactions.Where(t => t.Date >= start && t.Date <= end)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new { Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount), Expense = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount) })
            .ToList();
        if (!months.Any()) return Array.Empty<VerifiedValue>();
        var expectedIncome = Math.Round(months.Average(m => m.Income), 0);
        var expectedExpense = Math.Round(months.Average(m => m.Expense), 0);
        return new[]
        {
            new VerifiedValue("Expected Income", $"Rs {expectedIncome:N0}"),
            new VerifiedValue("Expected Expense", $"Rs {expectedExpense:N0}"),
            new VerifiedValue("Expected Saving", $"Rs {expectedIncome - expectedExpense:N0}"),
            new VerifiedValue("Closing Balance", "Not available"),
        };
    }

    // --- Budget Recommendation (simplified — 3-month category average) ---
    private VerifiedFinancialContext BudgetRecommendation(string question, DateTime now)
    {
        var (start, end) = ThreeMonthRange(now);
        var category = DetectCategory(question);
        var categories = category != null ? new[] { category } : Budgets.Keys.ToArray();

        var lines = new List<string>();
        foreach (var cat in categories)
        {
            var catTxns = Transactions.Where(t => t.Type == TransactionType.Expense && t.Category == cat && t.Date >= start && t.Date <= end).ToList();
            if (!catTxns.Any()) continue;
            var monthsSpanned = 3;
            var average = Math.Round(catTxns.Sum(t => t.Amount) / monthsSpanned, 0);
            var existing = Budgets.TryGetValue(cat, out var b) ? b : (decimal?)null;
            var comparison = existing.HasValue
                ? (average > existing.Value ? $"above your current Rs {existing.Value:N0} budget" : $"within your current Rs {existing.Value:N0} budget")
                : "there is no existing budget for comparison";
            lines.Add($"{cat}: recommended Rs {average:N0}/month ({comparison}).");
        }

        if (!lines.Any())
            return new VerifiedFinancialContext(AskHisabDoIntent.BudgetRecommendation, "", new[] { "No verified category spending history is available to base a recommendation on." }, false);

        var limitations = new List<string> { "Recommendations are estimates based on verified historical averages and may change as spending behavior changes." };
        if (categories.Contains("Food"))
            limitations.Add("The Food recommendation includes an unusually high transaction this period, which may skew it upward.");

        return new VerifiedFinancialContext(
            AskHisabDoIntent.BudgetRecommendation,
            string.Join(" ", lines),
            limitations, true);
    }

    private IReadOnlyList<VerifiedValue> BudgetRecommendationValues(string question, DateTime now)
    {
        var (start, end) = ThreeMonthRange(now);
        var category = DetectCategory(question);
        var categories = category != null ? new[] { category } : Budgets.Keys.ToArray();
        var values = new List<VerifiedValue>();
        foreach (var cat in categories)
        {
            var catTxns = Transactions.Where(t => t.Type == TransactionType.Expense && t.Category == cat && t.Date >= start && t.Date <= end).ToList();
            if (!catTxns.Any()) continue;
            var average = Math.Round(catTxns.Sum(t => t.Amount) / 3m, 0);
            values.Add(new VerifiedValue(cat, $"Rs {average:N0}/month recommended"));
        }
        return values;
    }
}
