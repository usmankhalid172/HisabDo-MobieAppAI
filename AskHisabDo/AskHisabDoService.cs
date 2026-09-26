namespace HisabDo.Forecasting;

public sealed class AskHisabDoService(
    IFinancialRepository financialRepository,
    IBudgetRepository budgetRepository,
    ForecastingService forecastingService,
    ILogger<AskHisabDoService> logger)
{
    public async Task<AskHisabDoResponse> BuildContextAsync(
        AskHisabDoRequest request,
        Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, authenticatedUserId);
        var fromUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        IReadOnlyList<FinancialTransaction> transactions;
        IReadOnlyList<Budget> budgets;
        try
        {
            var transactionsTask = financialRepository.GetTransactionsAsync(
                authenticatedUserId, fromUtc, toUtc, cancellationToken);
            var budgetsTask = budgetRepository.GetBudgetsAsync(authenticatedUserId, cancellationToken);
            await Task.WhenAll(transactionsTask, budgetsTask);
            transactions = (await transactionsTask)
                .Where(x => x.UserId == authenticatedUserId && x.Amount >= 0m)
                .Where(x => x.DateUtc >= fromUtc && x.DateUtc < toUtc)
                .ToList();
            budgets = (await budgetsTask)
                .Where(x => x.UserId == authenticatedUserId && x.MonthlyLimit >= 0m)
                .ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to build financial context for {UserId}", authenticatedUserId);
            throw new ForecastUnavailableException("Financial data could not be loaded.");
        }

        var income = transactions.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
        var expenses = transactions.Where(x => x.Type == TransactionType.Expense).ToList();
        var expense = expenses.Sum(x => x.Amount);
        var categorySpending = expenses
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Category!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount), StringComparer.OrdinalIgnoreCase);
        var availability = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["income"] = transactions.Any(x => x.Type == TransactionType.Income) ? "available" : "unavailable",
            ["expenses"] = expenses.Count > 0 ? "available" : "unavailable",
            ["categorySpending"] = categorySpending.Count > 0 ? "available" : "unavailable",
            ["budgets"] = budgets.Count > 0 ? "available" : "unavailable"
        };

        ForecastResult? forecast = null;
        try
        {
            forecast = await forecastingService.ForecastAsync(
                new ForecastRequest(authenticatedUserId, request.From, request.To, ForecastFrequency.Monthly, 3),
                authenticatedUserId, cancellationToken);
            availability["cashFlowForecast"] = "available";
        }
        catch (Exception exception) when (exception is ForecastValidationException or ForecastUnavailableException or ForecastEngineException)
        {
            availability["cashFlowForecast"] = "unavailable_insufficient_data";
        }

        var anomalies = FindAnomalies(expenses);
        availability["anomalies"] = anomalies.Count > 0 ? "available" : "no_anomalies";
        var recommendations = CreateRecommendations(expenses, budgets, request.From, request.To);
        availability["budgetRecommendations"] = budgets.Count > 0 ? "available" : "unavailable";
        var health = CreateHealthScore(income, expense, categorySpending, budgets);
        availability["financialHealthScore"] = transactions.Count > 0 ? "available" : "unavailable";

        var context = new VerifiedFinancialContext(
            request.From,
            request.To,
            income,
            expense,
            income - expense,
            categorySpending,
            budgets,
            health,
            anomalies,
            forecast,
            recommendations,
            availability,
            $"verified_for_user:{authenticatedUserId};source:financial-repository;filtered:authenticated-user-id");
        return new AskHisabDoResponse(authenticatedUserId, request.Question, "ready", context);
    }

    private static IReadOnlyList<SpendingAnomaly> FindAnomalies(IReadOnlyList<FinancialTransaction> expenses)
    {
        return expenses
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Category!, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var typical = group.Average(x => x.Amount);
                return group.Where(x => x.Amount > typical * 1.5m)
                    .Select(x => new SpendingAnomaly(group.Key, x.Amount, decimal.Round(typical, 2),
                        DateOnly.FromDateTime(x.DateUtc), "Transaction is materially above the category average."));
            })
            .ToList();
    }

    private static IReadOnlyList<BudgetRecommendation> CreateRecommendations(
        IReadOnlyList<FinancialTransaction> expenses,
        IReadOnlyList<Budget> budgets,
        DateOnly from,
        DateOnly to)
    {
        var months = Math.Max(1, (to.Year - from.Year) * 12 + to.Month - from.Month + 1);
        return budgets.Select(budget =>
        {
            var average = decimal.Round(expenses.Where(x => string.Equals(x.Category, budget.Category, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount) / months, 2);
            var recommended = decimal.Round(average * 1.1m, 2);
            var reason = average > budget.MonthlyLimit ? "Increase coverage for observed spending." : "Budget is aligned with observed spending.";
            return new BudgetRecommendation(budget.Category, budget.MonthlyLimit, average, recommended, reason);
        }).ToList();
    }

    private static FinancialHealthScore CreateHealthScore(
        decimal income,
        decimal expense,
        IReadOnlyDictionary<string, decimal> categorySpending,
        IReadOnlyList<Budget> budgets)
    {
        var savingsRate = income == 0m ? 0m : (income - expense) / income;
        var score = (int)Math.Clamp(50m + savingsRate * 50m, 0m, 100m);
        var factors = new List<string>();
        if (income == 0m) factors.Add("No income data is available.");
        if (expense > income) { score = Math.Max(0, score - 20); factors.Add("Expenses exceed income."); }
        if (budgets.Count == 0) factors.Add("No budget data is available.");
        if (categorySpending.Count > 0 && budgets.Count > 0) factors.Add("Category spending was compared with budgets.");
        if (factors.Count == 0) factors.Add("Income and spending are available for analysis.");
        return new FinancialHealthScore(score, score >= 70 ? "healthy" : score >= 40 ? "needs_attention" : "at_risk", factors);
    }

    private static void ValidateRequest(AskHisabDoRequest request, Guid authenticatedUserId)
    {
        if (authenticatedUserId == Guid.Empty || request.UserId != authenticatedUserId)
            throw new ForecastValidationException("Invalid user ID.");
        if (request.From == default || request.To == default || request.From > request.To)
            throw new ForecastValidationException("The date range is invalid.");
    }
}
