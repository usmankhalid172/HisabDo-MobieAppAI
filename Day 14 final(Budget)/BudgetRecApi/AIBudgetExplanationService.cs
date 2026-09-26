using System;
using System.Collections.Generic;

namespace HisabDo.AI.Day14;

public sealed record VerifiedBudgetRecommendation(
    string Category,
    decimal? HistoricalAverage,
    decimal? ExistingBudget,
    decimal? RecommendedBudget,
    decimal? BudgetVariance,
    decimal? BudgetUtilizationPercent,
    string RecommendationReason,
    string DataSufficiency,
    IReadOnlyList<string> Limitations);

public sealed record BudgetAIExplanation(
    string Summary,
    string RecommendedBudget,
    string WhyRecommendation,
    string Overspending,
    string SavingsOpportunity,
    string Action,
    string Limitations);

public sealed class TahaAIBudgetExplanationService
{
    public BudgetAIExplanation BuildExplanation(VerifiedBudgetRecommendation r)
    {
        if (r is null)
            throw new ArgumentNullException(nameof(r));

        if (!r.RecommendedBudget.HasValue)
        {
            return new BudgetAIExplanation(
                Summary: "A complete budget recommendation is not available because verified backend data is missing.",
                RecommendedBudget: "Recommended budget is unavailable.",
                WhyRecommendation: "No recommendation reason is available.",
                Overspending: "Overspending status cannot be determined from the available verified data.",
                SavingsOpportunity: "No savings opportunity has been estimated.",
                Action: "Review the missing financial data before making a budget decision.",
                Limitations: BuildLimitations(r));
        }

        return new BudgetAIExplanation(
            Summary: $"Your recommended {r.Category} budget is {Money(r.RecommendedBudget.Value)} per month.",
            RecommendedBudget: $"The verified recommended budget for {r.Category} is {Money(r.RecommendedBudget.Value)}.",
            WhyRecommendation: string.IsNullOrWhiteSpace(r.RecommendationReason)
                ? "No verified recommendation reason is available."
                : r.RecommendationReason,
            Overspending: BuildOverspending(r),
            SavingsOpportunity: BuildSavingsOpportunity(r),
            Action: BuildAction(r),
            Limitations: BuildLimitations(r));
    }

    private static string BuildOverspending(VerifiedBudgetRecommendation r)
    {
        if (!r.ExistingBudget.HasValue || !r.HistoricalAverage.HasValue)
            return "Overspending cannot be determined because the required verified comparison values are unavailable.";

        if (r.HistoricalAverage.Value > r.ExistingBudget.Value)
            return $"{r.Category} is above the existing budget based on the verified historical spending average.";

        return $"{r.Category} is not above the existing budget based on the verified historical spending average.";
    }

    private static string BuildSavingsOpportunity(VerifiedBudgetRecommendation r)
    {
        if (!r.BudgetVariance.HasValue)
            return "No verified savings/headroom amount is available.";

        if (r.BudgetVariance.Value > 0)
            return $"The verified budget comparison shows {Money(r.BudgetVariance.Value)} of budget headroom.";

        if (r.BudgetVariance.Value < 0)
            return "There is no positive budget headroom in the verified comparison; spending is above the existing budget.";

        return "The verified comparison shows no budget variance.";
    }

    private static string BuildAction(VerifiedBudgetRecommendation r)
    {
        if (r.DataSufficiency.Equals("Limited", StringComparison.OrdinalIgnoreCase))
            return "Treat this recommendation as an initial estimate and review it when more transaction history is available.";

        if (r.ExistingBudget.HasValue &&
            r.HistoricalAverage.HasValue &&
            r.HistoricalAverage.Value > r.ExistingBudget.Value)
            return "Review this category and identify practical ways to keep spending closer to the planned budget.";

        return "Compare the recommendation with your actual spending as new transactions are recorded.";
    }

    private static string BuildLimitations(VerifiedBudgetRecommendation r)
    {
        var text = "Budget recommendations are estimates based on verified historical data and may change as spending behavior changes.";

        if (r.Limitations.Count > 0)
            text += " " + string.Join(" ", r.Limitations);

        if (!string.IsNullOrWhiteSpace(r.DataSufficiency) &&
            !r.DataSufficiency.Equals("Sufficient", StringComparison.OrdinalIgnoreCase))
            text += $" Data sufficiency: {r.DataSufficiency}.";

        return text;
    }

    private static string Money(decimal value) => $"PKR {value:N0}";
}

// LLM integration rule:
// Send only verified BudgetAIExplanation / VerifiedBudgetRecommendation.
// The LLM may improve wording, but must not:
// - invent financial amounts;
// - calculate a replacement recommendation;
// - invent historical facts;
// - invent savings;
// - change backend values.
// If a field is null, explain that it is unavailable.
