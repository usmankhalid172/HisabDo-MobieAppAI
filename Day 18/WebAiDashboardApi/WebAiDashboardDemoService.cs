using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.Web.AI;

// NOTE FOR THE TEAM: nothing in the repo builds a DashboardResponse from
// the real Day 10-16 services — that aggregation is explicitly out of
// scope for this integration task (it's Jaffer's job per every prior
// day's team table). This class is a demo stand-in so the dashboard can
// be tested end-to-end. The shapes/fields are real and match every DTO
// in WebAiDashboardCore.cs exactly; the values are seeded demo data, not
// live calculations.
//
// Three demo users cover the exact states the Day 17 spec's own E2E test
// matrix calls out:
//   - "Rich"   -> every card populated, some optional fields intentionally
//                 missing (severity, expected benefit) to prove those
//                 sections hide instead of getting invented
//   - "Empty"  -> nothing available -> genuinely empty dashboard
//   - "HealthOnly" -> only Financial Health verified, every other card
//                 shows its own independent empty state (spec's own
//                 named test case: "Financial Health available only")

public static class WebDemoUsers
{
    public const string Rich = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    public const string Empty = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee";
    public const string HealthOnly = "ffffffff-ffff-ffff-ffff-ffffffffffff";
}

public sealed class WebAiDashboardDemoService : IWebAiDashboardService
{
    public Task<DashboardResponse> GetDashboardAsync(string authenticatedUserId, CancellationToken cancellationToken)
    {
        var response = authenticatedUserId switch
        {
            WebDemoUsers.Rich => RichDashboard(),
            WebDemoUsers.Empty => EmptyDashboard(),
            WebDemoUsers.HealthOnly => HealthOnlyDashboard(),
            _ => throw new InvalidOperationException($"No demo dashboard data is on file for user '{authenticatedUserId}'."),
        };
        WebUiValidator.ValidateDashboard(response); // same integrity check the spec's own reference runs
        return Task.FromResult(response);
    }

    public Task<AskHisabDoResponse> AskAsync(string authenticatedUserId, string question, CancellationToken cancellationToken)
    {
        var dashboard = authenticatedUserId switch
        {
            WebDemoUsers.Rich => RichDashboard(),
            WebDemoUsers.Empty => EmptyDashboard(),
            WebDemoUsers.HealthOnly => HealthOnlyDashboard(),
            _ => throw new InvalidOperationException($"No demo dashboard data is on file for user '{authenticatedUserId}'."),
        };
        return Task.FromResult(AnswerFrom(dashboard, question));
    }

    // --- Ask HisabDo: a compact grounded answerer using ONLY the same
    // seeded dashboard data — no number here is independent of the cards
    // above, matching the spec's own "Verified Data Retrieval -> AI
    // Explanation -> Verified Answer" flow. ---
    private static AskHisabDoResponse AnswerFrom(DashboardResponse dashboard, string question)
    {
        var q = question.ToLowerInvariant();

        if (q.Contains("health") || q.Contains("score"))
        {
            if (dashboard.FinancialHealth is { IsVerified: true } health)
                return new AskHisabDoResponse(true,
                    $"Your financial health score is {health.Score}/100 ({health.Classification}). {health.Explanation}",
                    "FinancialHealth", null);
            return new AskHisabDoResponse(false, "", "FinancialHealth", "No verified financial health result is available for this user yet.");
        }

        if (q.Contains("unusual") || q.Contains("anomaly") || q.Contains("spike"))
        {
            var anomaly = dashboard.Anomalies.FirstOrDefault(a => a.IsVerified);
            if (anomaly != null)
                return new AskHisabDoResponse(true,
                    $"Yes — {anomaly.Reason}", "Anomaly", null);
            return new AskHisabDoResponse(false, "", "Anomaly", "No verified anomalies are on file for the requested period.");
        }

        if (q.Contains("forecast") || q.Contains("next month"))
        {
            if (dashboard.CashFlowForecast is { IsVerified: true } forecast)
                return new AskHisabDoResponse(true,
                    $"Next month's income is expected to be about Rs {forecast.ForecastIncome:N0} against expenses of about Rs {forecast.ForecastExpense:N0}, for an estimated saving of Rs {forecast.ExpectedSaving:N0}.",
                    "CashFlowForecast", forecast.ExpectedClosingBalance is null ? "A closing balance isn't available without a trusted opening balance on file." : null);
            return new AskHisabDoResponse(false, "", "CashFlowForecast", "No verified forecast is available for this user yet.");
        }

        if (q.Contains("budget"))
        {
            var rec = dashboard.BudgetRecommendations.FirstOrDefault(b => b.IsVerified);
            if (rec != null)
                return new AskHisabDoResponse(true,
                    $"For {rec.Category}, the recommended budget is Rs {rec.RecommendedBudget:N0} based on a historical average of Rs {rec.HistoricalAverage:N0}. {rec.Reason}",
                    "BudgetRecommendation", null);
            return new AskHisabDoResponse(false, "", "BudgetRecommendation", "No verified budget data is available for this user yet.");
        }

        if (q.Contains("spend") || q.Contains("category") || q.Contains("money going"))
        {
            var top = dashboard.SpendingPatterns.Where(p => p.IsVerified).OrderByDescending(p => p.CurrentAmount).FirstOrDefault();
            if (top != null)
                return new AskHisabDoResponse(true,
                    $"Your highest spending category is {top.Category} at Rs {top.CurrentAmount:N0}, {(top.PercentageChange >= 0 ? "up" : "down")} {Math.Abs(top.PercentageChange)}% from the previous period.",
                    "SpendingPattern", null);
            return new AskHisabDoResponse(false, "", "SpendingPattern", "No verified spending pattern data is available for this user yet.");
        }

        return new AskHisabDoResponse(false, "",
            null, "I can answer questions about your financial health, spending patterns, budgets, anomalies and cash-flow forecast — try rephrasing your question around one of those.");
    }

    // --- Demo dataset: "Rich" user ---
    private static DashboardResponse RichDashboard() => new(
        FinancialHealth: new FinancialHealthDto(
            IsVerified: true, Score: 72, Classification: "Good",
            Factors: new Dictionary<string, decimal> { ["Saving Behavior"] = 22, ["Expense Control"] = 18, ["Cash Flow"] = 12, ["Budget Control"] = 13, ["Debt/Udhaar"] = 7 },
            Explanation: "Saving behavior and cash flow are strong; budget adherence in Food needs attention."),
        SpendingPatterns: new[]
        {
            new SpendingPatternDto(true, "Food", 17940, 14020, 28.0m, "Up", "Food spending increased noticeably this period."),
            new SpendingPatternDto(true, "Transport", 3100, 3200, -3.1m, "Down", "Transport spending is stable to slightly lower."),
            new SpendingPatternDto(true, "Bills", 4500, 4500, 0m, "Flat", "Bills are unchanged from the previous period."),
        },
        Anomalies: new[]
        {
            new AnomalyDto(true, "Unusual Amount", 9000, "High", "Food expense is about 990% above the typical Rs 825 baseline.", "This single transaction is well outside your normal Food spending pattern."),
        },
        CashFlowForecast: new CashFlowForecastDto(
            IsVerified: true, ForecastIncome: 50000, ForecastExpense: 11817, ExpectedSaving: 38183,
            ExpectedClosingBalance: null, Confidence: "Medium",
            Explanation: "Based on 3 months of history. A closing balance isn't available without a trusted opening balance on file."),
        BudgetRecommendations: new[]
        {
            new BudgetRecommendationDto(true, "Food", 3550, 3550, 1000, -2550, "Historical average is well above the existing budget.", "Sufficient", "Consider raising the Food budget closer to actual spending."),
            new BudgetRecommendationDto(true, "Transport", 3100, 3100, 3500, 400, "Historical average is within the existing budget.", "Sufficient", "Current Transport budget has healthy headroom."),
        },
        Recommendations: new[]
        {
            new RecommendationDto("rec-cashflow-risk", true, "CashFlowRisk", "High", "High",
                "Verified forecasted saving is positive this period, but Food overspending is eroding it.",
                "Review expected expenses before the forecast period.", 38183, null, null, null),
            new RecommendationDto("rec-food-unusual", true, "UnusualSpendingReview", "High", "High",
                "Verified anomaly: Unusual Amount (High) in Food.",
                "Review the transaction details and confirm whether the spending was expected.", 9000, null, null, null),
            new RecommendationDto("rec-food-budget", true, "BudgetAdjustment", "Medium", "Medium",
                "Verified budget utilization for Food is high relative to the existing budget.",
                "Review Food spending and compare it with the existing budget.", 17940, 138, null, null),
            // Deliberately no Severity and no VerifiedExpectedBenefit here —
            // proves the UI hides those sections instead of inventing them.
            new RecommendationDto("rec-transport-budget", true, "BudgetAdjustment", "Low", null,
                "Verified budget engine recommends Rs 3,100 for Transport, within existing budget.",
                "Review the recommended budget against your current financial plan.", 3100, null, null,
                "This recommendation is based on 3 months of history and may change as spending behavior changes."),
        });

    // --- Demo dataset: "Empty" user ---
    private static DashboardResponse EmptyDashboard() => new(
        FinancialHealth: null,
        SpendingPatterns: Array.Empty<SpendingPatternDto>(),
        Anomalies: Array.Empty<AnomalyDto>(),
        CashFlowForecast: null,
        BudgetRecommendations: Array.Empty<BudgetRecommendationDto>(),
        Recommendations: Array.Empty<RecommendationDto>());

    // --- Demo dataset: "HealthOnly" user ---
    private static DashboardResponse HealthOnlyDashboard() => new(
        FinancialHealth: new FinancialHealthDto(
            IsVerified: true, Score: 45, Classification: "Needs Attention",
            Factors: new Dictionary<string, decimal> { ["Saving Behavior"] = 8, ["Expense Control"] = 12, ["Cash Flow"] = 5 },
            Explanation: "Saving rate is low this period. Other verified factors aren't available yet."),
        SpendingPatterns: Array.Empty<SpendingPatternDto>(),
        Anomalies: Array.Empty<AnomalyDto>(),
        CashFlowForecast: null,
        BudgetRecommendations: Array.Empty<BudgetRecommendationDto>(),
        Recommendations: Array.Empty<RecommendationDto>());
}

/// <summary>
/// DEMO-ONLY current-user resolver — reads the authenticated identity that
/// DemoAuthenticationHandler (Program.cs) attaches via the X-Demo-User
/// header. Real auth will replace this entirely.
/// </summary>
public sealed class DemoCurrentUser : ICurrentUser
{
    private readonly Microsoft.AspNetCore.Http.IHttpContextAccessor _httpContextAccessor;
    public DemoCurrentUser(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string UserId =>
        _httpContextAccessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
}
