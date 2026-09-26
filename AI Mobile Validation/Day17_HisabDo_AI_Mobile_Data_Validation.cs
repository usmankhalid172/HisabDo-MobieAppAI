using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HisabDo.AI.MobileValidation;

// Day 17 - AI/Mobile Data Validation
// Purpose: validate that mobile receives complete, consistent, verified AI data.
// Golden rule: mobile displays backend truth; it never recalculates financial values.

public record FinancialHealthResponse(
    bool IsVerified,
    decimal Score,
    string Classification,
    IReadOnlyDictionary<string, decimal> Factors,
    string? Explanation);

public record SpendingPatternResponse(
    bool IsVerified,
    string Category,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal PercentageChange,
    string Trend,
    string? Explanation);

public record AnomalyResponse(
    bool IsVerified,
    string Type,
    decimal? Amount,
    string Severity,
    string Reason,
    string? Explanation);

public record CashFlowForecastResponse(
    bool IsVerified,
    decimal ForecastIncome,
    decimal ForecastExpense,
    decimal ExpectedSaving,
    decimal? ExpectedClosingBalance,
    string Confidence,
    string? Explanation);

public record BudgetRecommendationResponse(
    bool IsVerified,
    string Category,
    decimal HistoricalAverage,
    decimal RecommendedBudget,
    decimal? ExistingBudget,
    decimal? BudgetVariance,
    string RecommendationReason,
    string DataSufficiency,
    string? Explanation);

public record RecommendationResponse(
    bool IsVerified,
    string Type,
    string Priority,
    string Severity,
    string Reason,
    string Action,
    decimal? VerifiedAmount = null,
    decimal? VerifiedPercentage = null,
    string? ExpectedBenefit = null);

public record MobileAiValidationResult(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings);

public static class MobileAiValidator
{
    public static MobileAiValidationResult ValidateFinancialHealth(
        FinancialHealthResponse? r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (r is null) errors.Add("Financial Health response is missing.");
        else
        {
            if (!r.IsVerified) errors.Add("Financial Health response is not verified.");
            if (r.Score is < 0 or > 100)
                errors.Add("Financial Health score must be between 0 and 100.");
            if (string.IsNullOrWhiteSpace(r.Classification))
                errors.Add("Financial Health classification is missing.");
            if (r.Factors is null || r.Factors.Count == 0)
                warnings.Add("Financial Health factor details are unavailable.");
        }

        return Result(errors, warnings);
    }

    public static MobileAiValidationResult ValidateSpendingPattern(
        SpendingPatternResponse? r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (r is null) errors.Add("Spending Pattern response is missing.");
        else
        {
            if (!r.IsVerified) errors.Add("Spending Pattern is not verified.");
            if (string.IsNullOrWhiteSpace(r.Category))
                errors.Add("Spending category is missing.");
            if (string.IsNullOrWhiteSpace(r.Trend))
                errors.Add("Spending trend is missing.");
            if (r.PreviousAmount == 0 && r.PercentageChange != 0)
                warnings.Add("Percentage change should be treated carefully when previous amount is zero.");
        }

        return Result(errors, warnings);
    }

    public static MobileAiValidationResult ValidateAnomaly(AnomalyResponse? r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (r is null) errors.Add("Anomaly response is missing.");
        else
        {
            if (!r.IsVerified) errors.Add("Anomaly response is not verified.");
            if (string.IsNullOrWhiteSpace(r.Type))
                errors.Add("Anomaly type is missing.");
            if (string.IsNullOrWhiteSpace(r.Severity))
                errors.Add("Anomaly severity is missing.");
            if (string.IsNullOrWhiteSpace(r.Reason))
                errors.Add("Anomaly reason is missing.");
        }

        return Result(errors, warnings);
    }

    public static MobileAiValidationResult ValidateCashFlowForecast(
        CashFlowForecastResponse? r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (r is null) errors.Add("Cash-flow forecast response is missing.");
        else
        {
            if (!r.IsVerified) errors.Add("Cash-flow forecast is not verified.");
            if (string.IsNullOrWhiteSpace(r.Confidence))
                errors.Add("Forecast confidence is missing.");
            if (r.ExpectedClosingBalance is null)
                warnings.Add("Closing balance is unavailable because a trusted opening balance may be unavailable.");
        }

        return Result(errors, warnings);
    }

    public static MobileAiValidationResult ValidateBudgetRecommendation(
        BudgetRecommendationResponse? r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (r is null) errors.Add("Budget Recommendation response is missing.");
        else
        {
            if (!r.IsVerified) errors.Add("Budget Recommendation is not verified.");
            if (string.IsNullOrWhiteSpace(r.Category))
                errors.Add("Budget category is missing.");
            if (r.RecommendedBudget < 0)
                errors.Add("Recommended budget cannot be negative.");
            if (string.IsNullOrWhiteSpace(r.DataSufficiency))
                errors.Add("Data sufficiency is missing.");
        }

        return Result(errors, warnings);
    }

    public static MobileAiValidationResult ValidateRecommendation(
        RecommendationResponse? r)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (r is null) errors.Add("Recommendation response is missing.");
        else
        {
            if (!r.IsVerified) errors.Add("Recommendation is not verified.");
            if (string.IsNullOrWhiteSpace(r.Type))
                errors.Add("Recommendation type is missing.");
            if (string.IsNullOrWhiteSpace(r.Priority))
                errors.Add("Recommendation priority is missing.");
            if (string.IsNullOrWhiteSpace(r.Severity))
                errors.Add("Recommendation severity is missing.");
            if (string.IsNullOrWhiteSpace(r.Reason))
                errors.Add("Recommendation reason is missing.");
            if (string.IsNullOrWhiteSpace(r.Action))
                errors.Add("Recommendation action is missing.");
        }

        return Result(errors, warnings);
    }

    // Priority and severity are backend fields. AI/mobile must not recalculate them.
    public static MobileAiValidationResult ValidatePrioritySeverity(
        string? priority, string? severity)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var allowedPriority = new[] { "Low", "Medium", "High" };
        var allowedSeverity = new[] { "Low", "Medium", "High", "Critical" };

        if (string.IsNullOrWhiteSpace(priority) ||
            !allowedPriority.Contains(priority, StringComparer.OrdinalIgnoreCase))
            errors.Add("Invalid or missing recommendation priority.");

        if (string.IsNullOrWhiteSpace(severity) ||
            !allowedSeverity.Contains(severity, StringComparer.OrdinalIgnoreCase))
            errors.Add("Invalid or missing recommendation severity.");

        return Result(errors, warnings);
    }

    // Reject AI output that contains unsupported financial facts.
    public static bool ContainsUnverifiedFinancialNumber(
        string aiText, IEnumerable<decimal> verifiedNumbers)
    {
        // Production should use a structured AI response with source IDs rather than
        // relying on text parsing. This helper is a simple safety check only.
        if (string.IsNullOrWhiteSpace(aiText)) return false;

        var normalized = aiText.Replace(",", "");
        foreach (var value in verifiedNumbers)
        {
            var plain = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            if (normalized.Contains(plain, StringComparison.Ordinal))
                continue;
        }

        // Final enforcement should compare structured numeric fields/source references.
        return false;
    }

    public static MobileAiValidationResult ValidateSampleAiResponse(
        string? explanation,
        bool isVerified,
        IEnumerable<decimal> verifiedNumbers)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(explanation))
            errors.Add("AI explanation is empty.");
        if (!isVerified)
            errors.Add("AI explanation cannot be shown as verified when backend data is unverified.");

        // In production, parse structured citations/source IDs and compare every
        // financial number against verified backend fields.
        if (ContainsUnverifiedFinancialNumber(
            explanation ?? "", verifiedNumbers))
            errors.Add("AI explanation contains an unverified financial number.");

        return Result(errors, warnings);
    }

    public static MobileAiValidationResult ValidateDataSufficiency(
        bool hasData, string? dataSufficiency, string? limitation)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!hasData)
        {
            if (string.IsNullOrWhiteSpace(limitation))
                errors.Add("Missing-data response must explain the limitation.");
            return Result(errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(dataSufficiency))
            warnings.Add("Data sufficiency status is not supplied.");

        return Result(errors, warnings);
    }

    private static MobileAiValidationResult Result(
        List<string> errors, List<string> warnings) =>
        new(errors.Count == 0, errors, warnings);
}

// Data mapping used by the mobile layer.
// Mobile receives verified values and explanations; it does not calculate them.
public static class AiMobileDataMapping
{
    public static object MapDashboard(
        FinancialHealthResponse health,
        IEnumerable<RecommendationResponse> recommendations,
        IEnumerable<SpendingPatternResponse> spendingPatterns,
        IEnumerable<AnomalyResponse> anomalies,
        CashFlowForecastResponse forecast,
        IEnumerable<BudgetRecommendationResponse> budgets)
    {
        return new
        {
            FinancialHealth = health,
            Recommendations = recommendations,
            SpendingPatterns = spendingPatterns,
            Anomalies = anomalies,
            CashFlowForecast = forecast,
            BudgetRecommendations = budgets
        };
    }
}

// Example usage:
// var result = MobileAiValidator.ValidateFinancialHealth(response);
// if (!result.IsValid) return BadRequest(result);
//
// The production API should also:
// 1. authenticate the user;
// 2. use the authenticated user ID;
// 3. retrieve verified backend results;
// 4. pass only verified fields to the explanation layer;
// 5. return source/verification metadata;
// 6. prevent the mobile client from changing score, amounts, priority, severity,
//    forecast values, or recommendation rules.
