namespace HisabDo.Forecasting;

public sealed class BudgetAnalysisService(
    IFinancialRepository financialRepository,
    IBudgetRepository budgetRepository,
    ILogger<BudgetAnalysisService> logger)
{
    private const int MinimumMonths = 3;

    public async Task<BudgetAnalysisResult> AnalyzeAsync(
        BudgetAnalysisRequest request,
        Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request, authenticatedUserId);
        var fromUtc = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        try
        {
            var transactionsTask = financialRepository.GetTransactionsAsync(
                authenticatedUserId, fromUtc, toUtc, cancellationToken);
            var budgetsTask = budgetRepository.GetBudgetsAsync(authenticatedUserId, cancellationToken);
            await Task.WhenAll(transactionsTask, budgetsTask);

            var transactions = (await transactionsTask)
                .Where(x => x.UserId == authenticatedUserId)
                .Where(x => x.Amount >= 0m)
                .Where(x => x.DateUtc >= fromUtc && x.DateUtc < toUtc)
                .ToList();
            var expenses = transactions
                .Where(x => x.Type == TransactionType.Expense && !string.IsNullOrWhiteSpace(x.Category))
                .ToList();
            var totalIncome = transactions.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
            var totalExpense = expenses.Sum(x => x.Amount);
            var budgets = (await budgetsTask)
                .Where(x => x.UserId == authenticatedUserId && x.MonthlyLimit >= 0m)
                .ToDictionary(x => x.Category, StringComparer.OrdinalIgnoreCase);

            var months = MonthsBetween(request.From, request.To);
            var monthsWithData = transactions.Select(x => new DateOnly(x.DateUtc.Year, x.DateUtc.Month, 1)).Distinct().Count();
            if (monthsWithData < MinimumMonths || budgets.Count == 0)
                return new(request.UserId, request.From, request.To, "insufficient_data", monthsWithData, totalIncome, totalExpense, []);

            var categories = budgets.Keys
                .Union(expenses.Select(x => x.Category!), StringComparer.OrdinalIgnoreCase)
                .Select(category => CreateCategoryResult(
                    category,
                    expenses.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase)).ToList(),
                    budgets,
                    months))
                .ToList();
            return new(request.UserId, request.From, request.To, "ready", monthsWithData, totalIncome, totalExpense, categories);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unable to analyze budget for {UserId}", authenticatedUserId);
            throw new ForecastUnavailableException("Budget data could not be loaded.");
        }
    }

    private static CategoryBudgetAnalysis CreateCategoryResult(
        string category,
        IReadOnlyList<FinancialTransaction> transactions,
        IReadOnlyDictionary<string, Budget> budgets,
        int months)
    {
        var average = decimal.Round(transactions.Sum(x => x.Amount) / months, 2);
        var current = budgets.TryGetValue(category, out var budget) ? budget.MonthlyLimit : 0m;
        var utilization = current == 0m ? (average == 0m ? 0m : 100m) : decimal.Round(average / current * 100m, 2);
        var recommended = decimal.Round(average * 1.1m, 2);
        var needsAdjustment = current == 0m || utilization > 100m || utilization < 50m;
        var reason = current == 0m ? "No budget exists." : utilization > 100m ? "Average spending exceeds the budget." : utilization < 50m ? "Budget is materially underused." : "Budget is performing within range.";
        return new(category, average, current, utilization, recommended, needsAdjustment, reason);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        (to.Year - from.Year) * 12 + to.Month - from.Month + 1;

    private static void ValidateRequest(BudgetAnalysisRequest request, Guid authenticatedUserId)
    {
        if (authenticatedUserId == Guid.Empty || request.UserId != authenticatedUserId)
            throw new ForecastValidationException("Invalid user ID.");
        if (request.From == default || request.To == default || request.From > request.To)
            throw new ForecastValidationException("The date range is invalid.");
    }
}