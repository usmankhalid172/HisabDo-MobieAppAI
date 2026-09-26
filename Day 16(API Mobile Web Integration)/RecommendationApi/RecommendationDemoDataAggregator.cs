using System;
using System.Collections.Generic;

namespace HisabDo.AI.Day16;

// NOTE FOR THE TEAM: TahaRecommendationEngine.Generate() takes an
// already-assembled VerifiedRecommendationInput — it doesn't fetch
// anything itself. Nothing in the repo builds that input from the real
// Day 10-14 services (Jaffer's task per spec Section 12: "connect
// verified Day 10-14 results, UserId isolation, recommendation API,
// missing-data handling"). This class is a demo stand-in for that
// aggregation step so the engine — which is exactly as Taha wrote it,
// completely unmodified — can be tested end-to-end. In production,
// replace this with real calls into the Day 10-14 services already built
// and verified across those days' deliverables.
//
// Three demo users are seeded to cover the three states this task
// explicitly asks to test:
//   - "AtRisk": triggers most recommendation types at once (multiple
//     recommendations, deterministic ordering, several verified sources
//     for the same category)
//   - "Healthy": every verified input is within normal range -> genuinely
//     empty recommendation list, no limitations
//   - "InsufficientData": no verified results available at all -> also an
//     empty recommendation list, but WITH limitations explaining why —
//     deliberately distinct from the "Healthy" empty state so the UI can
//     tell "nothing to flag" apart from "we don't know yet"

public static class RecDemoUsers
{
    public const string AtRisk = "99999999-9999-9999-9999-999999999999";
    public const string Healthy = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    public const string InsufficientData = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
}

public sealed class RecommendationDemoDataAggregator
{
    public VerifiedRecommendationInput BuildInput(string userId)
    {
        return userId switch
        {
            RecDemoUsers.AtRisk => AtRiskInput(),
            RecDemoUsers.Healthy => HealthyInput(),
            RecDemoUsers.InsufficientData => InsufficientDataInput(),
            _ => throw new InvalidOperationException($"No demo verified data is on file for user '{userId}'."),
        };
    }

    private static VerifiedRecommendationInput AtRiskInput() => new()
    {
        UserId = RecDemoUsers.AtRisk,
        FinancialHealthScore = 55,          // <=59 -> FinancialHealthImprovement
        SavingRatePercent = 6,               // <10  -> SavingImprovement
        ExpenseGrowthPercent = 25,           // >=20 -> SpendingReduction
        SpendingPatterns = new[]
        {
            new VerifiedSpendingPattern
            {
                Category = "Food", CurrentAmount = 17940, PercentageChange = 28,
                IsUnusual = true, IsRecurringCandidate = false,
            },
            new VerifiedSpendingPattern
            {
                Category = "Subscriptions", CurrentAmount = 3200, PercentageChange = 5,
                IsUnusual = false, IsRecurringCandidate = true,
            },
        },
        Anomalies = new[]
        {
            new VerifiedAnomaly { Type = "Unusual Amount", Severity = "High", Category = "Food", Amount = 9000 },
        },
        ExpectedIncome = 50000,
        ExpectedExpense = 55000,
        ExpectedSaving = -5000,              // <=0 -> CashFlowRisk (matches spec's sample dataset exactly)
        ExpectedClosingBalance = null,       // no trusted opening balance
        ForecastConfidence = "Medium",
        BudgetStatuses = new[]
        {
            new VerifiedBudgetStatus
            {
                Category = "Food", ActualExpense = 17940, UtilizationPercent = 138, IsOverBudget = true,
            },
        },
        BudgetRecommendations = new[]
        {
            new VerifiedBudgetRecommendation
            {
                Category = "Transport", RecommendedBudget = 3500,
                Reason = "Historical average based recommendation.",
            },
        },
        Limitations = Array.Empty<string>(),
    };

    private static VerifiedRecommendationInput HealthyInput() => new()
    {
        UserId = RecDemoUsers.Healthy,
        FinancialHealthScore = 88,
        SavingRatePercent = 32,
        ExpenseGrowthPercent = 4,
        SpendingPatterns = new[]
        {
            new VerifiedSpendingPattern
            {
                Category = "Groceries", CurrentAmount = 8000, PercentageChange = 2,
                IsUnusual = false, IsRecurringCandidate = false,
            },
        },
        Anomalies = Array.Empty<VerifiedAnomaly>(),
        ExpectedIncome = 60000,
        ExpectedExpense = 45000,
        ExpectedSaving = 15000,
        ExpectedClosingBalance = 220000,
        ForecastConfidence = "High",
        BudgetStatuses = new[]
        {
            new VerifiedBudgetStatus
            {
                Category = "Groceries", ActualExpense = 8000, UtilizationPercent = 80, IsOverBudget = false,
            },
        },
        BudgetRecommendations = Array.Empty<VerifiedBudgetRecommendation>(),
        Limitations = Array.Empty<string>(),
    };

    private static VerifiedRecommendationInput InsufficientDataInput() => new()
    {
        UserId = RecDemoUsers.InsufficientData,
        FinancialHealthScore = null,
        SavingRatePercent = null,
        ExpenseGrowthPercent = null,
        SpendingPatterns = Array.Empty<VerifiedSpendingPattern>(),
        Anomalies = Array.Empty<VerifiedAnomaly>(),
        ExpectedIncome = null,
        ExpectedExpense = null,
        ExpectedSaving = null,
        ExpectedClosingBalance = null,
        ForecastConfidence = null,
        BudgetStatuses = Array.Empty<VerifiedBudgetStatus>(),
        BudgetRecommendations = Array.Empty<VerifiedBudgetRecommendation>(),
        Limitations = new[]
        {
            "No verified financial health result is available for this user yet.",
            "No verified spending pattern history is available for this user yet.",
            "No verified budget or forecast data is available for this user yet.",
        },
    };
}
