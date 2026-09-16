namespace HisabDo.Forecasting;

internal sealed class DemoFinancialRepository : IFinancialRepository
{
    private static readonly Guid UserId = DemoAuthenticationHandler.DemoUserId;
    private static readonly IReadOnlyList<FinancialTransaction> Transactions =
    [
        new(UserId, Guid.NewGuid(), TransactionType.Income, 5000m, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Expense, 3200m, new DateTime(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Income, 5100m, new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Expense, 3300m, new DateTime(2026, 2, 18, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Income, 5200m, new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Expense, 3100m, new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Income, 5300m, new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Expense, 3400m, new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Income, 5400m, new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc)),
        new(UserId, Guid.NewGuid(), TransactionType.Expense, 3500m, new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc))
    ];

    public Task<IReadOnlyList<FinancialTransaction>> GetTransactionsAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<FinancialTransaction>>(
            Transactions.Where(x => x.UserId == userId && x.DateUtc >= fromUtc && x.DateUtc < toUtc).ToList());

    public Task<decimal?> GetOpeningBalanceAsync(
        Guid userId, DateTime fromUtc, CancellationToken cancellationToken)
        => Task.FromResult<decimal?>(userId == UserId ? 10000m : null);
}

internal sealed class DemoForecastingEngine : IForecastingEngine
{
    public Task<ForecastEngineResult> ForecastAsync(
        IReadOnlyList<ForecastInput> history,
        ForecastFrequency frequency,
        int periods,
        CancellationToken cancellationToken)
    {
        var income = history.Average(x => x.Income);
        var expense = history.Average(x => x.Expense);
        var savings = income - expense;
        var result = Enumerable.Range(1, periods)
            .Select(index => new ForecastInput(
                $"Forecast-{index}",
                decimal.Round(income, 2),
                decimal.Round(expense, 2),
                decimal.Round(savings, 2),
                null,
                null))
            .ToList();
        return Task.FromResult(new ForecastEngineResult(result));
    }
}