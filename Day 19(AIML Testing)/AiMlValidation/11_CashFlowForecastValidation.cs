using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HisabDo.AI.CashFlowForecasting;

namespace HisabDo.AI.Validation;

internal sealed class InMemoryCashFlowRepo : ICashFlowTransactionRepository
{
    private readonly List<CashFlowTransaction> _all;
    public InMemoryCashFlowRepo(List<CashFlowTransaction> all) => _all = all;

    public Task<IReadOnlyList<CashFlowTransaction>> GetTransactionsAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var result = _all.Where(t => t.UserId == userId && t.Date >= fromUtc && t.Date < toUtc).ToList();
        return Task.FromResult<IReadOnlyList<CashFlowTransaction>>(result);
    }
}

internal sealed class FixedBalanceProvider : IOpeningBalanceProvider
{
    private readonly decimal? _balance;
    public FixedBalanceProvider(decimal? balance) => _balance = balance;
    public Task<decimal?> GetOpeningBalanceAsync(Guid userId, DateTime forecastStartUtc, CancellationToken cancellationToken)
        => Task.FromResult(_balance);
}

public static class CashFlowForecastValidation
{
    private static readonly Guid User = Guid.NewGuid();

    private static DateTime MonthStart(int monthsAgo)
    {
        var today = DateTime.UtcNow.Date;
        var firstOfThisMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return firstOfThisMonth.AddMonths(-monthsAgo);
    }

    private static CashFlowTransaction Tx(TransactionType type, decimal amount, int monthsAgo, int day = 10, string? category = null) =>
        new(User, Guid.NewGuid(), type, amount, MonthStart(monthsAgo).AddDays(day - 1), category);

    private static async Task<CashFlowForecastResult> Run(List<CashFlowTransaction> txns, int historyMonths = 6, decimal? openingBalance = null)
    {
        var service = new CashFlowForecastService(new InMemoryCashFlowRepo(txns), new FixedBalanceProvider(openingBalance));
        return await service.ForecastNextMonthAsync(User, historyMonths);
    }

    public static async Task RunAll()
    {
        Report.Suite("CASH-FLOW FORECAST — CashFlowForecastService (Day 13)");
        await NormalForecastTest();
        await ConfidenceBoundaryTests();
        await InsufficientDataTests();
        await ZeroIncomeAndNegativeValueTests();
        await IncreasingDecreasingTrendTest();
        await ExpenseAveragingAsymmetryTest();
    }

    private static async Task NormalForecastTest()
    {
        Report.Info("--- Normal 6-month forecast ---");
        var txns = new List<CashFlowTransaction>();
        var incomes = new[] { 50000m, 51000m, 52000m, 53000m, 54000m, 55000m };
        var expenses = new[] { 32000m, 33000m, 31000m, 34000m, 35000m, 33000m };
        for (var m = 6; m >= 1; m--)
        {
            txns.Add(Tx(TransactionType.Income, incomes[6 - m], m));
            txns.Add(Tx(TransactionType.Expense, expenses[6 - m], m, day: 20));
        }
        var result = await Run(txns, 6, openingBalance: 200000m);

        var expectedIncome = incomes.Average();
        var expectedExpense = expenses.Average();
        Report.Check("Normal: expected income matches manual average", Math.Abs(result.ExpectedIncome - expectedIncome) < 0.01m,
            $"got {result.ExpectedIncome}, expected {expectedIncome}");
        Report.Check("Normal: expected expense matches manual average", Math.Abs(result.ExpectedExpense - expectedExpense) < 0.01m,
            $"got {result.ExpectedExpense}, expected {expectedExpense}");
        Report.Check("Normal: confidence is High with 6 full months", result.Confidence == "High", $"got {result.Confidence}");
        Report.Check("Normal: closing balance computed when opening balance is trusted",
            result.ExpectedClosingBalance == 200000m + result.ExpectedSaving);
    }

    private static async Task ConfidenceBoundaryTests()
    {
        Report.Info("--- Confidence threshold boundary tests ---");

        List<CashFlowTransaction> NMonths(int n)
        {
            var list = new List<CashFlowTransaction>();
            for (var m = n; m >= 1; m--)
            {
                list.Add(Tx(TransactionType.Income, 50000m, m));
                list.Add(Tx(TransactionType.Expense, 30000m, m, day: 20));
            }
            return list;
        }

        var r3 = await Run(NMonths(3), historyMonths: 3);
        Report.Check("Boundary: exactly 3 usable months -> Medium (not Low)", r3.Confidence == "Medium", $"got {r3.Confidence}");

        var r6 = await Run(NMonths(6), historyMonths: 6);
        Report.Check("Boundary: exactly 6 usable months, no gaps -> High", r6.Confidence == "High", $"got {r6.Confidence}");

        var withGap = NMonths(6).Where(t => !(t.Date >= MonthStart(3) && t.Date < MonthStart(2))).ToList();
        var rGap = await Run(withGap, historyMonths: 6);
        Report.Check("Boundary: 6-month window with 1 empty month -> Medium (downgraded from High)", rGap.Confidence == "Medium", $"got {rGap.Confidence}, usableMonths={rGap.HistoryMonthsUsed}");
    }

    private static async Task InsufficientDataTests()
    {
        Report.Info("--- Insufficient historical data tests ---");

        var oneMonth = new List<CashFlowTransaction> { Tx(TransactionType.Income, 50000m, 1), Tx(TransactionType.Expense, 30000m, 1, day: 20) };
        var r1 = await Run(oneMonth, historyMonths: 3);
        Report.Check("Insufficient: 1 usable month (<3) -> Low confidence, not a crash", r1.Confidence == "Low", $"got {r1.Confidence}");
        Report.Check("Insufficient: 1 usable month -> limitation about being below preferred minimum is included",
            r1.Limitations.Any(l => l.Contains("below the preferred minimum")));

        var none = new List<CashFlowTransaction>();
        var r2 = await Run(none, historyMonths: 6);
        Report.Check("Insufficient: zero transactions at all -> Confidence 'Unavailable', not a fabricated forecast",
            r2.Confidence == "Unavailable" && r2.ExpectedIncome == 0 && r2.ExpectedExpense == 0);
    }

    private static async Task ZeroIncomeAndNegativeValueTests()
    {
        Report.Info("--- Zero income / negative value tests ---");

        var noIncome = new List<CashFlowTransaction>();
        for (var m = 3; m >= 1; m--) noIncome.Add(Tx(TransactionType.Expense, 20000m, m, day: 20));
        var r1 = await Run(noIncome, historyMonths: 3);
        Report.Check("Zero income: expectedIncome is 0, not NaN/crash", r1.ExpectedIncome == 0m, $"got {r1.ExpectedIncome}");
        Report.Check("Zero income: expectedSaving is negative (all expense, no income)", r1.ExpectedSaving < 0);

        var withNegative = new List<CashFlowTransaction>
        {
            Tx(TransactionType.Income, 50000m, 1),
            Tx(TransactionType.Expense, 30000m, 1, day: 20),
            Tx(TransactionType.Expense, -5000m, 1, day: 25),
            Tx(TransactionType.Income, 50000m, 2),
            Tx(TransactionType.Expense, 30000m, 2, day: 20),
            Tx(TransactionType.Income, 50000m, 3),
            Tx(TransactionType.Expense, 30000m, 3, day: 20),
        };
        var r2 = await Run(withNegative, historyMonths: 3);
        Report.Check("Negative value: negative-amount transaction does not alter the expense average",
            r2.ExpectedExpense == 30000m, $"got {r2.ExpectedExpense} (would be 25000 if the -5000 were wrongly netted in)");
    }

    private static async Task IncreasingDecreasingTrendTest()
    {
        Report.Info("--- Increasing / decreasing expense trend tests ---");

        var increasing = new List<CashFlowTransaction>();
        var incExpenses = new[] { 20000m, 25000m, 30000m };
        for (var m = 3; m >= 1; m--) { increasing.Add(Tx(TransactionType.Income, 50000m, m)); increasing.Add(Tx(TransactionType.Expense, incExpenses[3 - m], m, day: 20)); }
        var rInc = await Run(increasing, historyMonths: 3);
        Report.Check("Increasing trend: forecast expense is the simple average, not the latest/highest value",
            rInc.ExpectedExpense == incExpenses.Average(),
            $"got {rInc.ExpectedExpense} — MVP uses a flat average, so a genuinely accelerating trend will UNDERESTIMATE next month's expense; worth flagging as a methodology limitation, not a bug");

        var decreasing = new List<CashFlowTransaction>();
        var decExpenses = new[] { 30000m, 25000m, 20000m };
        for (var m = 3; m >= 1; m--) { decreasing.Add(Tx(TransactionType.Income, 50000m, m)); decreasing.Add(Tx(TransactionType.Expense, decExpenses[3 - m], m, day: 20)); }
        var rDec = await Run(decreasing, historyMonths: 3);
        Report.Check("Decreasing trend: forecast expense is the simple average, not the latest/lowest value",
            rDec.ExpectedExpense == decExpenses.Average());
    }

    private static async Task ExpenseAveragingAsymmetryTest()
    {
        Report.Info("--- Re-confirming known issue: Income excludes zero months, Expense does not ---");
        var txns = new List<CashFlowTransaction>
        {
            Tx(TransactionType.Income, 50000m, 1), Tx(TransactionType.Expense, 30000m, 1, day: 20),
            Tx(TransactionType.Income, 50000m, 2), Tx(TransactionType.Expense, 30000m, 2, day: 20),
        };
        var result = await Run(txns, historyMonths: 3);
        Report.Check("Income average correctly divides by 2 (months with actual income), not 3", result.ExpectedIncome == 50000m, $"got {result.ExpectedIncome}");
        var expenseIfDilutedBy3 = 60000m / 3m;
        var expenseIfCorrectlyDividedBy2 = 60000m / 2m;
        if (Math.Abs(result.ExpectedExpense - expenseIfDilutedBy3) < 0.01m)
            Report.Warn("Expense average is diluted by the empty 3rd month",
                $"got {result.ExpectedExpense} (= 60000/3) instead of {expenseIfCorrectlyDividedBy2} (= 60000/2) — " +
                "Income and Expense use inconsistent averaging rules (Income skips zero months, Expense does not). Re-confirmed here under controlled conditions — see validation report Finding #2.");
        else
            Report.Check("Expense average matches the (2-month) income averaging convention", result.ExpectedExpense == expenseIfCorrectlyDividedBy2);
    }
}
