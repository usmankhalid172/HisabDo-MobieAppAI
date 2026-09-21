namespace HisabDo.Forecasting;

public sealed class RecommendationService(AskHisabDoService askHisabDoService)
{
    public async Task<RecommendationResponse> GenerateAsync(
        RecommendationRequest request,
        Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        var contextResponse = await askHisabDoService.BuildContextAsync(
            new AskHisabDoRequest(request.UserId, request.From, request.To),
            authenticatedUserId,
            cancellationToken);
        var context = contextResponse.Context;
        var recommendations = new List<Recommendation>();
        var limitations = new List<string>();

        AddFinancialHealthRule(context, recommendations, limitations);
        AddSavingsRule(context, recommendations, limitations);
        AddSpendingRules(context, recommendations, limitations);
        AddAnomalyRules(context, recommendations);
        AddCashFlowRules(context, recommendations, limitations);
        AddBudgetRules(context, recommendations, limitations);

        var ordered = recommendations
            .OrderBy(x => PriorityRank(x.Priority))
            .ThenBy(x => SeverityRank(x.Severity))
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new RecommendationResponse(
            authenticatedUserId,
            ordered.Count == 0 ? "no_recommendations" : "ready",
            ordered,
            limitations.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            context.DataAvailability);
    }

    private static void AddFinancialHealthRule(
        VerifiedFinancialContext context,
        ICollection<Recommendation> recommendations,
        ICollection<string> limitations)
    {
        if (context.DataAvailability["financialHealthScore"] != "available")
        {
            limitations.Add("Financial health recommendations require income or expense data.");
            return;
        }

        if (context.HealthScore.Score < 40)
            recommendations.Add(new(
                RecommendationType.FinancialHealth,
                RecommendationPriority.High,
                RecommendationSeverity.Critical,
                "Overall",
                $"Financial health score is {context.HealthScore.Score}/100.",
                "Address the highest-impact factors in the financial health result.",
                Percentage: context.HealthScore.Score));
        else if (context.HealthScore.Score < 70)
            recommendations.Add(new(
                RecommendationType.FinancialHealth,
                RecommendationPriority.Medium,
                RecommendationSeverity.Medium,
                "Overall",
                $"Financial health score is {context.HealthScore.Score}/100.",
                "Review the financial health factors and improve the weakest area.",
                Percentage: context.HealthScore.Score));
    }

    private static void AddSavingsRule(
        VerifiedFinancialContext context,
        ICollection<Recommendation> recommendations,
        ICollection<string> limitations)
    {
        if (context.TotalIncome <= 0m)
        {
            limitations.Add("Savings recommendations require income data.");
            return;
        }

        var savingRate = decimal.Round(context.NetCashFlow / context.TotalIncome * 100m, 2);
        if (savingRate < 10m)
            recommendations.Add(new(
                RecommendationType.Savings,
                RecommendationPriority.High,
                RecommendationSeverity.High,
                "Savings",
                $"Current saving rate is {savingRate}%.",
                "Reduce avoidable spending and set a realistic savings target.",
                Percentage: savingRate));
    }

    private static void AddSpendingRules(
        VerifiedFinancialContext context,
        ICollection<Recommendation> recommendations,
        ICollection<string> limitations)
    {
        if (context.DataAvailability["categorySpending"] != "available")
        {
            limitations.Add("Spending recommendations require categorized expense data.");
            return;
        }

        foreach (var spending in context.CategorySpending.OrderByDescending(x => x.Value))
        {
            var budget = context.Budgets.FirstOrDefault(x =>
                string.Equals(x.Category, spending.Key, StringComparison.OrdinalIgnoreCase));
            if (budget is not null && budget.MonthlyLimit > 0m && spending.Value > budget.MonthlyLimit)
                recommendations.Add(new(
                    RecommendationType.SpendingControl,
                    RecommendationPriority.Medium,
                    RecommendationSeverity.Medium,
                    spending.Key,
                    $"Spending of {spending.Value:0.##} exceeds the budget of {budget.MonthlyLimit:0.##}.",
                    "Review transactions in this category and reduce non-essential spending.",
                    spending.Value,
                    decimal.Round(spending.Value / budget.MonthlyLimit * 100m, 2)));
        }
    }

    private static void AddAnomalyRules(
        VerifiedFinancialContext context,
        ICollection<Recommendation> recommendations)
    {
        foreach (var anomaly in context.Anomalies)
            recommendations.Add(new(
                RecommendationType.AnomalyReview,
                RecommendationPriority.High,
                RecommendationSeverity.High,
                anomaly.Category,
                anomaly.Reason,
                "Review the transaction and confirm whether it is expected.",
                anomaly.Amount));
    }

    private static void AddCashFlowRules(
        VerifiedFinancialContext context,
        ICollection<Recommendation> recommendations,
        ICollection<string> limitations)
    {
        var forecast = context.CashFlowForecast;
        if (forecast is null || forecast.Expected.Count == 0)
        {
            limitations.Add("Cash-flow recommendations require a forecast with sufficient history.");
            return;
        }

        var period = forecast.Expected[0];
        if (period.Savings <= 0m)
            recommendations.Add(new(
                RecommendationType.CashFlow,
                RecommendationPriority.High,
                RecommendationSeverity.High,
                "Cash Flow",
                $"Forecasted savings for {period.Period} are {period.Savings:0.##}.",
                "Review upcoming expenses and commitments before the forecast period.",
                period.Savings));
        if (period.ClosingBalance is < 0m)
            recommendations.Add(new(
                RecommendationType.CashFlow,
                RecommendationPriority.High,
                RecommendationSeverity.Critical,
                "Cash Flow",
                $"Forecasted closing balance for {period.Period} is {period.ClosingBalance:0.##}.",
                "Prioritize essential payments and create a plan to avoid a negative balance.",
                period.ClosingBalance));
    }

    private static void AddBudgetRules(
        VerifiedFinancialContext context,
        ICollection<Recommendation> recommendations,
        ICollection<string> limitations)
    {
        if (context.DataAvailability["budgets"] != "available")
        {
            limitations.Add("Budget recommendations require budget data.");
            return;
        }

        foreach (var budget in context.Budgets)
        {
            var spending = context.CategorySpending.TryGetValue(budget.Category, out var value) ? value : 0m;
            if (budget.MonthlyLimit > 0m && spending > budget.MonthlyLimit * 1.2m)
                recommendations.Add(new(
                    RecommendationType.Budget,
                    RecommendationPriority.High,
                    RecommendationSeverity.High,
                    budget.Category,
                    $"Spending is {decimal.Round(spending / budget.MonthlyLimit * 100m, 2)}% of the budget.",
                    "Adjust spending or review the budget for this category.",
                    spending,
                    decimal.Round(spending / budget.MonthlyLimit * 100m, 2)));
        }

        foreach (var recommendation in context.BudgetRecommendations.Where(x =>
                     x.AverageMonthlySpend > 0m && x.RecommendedBudget != x.CurrentBudget))
            recommendations.Add(new(
                RecommendationType.BudgetAdjustment,
                RecommendationPriority.Low,
                RecommendationSeverity.Low,
                recommendation.Category,
                recommendation.Reason,
                "Review the recommended budget against your financial plan.",
                recommendation.RecommendedBudget));
    }

    private static int PriorityRank(RecommendationPriority priority) => priority switch
    {
        RecommendationPriority.High => 1,
        RecommendationPriority.Medium => 2,
        _ => 3
    };

    private static int SeverityRank(RecommendationSeverity severity) => severity switch
    {
        RecommendationSeverity.Critical => 1,
        RecommendationSeverity.High => 2,
        RecommendationSeverity.Medium => 3,
        _ => 4
    };
}
