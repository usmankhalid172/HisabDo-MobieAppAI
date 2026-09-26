// HisabDo AI — Final AI/ML Validation
// .NET 8 / C# reference implementation
// Financial calculations remain backend-owned.
// AI is an explanation layer over verified results.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.Ai.FinalValidation;

public enum ValidationStatus { Pass, Fail, Warning }

public sealed record MetricResult(
    string Name,
    double? Value,
    string? Notes);

public sealed record VerifiedAiInput(
    string UserId,
    decimal HealthScore,
    IReadOnlyDictionary<string, decimal> HealthFactors,
    IReadOnlyDictionary<string, decimal> SpendingPatterns,
    IReadOnlyList<VerifiedAnomaly> Anomalies,
    VerifiedForecast Forecast,
    IReadOnlyDictionary<string, decimal> BudgetRecommendations,
    IReadOnlyList<VerifiedRecommendation> Recommendations,
    bool IsSufficientData);

public sealed record VerifiedAnomaly(
    string Id,
    string Type,
    decimal? Amount,
    string Evidence,
    string Severity);

public sealed record VerifiedForecast(
    decimal ExpectedIncome,
    decimal ExpectedExpenses,
    decimal ExpectedSavings,
    decimal ExpectedClosingBalance,
    string Assumptions,
    string Limitation);

public sealed record VerifiedRecommendation(
    string Id,
    string Type,
    string Reason,
    string Action,
    string Priority,
    string Severity,
    decimal? VerifiedAmount,
    decimal? VerifiedPercentage);

public sealed record AiValidationResult(
    string TestName,
    ValidationStatus Status,
    string Message,
    IReadOnlyList<string>? Violations = null);

public interface IAiExplanationClient
{
    Task<string> ExplainAsync(
        VerifiedAiInput input,
        CancellationToken cancellationToken = default);
}

public sealed class AiSafetyValidator
{
    // Extracts numeric tokens from AI output and checks that they are traceable
    // to explicitly allowed verified values. This is a lightweight reference
    // guard; production systems should use structured AI output with source IDs.
    public AiValidationResult ValidateNoInventedNumbers(
        string aiText,
        VerifiedAiInput input)
    {
        if (string.IsNullOrWhiteSpace(aiText))
            return new("AI Output", ValidationStatus.Fail, "AI returned empty output.");

        var allowedNumbers = new HashSet<string>(
            GetAllowedNumericStrings(input),
            StringComparer.OrdinalIgnoreCase);

        var numericTokens = ExtractNumericTokens(aiText);
        var violations = numericTokens
            .Where(token => !allowedNumbers.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return violations.Count == 0
            ? new("AI No-Invention", ValidationStatus.Pass,
                "No unsupported numeric claims were detected.")
            : new("AI No-Invention", ValidationStatus.Fail,
                "AI output contains numeric claims not present in verified input.",
                violations);
    }

    public AiValidationResult ValidateInsufficientData(
        string aiText,
        VerifiedAiInput input)
    {
        if (input.IsSufficientData)
            return new("Insufficient Data", ValidationStatus.Pass,
                "Dataset contains sufficient data.");

        var safeTerms = new[]
        {
            "insufficient data",
            "not enough data",
            "unavailable",
            "cannot determine",
            "not enough history"
        };

        return safeTerms.Any(aiText.Contains, StringComparison.OrdinalIgnoreCase)
            ? new("Insufficient Data", ValidationStatus.Pass,
                "AI explicitly communicates data limitation.")
            : new("Insufficient Data", ValidationStatus.Fail,
                "AI did not safely communicate insufficient data.");
    }

    public AiValidationResult ValidateVerifiedRecommendation(
        string aiText,
        VerifiedRecommendation recommendation)
    {
        var required = new[]
        {
            recommendation.Type,
            recommendation.Reason,
            recommendation.Action,
            recommendation.Priority,
            recommendation.Severity
        };

        var missing = required
            .Where(x => string.IsNullOrWhiteSpace(x) ||
                        !aiText.Contains(x, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return missing.Count == 0
            ? new("Recommendation Explanation", ValidationStatus.Pass,
                "AI explanation preserves verified recommendation facts.")
            : new("Recommendation Explanation", ValidationStatus.Warning,
                "Some verified recommendation fields were not explicitly preserved.",
                missing);
    }

    private static IEnumerable<string> GetAllowedNumericStrings(
        VerifiedAiInput input)
    {
        yield return Format(input.HealthScore);

        foreach (var value in input.HealthFactors.Values)
            yield return Format(value);

        foreach (var value in input.SpendingPatterns.Values)
            yield return Format(value);

        foreach (var anomaly in input.Anomalies)
            if (anomaly.Amount.HasValue)
                yield return Format(anomaly.Amount.Value);

        yield return Format(input.Forecast.ExpectedIncome);
        yield return Format(input.Forecast.ExpectedExpenses);
        yield return Format(input.Forecast.ExpectedSavings);
        yield return Format(input.Forecast.ExpectedClosingBalance);

        foreach (var value in input.BudgetRecommendations.Values)
            yield return Format(value);

        foreach (var recommendation in input.Recommendations)
        {
            if (recommendation.VerifiedAmount.HasValue)
                yield return Format(recommendation.VerifiedAmount.Value);

            if (recommendation.VerifiedPercentage.HasValue)
                yield return Format(recommendation.VerifiedPercentage.Value);
        }
    }

    private static string Format(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> ExtractNumericTokens(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            text,
            @"(?<!\w)\d+(?:\.\d+)?(?!\w)");

        return matches.Select(m => m.Value).ToList();
    }
}

public sealed class FinalAiMlValidationRunner
{
    private readonly IAiExplanationClient _ai;
    private readonly AiSafetyValidator _safety;

    public FinalAiMlValidationRunner(
        IAiExplanationClient ai,
        AiSafetyValidator safety)
    {
        _ai = ai;
        _safety = safety;
    }

    public async Task<IReadOnlyList<AiValidationResult>> RunAsync(
        VerifiedAiInput input,
        CancellationToken ct = default)
    {
        // AI receives verified backend data only.
        var output = await _ai.ExplainAsync(input, ct);

        var results = new List<AiValidationResult>
        {
            _safety.ValidateNoInventedNumbers(output, input),
            _safety.ValidateInsufficientData(output, input)
        };

        foreach (var recommendation in input.Recommendations)
        {
            results.Add(
                _safety.ValidateVerifiedRecommendation(output, recommendation));
        }

        return results;
    }
}

// Example accuracy calculations.
// Use these only when ground-truth labels exist.
public static class AccuracyMetrics
{
    public static MetricResult Precision(int truePositive, int falsePositive)
    {
        var denominator = truePositive + falsePositive;
        return denominator == 0
            ? new("Precision", null, "No positive predictions available.")
            : new("Precision",
                (double)truePositive / denominator,
                "TP / (TP + FP)");
    }

    public static MetricResult Recall(int truePositive, int falseNegative)
    {
        var denominator = truePositive + falseNegative;
        return denominator == 0
            ? new("Recall", null, "No positive ground-truth cases available.")
            : new("Recall",
                (double)truePositive / denominator,
                "TP / (TP + FN)");
    }

    public static MetricResult F1(int truePositive, int falsePositive, int falseNegative)
    {
        var precision = Precision(truePositive, falsePositive).Value;
        var recall = Recall(truePositive, falseNegative).Value;

        if (!precision.HasValue || !recall.HasValue ||
            precision.Value + recall.Value == 0)
            return new("F1", null, "F1 cannot be calculated from the supplied data.");

        var f1 = 2 * precision.Value * recall.Value /
                 (precision.Value + recall.Value);

        return new("F1", f1, "2 * precision * recall / (precision + recall)");
    }

    // Forecasting example: MAE requires actual and predicted values for
    // the same evaluation periods.
    public static MetricResult MeanAbsoluteError(
        IReadOnlyList<decimal> actual,
        IReadOnlyList<decimal> predicted)
    {
        if (actual.Count == 0 || actual.Count != predicted.Count)
            return new("MAE", null,
                "Actual and predicted series must have equal non-zero length.");

        var mae = actual.Zip(predicted, (a, p) => Math.Abs(a - p))
                        .Average();

        return new("MAE", (double)mae,
            "Mean absolute error over the evaluation periods.");
    }
}

// Production implementation guidance:
// 1. Keep all financial formulas in backend services.
// 2. Store thresholds/weights in versioned configuration.
// 3. Run regression tests whenever thresholds change.
// 4. Use a fixed demo dataset for repeatable validation.
// 5. Keep user authorization on every data query.
// 6. Prefer structured AI output containing source field IDs.
// 7. Reject AI responses with unsupported numeric claims.
// 8. Never let an LLM write financial values back into the source of truth.
// 9. Document assumptions and limitations for every forecast/model.
// 10. Record false positives/false negatives and their resolution.
