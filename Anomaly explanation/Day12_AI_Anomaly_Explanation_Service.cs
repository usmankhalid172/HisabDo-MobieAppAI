public sealed record AnomalyExplanationInput(
    string AnomalyType,
    string Severity,
    string Category,
    decimal? TransactionAmount,
    decimal? HistoricalAverage,
    decimal? ChangePercentage,
    DateTime? TransactionDate,
    string? Description,
    string DetectionReason
);

public sealed record AnomalyExplanation(
    string Title,
    string WhatHappened,
    string WhyFlagged,
    string SeverityText,
    string SuggestedAction
);

public sealed class AnomalyExplanationService
{
    public AnomalyExplanation CreateExplanation(AnomalyExplanationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var title = input.AnomalyType switch
        {
            "Unusual Amount" => $"Unusual {input.Category} expense",
            "Sudden Spending Spike" => $"Spending spike in {input.Category}",
            "Unusual Category" => $"Unusual spending in {input.Category}",
            "Duplicate Transaction" => "Possible duplicate transaction",
            _ => "Spending anomaly detected"
        };

        var what = input.AnomalyType switch
        {
            "Unusual Amount" =>
                "This expense was flagged because its amount is outside the user's normal spending pattern.",
            "Sudden Spending Spike" =>
                "Spending in this category increased compared with the comparable historical period.",
            "Unusual Category" =>
                "This category shows spending above its normal historical pattern.",
            "Duplicate Transaction" =>
                "This transaction was flagged as a possible duplicate based on verified transaction details.",
            _ => "The backend detected an unusual spending pattern."
        };

        var why = string.IsNullOrWhiteSpace(input.DetectionReason)
            ? "The anomaly detection engine identified a deviation from the verified spending baseline."
            : input.DetectionReason;

        var severityText = input.Severity switch
        {
            "High" => "High severity: please review this transaction carefully.",
            "Medium" => "Medium severity: this spending pattern may need attention.",
            "Low" => "Low severity: this is a minor deviation from the normal pattern.",
            _ => $"Severity: {input.Severity}."
        };

        var action = input.AnomalyType == "Duplicate Transaction"
            ? "Review the flagged transactions and confirm whether both should remain."
            : "Review the transaction and confirm whether the spending was expected.";

        return new AnomalyExplanation(
            title,
            what,
            why,
            severityText,
            action
        );
    }
}