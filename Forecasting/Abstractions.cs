namespace HisabDo.Forecasting;

public interface IFinancialRepository
{
    Task<IReadOnlyList<FinancialTransaction>> GetTransactionsAsync(
        Guid userId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<decimal?> GetOpeningBalanceAsync(
        Guid userId, DateTime fromUtc, CancellationToken cancellationToken);
}

public interface IForecastingEngine
{
    Task<ForecastEngineResult> ForecastAsync(
        IReadOnlyList<ForecastInput> history,
        ForecastFrequency frequency,
        int periods,
        CancellationToken cancellationToken);
}

public interface IBudgetRepository
{
    Task<IReadOnlyList<Budget>> GetBudgetsAsync(
        Guid userId, CancellationToken cancellationToken);
}