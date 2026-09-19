using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day14;

internal static class DemoUsers
{
    public static readonly Guid Normal = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid NoTransactions = Guid.Parse("77777777-7777-7777-7777-777777777777");
}

/// <summary>
/// Demo IBudgetRecommendationRepository so the endpoint can actually be
/// run and verified end-to-end. The "Normal" user's data is designed to
/// exercise every category-wise rule from spec Section 6: existing budget
/// below spending (Food), existing budget above spending (Transport), no
/// existing budget (Shopping), spending equal to budget (Bills), and a
/// zero-amount existing budget to test the divide-by-zero guard
/// (Entertainment).
/// </summary>
internal sealed class DemoBudgetRecommendationRepository : IBudgetRecommendationRepository
{
    private static readonly List<BudgetTransaction> Transactions = BuildTransactions();
    private static readonly List<ExistingBudget> Budgets = BuildBudgets();

    public Task<IReadOnlyList<BudgetTransaction>> GetExpensesAsync(
        Guid userId, DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var result = Transactions
            .Where(t => t.UserId == userId && t.Date >= start && t.Date <= end)
            .ToList();
        return Task.FromResult<IReadOnlyList<BudgetTransaction>>(result);
    }

    public Task<IReadOnlyList<ExistingBudget>> GetBudgetsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var result = Budgets.Where(b => b.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<ExistingBudget>>(result);
    }

    private static List<BudgetTransaction> BuildTransactions()
    {
        var list = new List<BudgetTransaction>();

        // Food: existing budget (12,000) below historical average -> "above existing budget" flag
        decimal[] food = { 14000, 15500, 15500 };
        // Transport: existing budget (6,000) above historical average -> comparison shown, no forced reduction
        decimal[] transport = { 4000, 4500, 4500 };
        // Shopping: no existing budget at all, and only has data in 2 of the 3 months
        decimal[] shopping = { 3000, 0, 6000 };
        // Bills: historical average lands exactly on the existing budget
        decimal[] bills = { 4500, 4500, 4500 };
        // Entertainment: existing budget is explicitly 0 -> tests the divide-by-zero guard on utilization
        decimal[] entertainment = { 1000, 1200, 800 };

        var months = new[] { 6, 7, 8 }; // Jun, Jul, Aug 2026

        void AddMonthly(string category, decimal[] amounts)
        {
            for (var i = 0; i < months.Length; i++)
            {
                if (amounts[i] <= 0) continue; // Shopping's zero month: genuinely no transaction that month
                list.Add(new BudgetTransaction
                {
                    UserId = DemoUsers.Normal,
                    Amount = amounts[i],
                    Category = category,
                    Date = new DateTime(2026, months[i], 15),
                    IsExpense = true,
                });
            }
        }

        AddMonthly("Food", food);
        AddMonthly("Transport", transport);
        AddMonthly("Shopping", shopping);
        AddMonthly("Bills", bills);
        AddMonthly("Entertainment", entertainment);

        // No-transactions user: intentionally empty.

        return list;
    }

    private static List<ExistingBudget> BuildBudgets()
    {
        return new List<ExistingBudget>
        {
            new() { UserId = DemoUsers.Normal, Category = "Food", Amount = 12000 },
            new() { UserId = DemoUsers.Normal, Category = "Transport", Amount = 6000 },
            new() { UserId = DemoUsers.Normal, Category = "Bills", Amount = 4500 },
            new() { UserId = DemoUsers.Normal, Category = "Entertainment", Amount = 0 },
            // Shopping intentionally has no budget row at all.
        };
    }
}
