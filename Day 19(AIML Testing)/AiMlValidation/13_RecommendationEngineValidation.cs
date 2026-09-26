using System;
using System.Collections.Generic;
using System.Linq;
using HisabDo.AI.Day16;

namespace HisabDo.AI.Validation;

public static class RecommendationEngineValidation
{
    private static readonly TahaRecommendationEngine Engine = new();

    private static VerifiedRecommendationInput Base(string userId = "user-rec-test") => new()
    {
        UserId = userId,
    };

    public static void RunAll()
    {
        Report.Suite("RECOMMENDATION ENGINE — TahaRecommendationEngine (Day 16)");
        FinancialHealthThresholdTests();
        SavingRateThresholdTests();
        ExpenseGrowthThresholdTests();
        BudgetUtilizationThresholdTests();
        CashFlowRiskThresholdTest();
        HealthyEmptyInputTest();
        InsufficientDataLimitationsPassthroughTest();
        OrderingTest();
        SeverityCriticalNeverAssignedTest();
        MultiSourceSameCategoryTest();
    }

    private static void FinancialHealthThresholdTests()
    {
        Report.Info("--- Financial Health Score threshold (<=59 triggers) ---");
        var at59 = Engine.Generate(Base() with { FinancialHealthScore = 59 });
        Report.Check("Score exactly 59 (boundary, inclusive) triggers FinancialHealthImprovement",
            at59.Recommendations.Any(r => r.Type == RecommendationType.FinancialHealthImprovement));

        var at60 = Engine.Generate(Base() with { FinancialHealthScore = 60 });
        Report.Check("Score exactly 60 (just above boundary) does NOT trigger",
            !at60.Recommendations.Any(r => r.Type == RecommendationType.FinancialHealthImprovement));

        var atZero = Engine.Generate(Base() with { FinancialHealthScore = 0 });
        Report.Check("Score of 0 (worst case) still triggers correctly, no crash",
            atZero.Recommendations.Any(r => r.Type == RecommendationType.FinancialHealthImprovement));

        var atMax = Engine.Generate(Base() with { FinancialHealthScore = 100 });
        Report.Check("Score of 100 (best case) never triggers",
            !atMax.Recommendations.Any(r => r.Type == RecommendationType.FinancialHealthImprovement));
    }

    private static void SavingRateThresholdTests()
    {
        Report.Info("--- Saving Rate threshold (<10% triggers) ---");
        var at10 = Engine.Generate(Base() with { SavingRatePercent = 10 });
        Report.Check("Saving rate exactly 10% (boundary) does NOT trigger (rule is strictly '<', not '<=')",
            !at10.Recommendations.Any(r => r.Type == RecommendationType.SavingImprovement));

        var at999 = Engine.Generate(Base() with { SavingRatePercent = 9.99 });
        Report.Check("Saving rate 9.99% (just below boundary) DOES trigger",
            at999.Recommendations.Any(r => r.Type == RecommendationType.SavingImprovement));

        var negative = Engine.Generate(Base() with { SavingRatePercent = -15 });
        Report.Check("Negative saving rate (spending more than earning) triggers correctly, no crash",
            negative.Recommendations.Any(r => r.Type == RecommendationType.SavingImprovement));
    }

    private static void ExpenseGrowthThresholdTests()
    {
        Report.Info("--- Expense Growth threshold (>=20% triggers) ---");
        var at20 = Engine.Generate(Base() with { ExpenseGrowthPercent = 20 });
        Report.Check("Expense growth exactly 20% (boundary, inclusive) triggers SpendingReduction",
            at20.Recommendations.Any(r => r.Type == RecommendationType.SpendingReduction));

        var at1999 = Engine.Generate(Base() with { ExpenseGrowthPercent = 19.99 });
        Report.Check("Expense growth 19.99% (just below boundary) does NOT trigger",
            !at1999.Recommendations.Any(r => r.Type == RecommendationType.SpendingReduction));

        var negative = Engine.Generate(Base() with { ExpenseGrowthPercent = -10 });
        Report.Check("Negative expense growth (spending decreased) does NOT trigger",
            !negative.Recommendations.Any(r => r.Type == RecommendationType.SpendingReduction));
    }

    private static void BudgetUtilizationThresholdTests()
    {
        Report.Info("--- Budget Utilization thresholds (>=120% Medium, >=150% High) ---");

        var at120 = Engine.Generate(Base() with { BudgetStatuses = new[] { new VerifiedBudgetStatus { Category = "Food", ActualExpense = 1200, UtilizationPercent = 120, IsOverBudget = true } } });
        var rec120 = at120.Recommendations.FirstOrDefault(r => r.Type == RecommendationType.BudgetAdjustment);
        Report.Check("120% utilization -> Medium priority (not yet Critical-tier)", rec120?.Priority == RecommendationPriority.Medium, $"got {rec120?.Priority}");

        var at150 = Engine.Generate(Base() with { BudgetStatuses = new[] { new VerifiedBudgetStatus { Category = "Food", ActualExpense = 1500, UtilizationPercent = 150, IsOverBudget = true } } });
        var rec150 = at150.Recommendations.FirstOrDefault(r => r.Type == RecommendationType.BudgetAdjustment);
        Report.Check("150% utilization (boundary, inclusive) -> High priority", rec150?.Priority == RecommendationPriority.High, $"got {rec150?.Priority}");

        var at149 = Engine.Generate(Base() with { BudgetStatuses = new[] { new VerifiedBudgetStatus { Category = "Food", ActualExpense = 1490, UtilizationPercent = 149, IsOverBudget = true } } });
        var rec149 = at149.Recommendations.FirstOrDefault(r => r.Type == RecommendationType.BudgetAdjustment);
        Report.Check("149% utilization (just below Critical boundary) -> still Medium", rec149?.Priority == RecommendationPriority.Medium, $"got {rec149?.Priority}");

        var notOver = Engine.Generate(Base() with { BudgetStatuses = new[] { new VerifiedBudgetStatus { Category = "Food", ActualExpense = 800, UtilizationPercent = 80, IsOverBudget = false } } });
        Report.Check("Under-budget category (IsOverBudget=false) never generates a BudgetAdjustment recommendation from the status list",
            !notOver.Recommendations.Any(r => r.Type == RecommendationType.BudgetAdjustment));
    }

    private static void CashFlowRiskThresholdTest()
    {
        Report.Info("--- Cash-Flow Risk threshold (ExpectedSaving <= 0 triggers) ---");
        var zero = Engine.Generate(Base() with { ExpectedSaving = 0 });
        Report.Check("Expected saving exactly 0 (boundary, inclusive) triggers CashFlowRisk",
            zero.Recommendations.Any(r => r.Type == RecommendationType.CashFlowRisk));

        var slightlyPositive = Engine.Generate(Base() with { ExpectedSaving = 1 });
        Report.Check("Expected saving of just 1 (barely positive) does NOT trigger",
            !slightlyPositive.Recommendations.Any(r => r.Type == RecommendationType.CashFlowRisk));

        var deeplyNegative = Engine.Generate(Base() with { ExpectedSaving = -50000 });
        Report.Check("Deeply negative expected saving triggers correctly, no crash", deeplyNegative.Recommendations.Any(r => r.Type == RecommendationType.CashFlowRisk));
    }

    private static void HealthyEmptyInputTest()
    {
        Report.Info("--- Healthy input: every value within normal range -> zero recommendations ---");
        var healthy = Engine.Generate(Base() with
        {
            FinancialHealthScore = 88, SavingRatePercent = 32, ExpenseGrowthPercent = 4, ExpectedSaving = 15000,
            SpendingPatterns = new[] { new VerifiedSpendingPattern { Category = "Groceries", CurrentAmount = 8000, PercentageChange = 2, IsUnusual = false, IsRecurringCandidate = false } },
            BudgetStatuses = new[] { new VerifiedBudgetStatus { Category = "Groceries", ActualExpense = 8000, UtilizationPercent = 80, IsOverBudget = false } },
        });
        Report.Check("Genuinely healthy input produces zero recommendations, not empty-but-fabricated ones", healthy.Recommendations.Count == 0, $"got {healthy.Recommendations.Count}");
    }

    private static void InsufficientDataLimitationsPassthroughTest()
    {
        Report.Info("--- Insufficient data: nulls everywhere -> zero recommendations, limitations preserved ---");
        var noData = Engine.Generate(Base() with
        {
            Limitations = new[] { "No verified financial health result is available for this user yet." },
        });
        Report.Check("No verified data anywhere -> zero recommendations (never invents a rule trigger from a null)", noData.Recommendations.Count == 0);
        Report.Check("Limitations from the input are passed through unchanged to the output", noData.Limitations.SequenceEqual(new[] { "No verified financial health result is available for this user yet." }));
    }

    private static void OrderingTest()
    {
        Report.Info("--- Deterministic ordering: Priority, then Severity, then Category (alphabetical) ---");
        var input = Base() with
        {
            ExpectedSaving = -1000, // High/High, Category "Cash Flow"
            FinancialHealthScore = 40, // High/High, Category "Overall"
            BudgetStatuses = new[]
            {
                new VerifiedBudgetStatus { Category = "Zebra", ActualExpense = 100, UtilizationPercent = 121, IsOverBudget = true }, // Medium/Medium
                new VerifiedBudgetStatus { Category = "Apple", ActualExpense = 100, UtilizationPercent = 121, IsOverBudget = true }, // Medium/Medium
            },
        };
        var result = Engine.Generate(input);
        var priorities = result.Recommendations.Select(r => r.Priority).ToList();
        Report.Check("High-priority items all appear before Medium-priority items",
            priorities.SkipWhile(p => p == RecommendationPriority.High).All(p => p != RecommendationPriority.High));

        var mediumItems = result.Recommendations.Where(r => r.Priority == RecommendationPriority.Medium).Select(r => r.Category).ToList();
        Report.Check("Within the same priority+severity tier, categories are sorted alphabetically (Apple before Zebra)",
            mediumItems.IndexOf("Apple") < mediumItems.IndexOf("Zebra"), $"order was: {string.Join(",", mediumItems)}");
    }

    private static void SeverityCriticalNeverAssignedTest()
    {
        Report.Info("--- RecommendationSeverity.Critical is defined but never assigned ---");
        // Sweep every trigger path at its most extreme input and confirm
        // none of them ever produces Severity.Critical, even though the
        // enum defines it and a 150%+ utilization case conceptually reads
        // as "critical" to a human.
        var extreme = Engine.Generate(Base() with
        {
            FinancialHealthScore = 0,
            SavingRatePercent = -100,
            ExpenseGrowthPercent = 500,
            ExpectedSaving = -1_000_000,
            SpendingPatterns = new[] { new VerifiedSpendingPattern { Category = "Food", CurrentAmount = 99999, PercentageChange = 900, IsUnusual = true, IsRecurringCandidate = true } },
            Anomalies = new[] { new VerifiedAnomaly { Type = "Unusual Amount", Severity = "Critical", Category = "Food", Amount = 99999 } },
            BudgetStatuses = new[] { new VerifiedBudgetStatus { Category = "Food", ActualExpense = 99999, UtilizationPercent = 999, IsOverBudget = true } },
        });
        var anyCritical = extreme.Recommendations.Any(r => r.Severity == RecommendationSeverity.Critical);
        if (anyCritical)
            Report.Check("Critical severity is assignable under extreme input", true);
        else
            Report.Warn("Severity.Critical is defined in the enum but no code path ever assigns it",
                "Even feeding the engine a Severity=\"Critical\" verified anomaly, a 999% budget utilization, and every other threshold at its most extreme, " +
                "every recommendation still comes back High/Medium/Low. Worth confirming this is intentional (reserved for a future rule) — currently a 121% " +
                "and a 999% budget overrun both read as the same 'High' severity to the end user.");
    }

    private static void MultiSourceSameCategoryTest()
    {
        Report.Info("--- Two independent verified sources flagging the same category (expected behavior, not a dedup bug) ---");
        var input = Base() with
        {
            SpendingPatterns = new[] { new VerifiedSpendingPattern { Category = "Food", CurrentAmount = 9000, PercentageChange = 28, IsUnusual = true, IsRecurringCandidate = false } },
            Anomalies = new[] { new VerifiedAnomaly { Type = "Unusual Amount", Severity = "High", Category = "Food", Amount = 9000 } },
        };
        var result = Engine.Generate(input);
        var foodUnusual = result.Recommendations.Count(r => r.Category == "Food" && r.Type == RecommendationType.UnusualSpendingReview);
        Report.Check("Two independent verified sources about the same category correctly produce two separate recommendations (not silently merged/deduped)",
            foodUnusual == 2, $"got {foodUnusual}");
    }
}
