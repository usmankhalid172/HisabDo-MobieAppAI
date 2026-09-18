using System;
using System.Collections.Generic;
using System.Linq;

public sealed record VerifiedCategorySpending(
    string Category,
    decimal Amount,
    decimal Percentage);

public sealed record VerifiedCategoryChange(
    string Category,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal ChangeAmount,
    decimal? ChangePercentage,
    string Trend);

public sealed record VerifiedSpendingResult(
    decimal TotalExpense,
    IReadOnlyList<VerifiedCategorySpending> TopCategories,
    IReadOnlyList<VerifiedCategoryChange> CategoryChanges);

public sealed record SpendingInsight(
    string Summary,
    IReadOnlyList<string> TopCategoryInsights,
    IReadOnlyList<string> ChangeInsights,
    IReadOnlyList<string> Actions);

public sealed class SpendingAiExplanationService
{
    public SpendingInsight BuildVerifiedInsight(
        VerifiedSpendingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var summary =
            $"Total verified spending for this period is PKR {result.TotalExpense:N2}.";

        var top = result.TopCategories
            .Take(5)
            .Select(x =>
                $"{x.Category} is a top spending category, " +
                $"accounting for {x.Percentage:N2}% of total spending " +
                $"(PKR {x.Amount:N2}).")
            .ToList();

        var changes = result.CategoryChanges
            .Where(x => x.Trend is "Increasing"
                             or "Decreasing"
                             or "New Category")
            .Select(BuildChangeInsight)
            .ToList();

        var actions = result.CategoryChanges
            .Where(x => x.Trend is "Increasing"
                             or "New Category")
            .Take(5)
            .Select(x =>
                x.Trend == "New Category"
                    ? $"Review {x.Category} spending to confirm whether it was planned or one-time."
                    : $"Review recent {x.Category} expenses to understand the spending increase.")
            .ToList();

        return new SpendingInsight(
            summary,
            top,
            changes,
            actions);
    }

    private static string BuildChangeInsight(
        VerifiedCategoryChange change)
    {
        return change.Trend switch
        {
            "Increasing" when change.ChangePercentage.HasValue =>
                $"{change.Category} spending increased by " +
                $"{change.ChangePercentage.Value:N2}% compared with the previous period.",

            "Decreasing" when change.ChangePercentage.HasValue =>
                $"{change.Category} spending decreased by " +
                $"{Math.Abs(change.ChangePercentage.Value):N2}% compared with the previous period.",

            "New Category" =>
                $"{change.Category} is a new spending category in the current period.",

            _ =>
                $"{change.Category} has a verified spending pattern."
        };
    }
}