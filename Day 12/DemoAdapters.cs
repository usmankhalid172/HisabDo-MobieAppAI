using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day12.AnomalyDetection;

/// <summary>
/// Demo IExpenseTransactionRepository so the anomaly detection endpoint can
/// actually be run and its output verified end-to-end. Seed data is
/// designed to realistically trigger all four anomaly types when queried
/// for the demo user over 2026-09-01..2026-09-30 (see HOW_TO_RUN.md).
/// </summary>
internal sealed class DemoExpenseTransactionRepository : IExpenseTransactionRepository
{
    public const string DemoUserId = "demo-user-001";

    private static readonly List<ExpenseTransaction> Transactions = BuildSeedData();

    public Task<IReadOnlyList<ExpenseTransaction>> GetExpensesAsync(
        string userId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default)
    {
        var result = Transactions
            .Where(x => x.UserId == userId && x.Date >= startUtc && x.Date < endUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<ExpenseTransaction>>(result);
    }

    private static List<ExpenseTransaction> BuildSeedData()
    {
        var list = new List<ExpenseTransaction>();
        int id = 1;
        string NextId() => $"tx-{id++:D4}";

        // --- Historical baseline: June, July, August 2026 ---
        // Food: regular grocery-run-sized transactions, several per month.
        decimal[] foodAmounts = { 800, 900, 750, 850, 700, 950, 820, 880 };
        var foodDates = new[] {
            new DateTime(2026,6,3), new DateTime(2026,6,17), new DateTime(2026,7,2),
            new DateTime(2026,7,16), new DateTime(2026,7,30), new DateTime(2026,8,5),
            new DateTime(2026,8,19), new DateTime(2026,8,28),
        };
        for (int i = 0; i < foodAmounts.Length; i++)
            list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
                foodAmounts[i], "Food", foodDates[i], "Groceries"));

        // Transport: small regular fares, historical only (no current-period spike planned).
        decimal[] transportAmounts = { 300, 320, 280, 310, 295, 305 };
        var transportDates = new[] {
            new DateTime(2026,6,10), new DateTime(2026,6,24), new DateTime(2026,7,8),
            new DateTime(2026,7,22), new DateTime(2026,8,12), new DateTime(2026,8,26),
        };
        for (int i = 0; i < transportAmounts.Length; i++)
            list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
                transportAmounts[i], "Transport", transportDates[i], "Ride fare"));

        // Bills: steady monthly amount, historical only.
        foreach (var d in new[] { new DateTime(2026,6,5), new DateTime(2026,7,5), new DateTime(2026,8,5) })
            list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
                4500, "Bills", d, "Utilities"));

        // --- Current period: September 2026 ---

        // Normal food spending (should NOT be flagged) — within the historical pattern.
        list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
            830, "Food", new DateTime(2026, 9, 4), "Groceries"));

        // Unusual Amount: one Food transaction far above the ~825 category average
        // (threshold = max(825*2=1650, 825+2*sd) — 9000 clears it easily).
        list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
            9000, "Food", new DateTime(2026, 9, 10), "Large grocery + electronics run"));

        // Unusual Category / spike-eligible: Transport spend far above its ~300 historical
        // per-transaction pattern and above its historical daily-spend baseline.
        list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
            2500, "Transport", new DateTime(2026, 9, 15), "Airport trip"));

        // Bills: normal, matches historical pattern (should NOT be flagged).
        list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
            4600, "Bills", new DateTime(2026, 9, 5), "Utilities"));

        // Duplicate Transaction candidate: two identical-looking Shopping transactions
        // on the same day (new category this period — no historical Shopping data at all,
        // so this also exercises the "new category" path from Section 7.3 of the spec).
        list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
            1200, "Shopping", new DateTime(2026, 9, 20), "Shoes - City Mall"));
        list.Add(new ExpenseTransaction(DemoUserId, NextId(), TransactionType.Expense,
            1200, "Shopping", new DateTime(2026, 9, 20), "Shoes - City Mall"));

        return list;
    }
}
