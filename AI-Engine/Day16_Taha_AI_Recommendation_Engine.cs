using System;
using System.Collections.Generic;
using System.Linq;

namespace HisabDo.AI.Day16;

public enum RecommendationType
{
    SpendingReduction, BudgetAdjustment, SavingImprovement, CashFlowRisk,
    UnusualSpendingReview, RecurringExpenseReview,
    FinancialHealthImprovement, CategorySpendingControl
}
public enum RecommendationPriority { High, Medium, Low }
public enum RecommendationSeverity { Critical, High, Medium, Low }

public sealed record VerifiedRecommendationInput
{
    public required string UserId { get; init; }
    public double? FinancialHealthScore { get; init; }
    public double? SavingRatePercent { get; init; }
    public double? ExpenseGrowthPercent { get; init; }
    public IReadOnlyList<VerifiedSpendingPattern> SpendingPatterns { get; init; } = Array.Empty<VerifiedSpendingPattern>();
    public IReadOnlyList<VerifiedAnomaly> Anomalies { get; init; } = Array.Empty<VerifiedAnomaly>();
    public double? ExpectedIncome { get; init; }
    public double? ExpectedExpense { get; init; }
    public double? ExpectedSaving { get; init; }
    public double? ExpectedClosingBalance { get; init; }
    public string? ForecastConfidence { get; init; }
    public IReadOnlyList<VerifiedBudgetStatus> BudgetStatuses { get; init; } = Array.Empty<VerifiedBudgetStatus>();
    public IReadOnlyList<VerifiedBudgetRecommendation> BudgetRecommendations { get; init; } = Array.Empty<VerifiedBudgetRecommendation>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}
public sealed record VerifiedSpendingPattern
{
    public required string Category { get; init; }
    public double CurrentAmount { get; init; }
    public double? PercentageChange { get; init; }
    public bool IsUnusual { get; init; }
    public bool IsRecurringCandidate { get; init; }
}
public sealed record VerifiedAnomaly
{
    public required string Type { get; init; }
    public required string Severity { get; init; }
    public required string Category { get; init; }
    public double Amount { get; init; }
}
public sealed record VerifiedBudgetStatus
{
    public required string Category { get; init; }
    public double ActualExpense { get; init; }
    public double? UtilizationPercent { get; init; }
    public bool IsOverBudget { get; init; }
}
public sealed record VerifiedBudgetRecommendation
{
    public required string Category { get; init; }
    public double RecommendedBudget { get; init; }
    public string Reason { get; init; } = "";
}
public sealed record Recommendation
{
    public required RecommendationType Type { get; init; }
    public required RecommendationPriority Priority { get; init; }
    public required RecommendationSeverity Severity { get; init; }
    public required string Category { get; init; }
    public required string Reason { get; init; }
    public required string Action { get; init; }
    public double? VerifiedAmount { get; init; }
    public double? VerifiedPercentage { get; init; }
}
public sealed record RecommendationResult
{
    public required string UserId { get; init; }
    public IReadOnlyList<Recommendation> Recommendations { get; init; } = Array.Empty<Recommendation>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Deterministic MVP engine. It consumes only verified Day 10–14 results.
/// Keep thresholds configurable in production.
/// </summary>
public sealed class TahaRecommendationEngine
{
    private const double LowSavingRate = 10.0;
    private const double HighExpenseGrowth = 20.0;
    private const double HighBudgetUtilization = 120.0;
    private const double CriticalBudgetUtilization = 150.0;

    public RecommendationResult Generate(VerifiedRecommendationInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        if (string.IsNullOrWhiteSpace(input.UserId))
            throw new InvalidOperationException("Authenticated UserId is required.");

        var r = new List<Recommendation>();

        if (input.FinancialHealthScore is <= 59)
            r.Add(new Recommendation {
                Type=RecommendationType.FinancialHealthImprovement, Priority=RecommendationPriority.High,
                Severity=RecommendationSeverity.High, Category="Overall",
                Reason=$"Verified Financial Health Score is {input.FinancialHealthScore}/100.",
                Action="Review the verified weak financial factors and address the highest-impact area.",
                VerifiedPercentage=input.FinancialHealthScore
            });

        if (input.SavingRatePercent is < LowSavingRate)
            r.Add(new Recommendation {
                Type=RecommendationType.SavingImprovement, Priority=RecommendationPriority.High,
                Severity=RecommendationSeverity.High, Category="Savings",
                Reason=$"Verified saving rate is {input.SavingRatePercent}%, below the MVP threshold of {LowSavingRate}%.",
                Action="Review high-spending categories and identify a practical saving opportunity.",
                VerifiedPercentage=input.SavingRatePercent
            });

        if (input.ExpenseGrowthPercent is >= HighExpenseGrowth)
            r.Add(new Recommendation {
                Type=RecommendationType.SpendingReduction, Priority=RecommendationPriority.High,
                Severity=RecommendationSeverity.High, Category="Overall Expenses",
                Reason=$"Verified expense growth is {input.ExpenseGrowthPercent}%, meeting the {HighExpenseGrowth}% threshold.",
                Action="Review categories with the largest verified increases.",
                VerifiedPercentage=input.ExpenseGrowthPercent
            });

        foreach (var p in input.SpendingPatterns)
        {
            if (p.IsUnusual)
                r.Add(new Recommendation {
                    Type=RecommendationType.UnusualSpendingReview, Priority=RecommendationPriority.High,
                    Severity=RecommendationSeverity.High, Category=p.Category,
                    Reason=$"Verified spending engine marked {p.Category} as unusual.",
                    Action="Review the recorded transactions in this category.",
                    VerifiedAmount=p.CurrentAmount, VerifiedPercentage=p.PercentageChange
                });

            if (p.IsRecurringCandidate)
                r.Add(new Recommendation {
                    Type=RecommendationType.RecurringExpenseReview, Priority=RecommendationPriority.Medium,
                    Severity=RecommendationSeverity.Medium, Category=p.Category,
                    Reason=$"Verified pattern engine identified a recurring-expense candidate in {p.Category}.",
                    Action="Review whether this recurring expense is still necessary and within budget.",
                    VerifiedAmount=p.CurrentAmount
                });

            if (p.PercentageChange is >= 20)
                r.Add(new Recommendation {
                    Type=RecommendationType.CategorySpendingControl, Priority=RecommendationPriority.Medium,
                    Severity=RecommendationSeverity.Medium, Category=p.Category,
                    Reason=$"Verified spending in {p.Category} increased by {p.PercentageChange}%.",
                    Action="Review the transactions driving this increase and consider spending controls.",
                    VerifiedAmount=p.CurrentAmount, VerifiedPercentage=p.PercentageChange
                });
        }

        foreach (var a in input.Anomalies)
        {
            bool high = a.Severity.Equals("High", StringComparison.OrdinalIgnoreCase) ||
                        a.Severity.Equals("Critical", StringComparison.OrdinalIgnoreCase);
            r.Add(new Recommendation {
                Type=RecommendationType.UnusualSpendingReview,
                Priority=high ? RecommendationPriority.High : RecommendationPriority.Medium,
                Severity=high ? RecommendationSeverity.High : RecommendationSeverity.Medium,
                Category=a.Category,
                Reason=$"Verified anomaly: {a.Type} ({a.Severity}).",
                Action="Review the transaction details and confirm whether the spending is expected.",
                VerifiedAmount=a.Amount
            });
        }

        if (input.ExpectedSaving is <= 0)
            r.Add(new Recommendation {
                Type=RecommendationType.CashFlowRisk, Priority=RecommendationPriority.High,
                Severity=RecommendationSeverity.High, Category="Cash Flow",
                Reason=$"Verified forecasted saving is {input.ExpectedSaving}.",
                Action="Review expected expenses and upcoming commitments before the forecast period.",
                VerifiedAmount=input.ExpectedSaving
            });

        foreach (var b in input.BudgetStatuses.Where(x => x.IsOverBudget))
        {
            double u = b.UtilizationPercent ?? 0;
            bool critical = u >= CriticalBudgetUtilization;
            r.Add(new Recommendation {
                Type=RecommendationType.BudgetAdjustment,
                Priority=critical ? RecommendationPriority.High : RecommendationPriority.Medium,
                Severity=critical ? RecommendationSeverity.High : RecommendationSeverity.Medium,
                Category=b.Category,
                Reason=$"Verified budget utilization for {b.Category} is {u}%.",
                Action="Review spending in this category and compare it with the existing budget.",
                VerifiedAmount=b.ActualExpense, VerifiedPercentage=u
            });
        }

        foreach (var b in input.BudgetRecommendations)
            r.Add(new Recommendation {
                Type=RecommendationType.BudgetAdjustment, Priority=RecommendationPriority.Low,
                Severity=RecommendationSeverity.Low, Category=b.Category,
                Reason=$"Verified budget engine recommends {b.RecommendedBudget} for {b.Category}. {b.Reason}",
                Action="Review the recommended budget against your current financial plan.",
                VerifiedAmount=b.RecommendedBudget
            });

        var ordered = r.OrderBy(x => PriorityRank(x.Priority))
                       .ThenBy(x => SeverityRank(x.Severity))
                       .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                       .ToList();

        return new RecommendationResult {
            UserId=input.UserId, Recommendations=ordered, Limitations=input.Limitations
        };
    }

    private static int PriorityRank(RecommendationPriority p) => p switch {
        RecommendationPriority.High => 1, RecommendationPriority.Medium => 2, _ => 3
    };
    private static int SeverityRank(RecommendationSeverity s) => s switch {
        RecommendationSeverity.Critical => 1, RecommendationSeverity.High => 2,
        RecommendationSeverity.Medium => 3, _ => 4
    };
}
