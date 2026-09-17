using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day15;

/// <summary>
/// Day 15 — Ask HisabDo AI
/// Query classification and query-to-data mapping reference implementation.
/// Important: this layer identifies intent and required verified data.
/// It does NOT calculate financial values and does NOT invent answers.
/// </summary>
public enum FinancialQueryCategory
{
    IncomeExpense,
    SpendingCategory,
    Budget,
    FinancialHealth,
    Anomaly,
    CashFlowForecast,
    BudgetRecommendation,
    Unsupported,
    Ambiguous
}

public sealed record ClassificationResult(
    FinancialQueryCategory Category,
    double Confidence,
    IReadOnlyList<string> RequiredData,
    string Reason,
    IReadOnlyList<string> MissingClarification
);

public interface IFinancialQueryClassifier
{
    ClassificationResult Classify(string question);
}

public sealed class AskHisabDoQueryClassifier : IFinancialQueryClassifier
{
    private static readonly string[] IncomeExpense =
    {
        "income", "salary", "earned", "earning", "expense", "expenses",
        "spent", "spend", "cost", "how much did i spend", "how much did i earn"
    };

    private static readonly string[] Spending =
    {
        "spending", "spend", "category", "food", "transport", "shopping",
        "where is my money going", "highest spending", "top spending",
        "which category", "category-wise"
    };

    private static readonly string[] Budget =
    {
        "budget", "budget used", "budget utilization", "over budget",
        "remaining budget", "under budget", "budget limit"
    };

    private static readonly string[] Health =
    {
        "financial health", "health score", "financial score",
        "saving rate", "expense ratio", "cash flow health"
    };

    private static readonly string[] Anomaly =
    {
        "anomaly", "unusual", "unusually", "strange spending",
        "spike", "sudden spending", "duplicate transaction",
        "duplicate expense", "abnormal"
    };

    private static readonly string[] Forecast =
    {
        "forecast", "predict", "prediction", "future cash flow",
        "next month", "next month cash", "expected income",
        "expected expense", "expected savings", "closing balance"
    };

    private static readonly string[] Recommendation =
    {
        "recommend", "recommendation", "suggest", "suggestion",
        "what should i do", "how should i budget", "recommended budget",
        "save more", "where should i cut", "budget adjustment"
    };

    public ClassificationResult Classify(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new(
                FinancialQueryCategory.Ambiguous,
                0,
                Array.Empty<string>(),
                "Question is empty.",
                new[] { "Please provide a financial question." });
        }

        var q = Normalize(question);
        var scores = new Dictionary<FinancialQueryCategory, int>
        {
            [FinancialQueryCategory.IncomeExpense] = Score(q, IncomeExpense),
            [FinancialQueryCategory.SpendingCategory] = Score(q, Spending),
            [FinancialQueryCategory.Budget] = Score(q, Budget),
            [FinancialQueryCategory.FinancialHealth] = Score(q, Health),
            [FinancialQueryCategory.Anomaly] = Score(q, Anomaly),
            [FinancialQueryCategory.CashFlowForecast] = Score(q, Forecast),
            [FinancialQueryCategory.BudgetRecommendation] = Score(q, Recommendation)
        };

        // Specific intent signals take priority over generic words such as "spend".
        if (ContainsAny(q, Anomaly)) return Build(FinancialQueryCategory.Anomaly, scores);
        if (ContainsAny(q, Forecast)) return Build(FinancialQueryCategory.CashFlowForecast, scores);
        if (ContainsAny(q, Recommendation)) return Build(FinancialQueryCategory.BudgetRecommendation, scores);
        if (ContainsAny(q, Health)) return Build(FinancialQueryCategory.FinancialHealth, scores);
        if (ContainsAny(q, Budget)) return Build(FinancialQueryCategory.Budget, scores);

        var best = scores.OrderByDescending(x => x.Value).First();
        var second = scores.OrderByDescending(x => x.Value).Skip(1).First();

        if (best.Value == 0)
        {
            return new(
                FinancialQueryCategory.Unsupported,
                0,
                Array.Empty<string>(),
                "No supported financial intent was detected.",
                Array.Empty<string>());
        }

        if (best.Value == second.Value)
        {
            return new(
                FinancialQueryCategory.Ambiguous,
                0.5,
                Array.Empty<string>(),
                "Multiple financial intents have equal evidence.",
                new[] { "Ask whether the user wants spending details, budget status, forecast, health, anomaly, or recommendation." });
        }

        return Build(best.Key, scores);
    }

    private static ClassificationResult Build(
        FinancialQueryCategory category,
        Dictionary<FinancialQueryCategory, int> scores)
    {
        var required = RequiredDataFor(category);
        var max = Math.Max(1, scores.Values.Max());
        var confidence = Math.Min(0.99, scores[category] / (double)max);

        return new(
            category,
            confidence,
            required,
            $"Detected supported intent: {category}.",
            Array.Empty<string>());
    }

    private static IReadOnlyList<string> RequiredDataFor(FinancialQueryCategory category) =>
        category switch
        {
            FinancialQueryCategory.IncomeExpense => new[]
            {
                "Authenticated UserId", "Transactions", "TransactionType",
                "Amount", "Date", "Category"
            },
            FinancialQueryCategory.SpendingCategory => new[]
            {
                "Authenticated UserId", "Expense Transactions",
                "Amount", "Category", "Date", "Current and previous period where comparison is requested"
            },
            FinancialQueryCategory.Budget => new[]
            {
                "Authenticated UserId", "Expense Transactions",
                "Category", "Amount", "Existing Budgets", "Budget Period"
            },
            FinancialQueryCategory.FinancialHealth => new[]
            {
                "Authenticated UserId", "Verified Financial Health Result",
                "Saving Rate", "Expense Ratio", "Budget Utilization",
                "Cash Flow", "Debt/Udhaar Result", "Expense Growth", "0–100 Score"
            },
            FinancialQueryCategory.Anomaly => new[]
            {
                "Authenticated UserId", "Expense Transactions",
                "Verified Anomaly Results", "Historical Baseline"
            },
            FinancialQueryCategory.CashFlowForecast => new[]
            {
                "Authenticated UserId", "3–6 Months Historical Transactions where available",
                "Monthly Income", "Monthly Expenses", "Opening Balance if available",
                "Verified Forecast Result"
            },
            FinancialQueryCategory.BudgetRecommendation => new[]
            {
                "Authenticated UserId", "3–6 Months Expense History where available",
                "Category Spending", "Existing Budgets where available",
                "Verified Budget Recommendation Result"
            },
            _ => Array.Empty<string>()
        };

    private static int Score(string q, IEnumerable<string> keywords) =>
        keywords.Count(k => q.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAny(string q, IEnumerable<string> keywords) =>
        keywords.Any(k => q.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        return Regex.Replace(lower, @"\s+", " ");
    }
}
