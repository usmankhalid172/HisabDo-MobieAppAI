
using System;
using System.Collections.Generic;
using System.Linq;

namespace HisabDo.AI.Recommendations
{
    // Day 16 - Taha: Rule-Based AI Recommendation Engine
    // Golden rule:
    // Backend/calculation engine owns financial facts.
    // This engine converts VERIFIED results into deterministic recommendations.
    // LLM/AI must explain these recommendations; it must not invent or recalculate facts.

    public enum RecommendationCategory
    {
        SavingImprovement,
        SpendingReduction,
        BudgetAdjustment,
        CashFlowRisk,
        UnusualSpendingReview,
        FinancialHealthImprovement,
        CategorySpendingControl
    }

    public enum RecommendationPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    public enum RecommendationSeverity
    {
        Low = 1,
        Medium = 2,
        High = 3
    }

    public sealed class FinancialHealthInput
    {
        public double Score { get; init; }
        public double SavingRate { get; init; }
        public double ExpenseGrowthPercent { get; init; }
    }

    public sealed class SpendingPatternInput
    {
        public string Category { get; init; } = "";
        public double CurrentAmount { get; init; }
        public double PreviousAmount { get; init; }
        public double ChangePercent { get; init; }
        public double CategorySharePercent { get; init; }
        public bool IsUnusual { get; init; }
        public bool IsRecurring { get; init; }
    }

    public sealed class AnomalyInput
    {
        public string Category { get; init; } = "";
        public string Type { get; init; } = "";
        public string Severity { get; init; } = "Low";
        public double Amount { get; init; }
    }

    public sealed class CashFlowInput
    {
        public double ExpectedIncome { get; init; }
        public double ExpectedExpense { get; init; }
        public double ExpectedSaving { get; init; }
        public bool IsVerifiedForecast { get; init; }
    }

    public sealed class BudgetInput
    {
        public string Category { get; init; } = "";
        public double Budget { get; init; }
        public double ActualExpense { get; init; }
        public double UtilizationPercent { get; init; }
        public bool IsVerified { get; init; }
    }

    public sealed class Recommendation
    {
        public RecommendationCategory Category { get; init; }
        public RecommendationPriority Priority { get; init; }
        public RecommendationSeverity Severity { get; init; }
        public string Title { get; init; } = "";
        public string Reason { get; init; } = "";
        public string SuggestedAction { get; init; } = "";
        public string Source { get; init; } = "";
    }

    public sealed class RecommendationResult
    {
        public string UserId { get; init; } = "";
        public List<Recommendation> Recommendations { get; init; } = new();
    }

    public sealed class RecommendationInput
    {
        public string UserId { get; init; } = "";

        // All inputs below must already be verified by the backend/calculation services.
        public FinancialHealthInput? FinancialHealth { get; init; }
        public List<SpendingPatternInput> SpendingPatterns { get; init; } = new();
        public List<AnomalyInput> Anomalies { get; init; } = new();
        public CashFlowInput? CashFlowForecast { get; init; }
        public List<BudgetInput> Budgets { get; init; } = new();
    }

    public sealed class RecommendationEngine
    {
        // MVP thresholds. Keep configurable in production and validate with real HisabDo data.
        private const double LowSavingRate = 10.0;
        private const double HighExpenseGrowth = 20.0;
        private const double CategoryIncrease = 20.0;
        private const double HighBudgetUtilization = 120.0;
        private const double CriticalBudgetUtilization = 150.0;
        private const double LowHealthScore = 59.0;

        public RecommendationResult Generate(RecommendationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (string.IsNullOrWhiteSpace(input.UserId))
                throw new ArgumentException("UserId is required.", nameof(input));

            var result = new List<Recommendation>();

            // 1. Financial Health factors
            if (input.FinancialHealth != null)
            {
                if (input.FinancialHealth.Score <= LowHealthScore)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.FinancialHealthImprovement,
                        Priority = RecommendationPriority.High,
                        Severity = RecommendationSeverity.High,
                        Title = "Review overall financial health",
                        Reason = $"Verified financial health score is {input.FinancialHealth.Score:0.##}/100.",
                        SuggestedAction = "Review the main weak financial-health factors and prioritize corrective actions.",
                        Source = "Financial Health"
                    });
                }

                if (input.FinancialHealth.SavingRate < LowSavingRate)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.SavingImprovement,
                        Priority = RecommendationPriority.High,
                        Severity = RecommendationSeverity.High,
                        Title = "Improve saving behavior",
                        Reason = $"Verified saving rate is {input.FinancialHealth.SavingRate:0.##}%.",
                        SuggestedAction = "Review discretionary spending and identify realistic saving opportunities.",
                        Source = "Financial Health"
                    });
                }

                if (input.FinancialHealth.ExpenseGrowthPercent >= HighExpenseGrowth)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.SpendingReduction,
                        Priority = RecommendationPriority.High,
                        Severity = RecommendationSeverity.High,
                        Title = "Review high-growth spending",
                        Reason = $"Verified expense growth is {input.FinancialHealth.ExpenseGrowthPercent:0.##}%.",
                        SuggestedAction = "Review the categories contributing most to expense growth.",
                        Source = "Financial Health"
                    });
                }
            }

            // 2. Spending patterns
            foreach (var p in input.SpendingPatterns)
            {
                if (p.IsUnusual)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.UnusualSpendingReview,
                        Priority = RecommendationPriority.High,
                        Severity = RecommendationSeverity.High,
                        Title = $"Review unusual {p.Category} spending",
                        Reason = "The backend has verified this category as unusual.",
                        SuggestedAction = $"Review recent {p.Category} transactions and confirm whether the spending is expected.",
                        Source = "Spending Pattern"
                    });
                }

                if (p.IsRecurring)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.SpendingReduction,
                        Priority = RecommendationPriority.Medium,
                        Severity = RecommendationSeverity.Medium,
                        Title = $"Review recurring {p.Category} expense",
                        Reason = "The backend has verified a recurring spending pattern.",
                        SuggestedAction = $"Review whether the recurring {p.Category} expense is necessary or can be reduced.",
                        Source = "Spending Pattern"
                    });
                }

                if (p.ChangePercent >= CategoryIncrease)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.CategorySpendingControl,
                        Priority = RecommendationPriority.Medium,
                        Severity = RecommendationSeverity.Medium,
                        Title = $"Control {p.Category} spending growth",
                        Reason = $"Verified {p.Category} spending increased by {p.ChangePercent:0.##}%.",
                        SuggestedAction = $"Review what caused the increase in {p.Category} spending.",
                        Source = "Spending Pattern"
                    });
                }
            }

            // 3. Anomalies
            foreach (var a in input.Anomalies)
            {
                var severity = ParseSeverity(a.Severity);
                var priority = severity == RecommendationSeverity.High
                    ? RecommendationPriority.High
                    : RecommendationPriority.Medium;

                result.Add(new Recommendation
                {
                    Category = RecommendationCategory.UnusualSpendingReview,
                    Priority = priority,
                    Severity = severity,
                    Title = $"Review {a.Type} anomaly",
                    Reason = $"A verified {a.Type} anomaly was detected in {a.Category}.",
                    SuggestedAction = $"Review the related {a.Category} transaction(s) and confirm the activity.",
                    Source = "Anomaly Detection"
                });
            }

            // 4. Cash-flow forecast
            if (input.CashFlowForecast?.IsVerifiedForecast == true &&
                input.CashFlowForecast.ExpectedSaving <= 0)
            {
                result.Add(new Recommendation
                {
                    Category = RecommendationCategory.CashFlowRisk,
                    Priority = RecommendationPriority.High,
                    Severity = RecommendationSeverity.High,
                    Title = "Review projected cash-flow risk",
                    Reason = $"Verified forecast shows expected saving of {input.CashFlowForecast.ExpectedSaving:0.##}.",
                    SuggestedAction = "Review projected expenses and identify actions that may improve future cash flow.",
                    Source = "Cash-Flow Forecast"
                });
            }

            // 5. Budget utilization
            foreach (var b in input.Budgets.Where(x => x.IsVerified))
            {
                if (b.UtilizationPercent >= CriticalBudgetUtilization)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.BudgetAdjustment,
                        Priority = RecommendationPriority.High,
                        Severity = RecommendationSeverity.High,
                        Title = $"Review {b.Category} budget",
                        Reason = $"Verified budget utilization is {b.UtilizationPercent:0.##}%.",
                        SuggestedAction = $"Review {b.Category} spending and whether the budget needs adjustment.",
                        Source = "Budget"
                    });
                }
                else if (b.UtilizationPercent > HighBudgetUtilization)
                {
                    result.Add(new Recommendation
                    {
                        Category = RecommendationCategory.BudgetAdjustment,
                        Priority = RecommendationPriority.Medium,
                        Severity = RecommendationSeverity.Medium,
                        Title = $"Review {b.Category} budget",
                        Reason = $"Verified budget utilization is {b.UtilizationPercent:0.##}%.",
                        SuggestedAction = $"Review {b.Category} spending and budget usage.",
                        Source = "Budget"
                    });
                }
            }

            // 6. Ranking: priority desc, severity desc, then category/title for deterministic output.
            var ranked = result
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.Severity)
                .ThenBy(x => x.Category.ToString())
                .ThenBy(x => x.Title)
                .ToList();

            return new RecommendationResult
            {
                UserId = input.UserId,
                Recommendations = ranked
            };
        }

        private static RecommendationSeverity ParseSeverity(string? value)
        {
            return value?.Trim().ToLowerInvariant() switch
            {
                "high" => RecommendationSeverity.High,
                "medium" => RecommendationSeverity.Medium,
                _ => RecommendationSeverity.Low
            };
        }
    }

    // Example validation dataset:
    // SavingRate=6%, ExpenseGrowth=25%, Food Change=28%,
    // Food Budget Utilization=138%, Food anomaly=High,
    // ExpectedSaving=-5000.
    //
    // Expected recommendation categories:
    // - SavingImprovement (High)
    // - SpendingReduction (High)
    // - CategorySpendingControl (Medium)
    // - BudgetAdjustment (Medium)
    // - UnusualSpendingReview (High)
    // - CashFlowRisk (High)
}
