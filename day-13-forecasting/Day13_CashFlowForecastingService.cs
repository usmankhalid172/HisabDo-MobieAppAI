using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.CashFlowForecasting;

public enum TransactionType { Income, Expense }

public sealed record CashFlowTransaction(
    Guid UserId,
    Guid TransactionId,
    TransactionType Type,
    decimal Amount,
    DateTime Date,
    string? Category = null,
    string? Description = null);

public sealed record MonthlyCashFlow(
    DateOnly Month,
    decimal Income,
    decimal Expense)
{
    public decimal Saving => Income - Expense;
}

public sealed record CashFlowForecastResult(
    Guid UserId,
    int HistoryMonthsUsed,
    int ForecastHorizonMonths,
    decimal ExpectedIncome,
    decimal ExpectedExpense,
    decimal ExpectedSaving,
    decimal? OpeningBalance,
    decimal? ExpectedClosingBalance,
    string Confidence,
    string Method,
    IReadOnlyList<string> Limitations);

public interface ICashFlowTransactionRepository
{
    Task<IReadOnlyList<CashFlowTransaction>> GetTransactionsAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken);
}

public interface IOpeningBalanceProvider
{
    Task<decimal?> GetOpeningBalanceAsync(
        Guid userId, DateTime forecastStartUtc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Explainable Day 13 MVP baseline.
/// Replace repository/provider mappings with the real HisabDo .NET backend.
/// No LLM is used for numeric calculations.
/// </summary>
public sealed class CashFlowForecastService
{
    private const int MinHistoryMonths = 3;
    private const int PreferredHistoryMonths = 6;

    private readonly ICashFlowTransactionRepository _repository;
    private readonly IOpeningBalanceProvider? _balanceProvider;

    public CashFlowForecastService(
        ICashFlowTransactionRepository repository,
        IOpeningBalanceProvider? balanceProvider = null)
    {
        _repository = repository;
        _balanceProvider = balanceProvider;
    }

    public async Task<CashFlowForecastResult> ForecastNextMonthAsync(
        Guid userId,
        int historyMonths = PreferredHistoryMonths,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Valid userId is required.", nameof(userId));

        historyMonths = Math.Clamp(
            historyMonths, MinHistoryMonths, PreferredHistoryMonths);

        var today = DateTime.UtcNow.Date;
        var forecastStart = new DateTime(
            today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var historyStart = forecastStart.AddMonths(-historyMonths);

        var transactions = await _repository.GetTransactionsAsync(
            userId, historyStart, forecastStart, cancellationToken);

        var valid = transactions
            .Where(t => t.UserId == userId)       // user isolation
            .Where(t => t.Amount >= 0m)
            .Where(t => t.Date >= historyStart && t.Date < forecastStart)
            .ToList();

        var monthly = BuildMonthlySeries(valid, historyStart, historyMonths);
        var usableMonths = monthly.Count(m => m.Income != 0m || m.Expense != 0m);

        if (usableMonths == 0)
        {
            return new CashFlowForecastResult(
                userId, 0, 1, 0m, 0m, 0m, null, null,
                "Unavailable", "HistoricalBaseline",
                new[]
                {
                    "No usable historical financial transactions were available."
                });
        }

        var expectedIncome = ForecastIncome(monthly);
        var expectedExpense = ForecastExpense(monthly);
        var expectedSaving = expectedIncome - expectedExpense;

        decimal? openingBalance = null;
        decimal? expectedClosingBalance = null;

        if (_balanceProvider is not null)
        {
            openingBalance = await _balanceProvider.GetOpeningBalanceAsync(
                userId, forecastStart, cancellationToken);

            if (openingBalance.HasValue)
                expectedClosingBalance =
                    openingBalance.Value + expectedSaving;
        }

        var confidence = DetermineConfidence(monthly, usableMonths);

        var limitations = new List<string>
        {
            "Forecast is based on historical transaction behavior.",
            "Actual future income and expenses may differ from the forecast."
        };

        if (usableMonths < MinHistoryMonths)
            limitations.Add(
                "Historical data is below the preferred minimum of 3 usable months.");

        if (!openingBalance.HasValue)
            limitations.Add(
                "Closing balance forecast is unavailable because no trusted opening balance was supplied.");

        return new CashFlowForecastResult(
            userId,
            usableMonths,
            1,
            decimal.Round(expectedIncome, 2),
            decimal.Round(expectedExpense, 2),
            decimal.Round(expectedSaving, 2),
            openingBalance.HasValue
                ? decimal.Round(openingBalance.Value, 2) : null,
            expectedClosingBalance.HasValue
                ? decimal.Round(expectedClosingBalance.Value, 2) : null,
            confidence,
            "HistoricalBaseline",
            limitations);
    }

    private static List<MonthlyCashFlow> BuildMonthlySeries(
        IEnumerable<CashFlowTransaction> transactions,
        DateTime start,
        int months)
    {
        var grouped = transactions
            .GroupBy(t => new DateOnly(t.Date.Year, t.Date.Month, 1))
            .ToDictionary(
                g => g.Key,
                g => new MonthlyCashFlow(
                    g.Key,
                    g.Where(x => x.Type == TransactionType.Income)
                     .Sum(x => x.Amount),
                    g.Where(x => x.Type == TransactionType.Expense)
                     .Sum(x => x.Amount)));

        var result = new List<MonthlyCashFlow>();

        for (var i = 0; i < months; i++)
        {
            var date = start.AddMonths(i);
            var key = new DateOnly(date.Year, date.Month, 1);

            result.Add(grouped.TryGetValue(key, out var month)
                ? month
                : new MonthlyCashFlow(key, 0m, 0m));
        }

        return result;
    }

    private static decimal ForecastIncome(
        IReadOnlyList<MonthlyCashFlow> monthly)
    {
        var values = monthly
            .Where(m => m.Income > 0m)
            .Select(m => m.Income)
            .ToList();

        return values.Count == 0 ? 0m : values.Average();
    }

    private static decimal ForecastExpense(
        IReadOnlyList<MonthlyCashFlow> monthly)
    {
        return monthly.Count == 0 ? 0m : monthly.Average(m => m.Expense);
    }

    private static string DetermineConfidence(
        IReadOnlyList<MonthlyCashFlow> monthly,
        int usableMonths)
    {
        if (usableMonths < MinHistoryMonths)
            return "Low";

        if (usableMonths >= PreferredHistoryMonths)
        {
            var emptyMonths =
                monthly.Count(m => m.Income == 0m && m.Expense == 0m);

            return emptyMonths == 0 ? "High" : "Medium";
        }

        return "Medium";
    }
}
