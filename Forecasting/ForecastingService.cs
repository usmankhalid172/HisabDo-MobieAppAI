namespace HisabDo.Forecasting;

public sealed class ForecastingService(
    IFinancialRepository repository,
    IForecastingEngine engine,
    ILogger<ForecastingService> logger)
{
    private const int MinimumUsablePeriods = 3;

    public async Task<ForecastResult> ForecastAsync(
        ForecastRequest request,
        Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, authenticatedUserId);

        var fromUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        IReadOnlyList<FinancialTransaction> transactions;

        try
        {
            transactions = await repository.GetTransactionsAsync(
                authenticatedUserId, fromUtc, toUtc, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to load financial history for {UserId}", authenticatedUserId);
            throw new ForecastUnavailableException("Financial history could not be loaded.");
        }

        var ownedTransactions = transactions
            .Where(transaction => transaction.UserId == authenticatedUserId)
            .Where(transaction => transaction.Amount >= 0m)
            .Where(transaction => transaction.DateUtc >= fromUtc && transaction.DateUtc < toUtc)
            .ToList();

        var openingBalance = await GetOpeningBalanceAsync(authenticatedUserId, fromUtc, cancellationToken);
        var history = BuildHistory(ownedTransactions, request, openingBalance);
        if (history.Count(x => x.Income != 0m || x.Expense != 0m) < MinimumUsablePeriods)
            throw new ForecastValidationException(
                $"At least {MinimumUsablePeriods} periods containing financial data are required.");

        ForecastEngineResult engineResult;
        try
        {
            engineResult = await engine.ForecastAsync(
                history.Select(ToInput).ToList(), request.Frequency, request.ForecastPeriods, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Forecasting engine failed for {UserId}", authenticatedUserId);
            throw new ForecastEngineException("The forecasting engine could not produce a result.", exception);
        }

        ValidateEngineResult(engineResult, request.ForecastPeriods);
        return new ForecastResult(authenticatedUserId, request.Frequency, history, engineResult.Periods.Select(ToPeriod).ToList());
    }

    private async Task<decimal?> GetOpeningBalanceAsync(Guid userId, DateTime fromUtc, CancellationToken cancellationToken)
    {
        try
        {
            return await repository.GetOpeningBalanceAsync(userId, fromUtc, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to load opening balance for {UserId}", userId);
            throw new ForecastUnavailableException("Balance data could not be loaded.");
        }
    }

    private static List<ForecastPeriod> BuildHistory(
        IEnumerable<FinancialTransaction> transactions,
        ForecastRequest request,
        decimal? openingBalance)
    {
        var grouped = transactions.GroupBy(x => PeriodStart(x.DateUtc, request.Frequency))
            .ToDictionary(group => group.Key, group => new
            {
                Income = group.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount),
                Expense = group.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount)
            });
        var result = new List<ForecastPeriod>();
        decimal? balance = openingBalance;
        for (var period = PeriodStart(request.From.ToDateTime(TimeOnly.MinValue), request.Frequency);
             period <= request.To.ToDateTime(TimeOnly.MinValue);
             period = NextPeriod(period, request.Frequency))
        {
            grouped.TryGetValue(period, out var values);
            var income = values?.Income ?? 0m;
            var expense = values?.Expense ?? 0m;
            var closing = balance.HasValue ? balance + income - expense : null;
            result.Add(new ForecastPeriod(Format(period, request.Frequency), income, expense, income - expense, balance, closing));
            balance = closing;
        }
        return result;
    }

    private static DateTime PeriodStart(DateTime date, ForecastFrequency frequency) => frequency switch
    {
        ForecastFrequency.Monthly => new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Utc),
        ForecastFrequency.Weekly => date.Date.AddDays(-(int)date.DayOfWeek),
        _ => throw new ForecastValidationException("Unsupported forecast frequency.")
    };

    private static DateTime NextPeriod(DateTime date, ForecastFrequency frequency) =>
        frequency == ForecastFrequency.Monthly ? date.AddMonths(1) : date.AddDays(7);

    private static string Format(DateTime date, ForecastFrequency frequency) =>
        frequency == ForecastFrequency.Monthly ? date.ToString("yyyy-MM") : date.ToString("yyyy-MM-dd");

    private static ForecastInput ToInput(ForecastPeriod period) =>
        new(period.Period, period.Income, period.Expense, period.Savings, period.OpeningBalance, period.ClosingBalance);

    private static ForecastPeriod ToPeriod(ForecastInput period) =>
        new(period.Period, period.Income, period.Expense, period.Savings, period.OpeningBalance, period.ClosingBalance);

    private static void ValidateRequest(ForecastRequest request, Guid authenticatedUserId)
    {
        if (authenticatedUserId == Guid.Empty) throw new ForecastValidationException("Authenticated user is required.");
        if (request.UserId == Guid.Empty || request.UserId != authenticatedUserId) throw new ForecastValidationException("Invalid user ID.");
        if (request.From == default || request.To == default) throw new ForecastValidationException("From and To dates are required.");
        if (request.From > request.To) throw new ForecastValidationException("The date range is invalid.");
        if (request.ForecastPeriods is < 1 or > 12) throw new ForecastValidationException("ForecastPeriods must be between 1 and 12.");
    }

    private static void ValidateEngineResult(ForecastEngineResult result, int expectedPeriods)
    {
        if (result.Periods is null || result.Periods.Count != expectedPeriods || result.Periods.Any(x => x.Income < 0m || x.Expense < 0m || x.Savings != x.Income - x.Expense))
            throw new ForecastEngineException("The forecasting engine returned an invalid response.");
    }
}