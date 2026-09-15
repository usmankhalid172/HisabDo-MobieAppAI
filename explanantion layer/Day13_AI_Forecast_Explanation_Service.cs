using System;
using System.Collections.Generic;
using System.Linq;

namespace HisabDo.AI.Day13
{
    // This DTO must be populated by the verified forecasting engine.
    // The AI explanation layer must not calculate replacement values.
    public sealed class VerifiedCashFlowForecast
    {
        public decimal? ExpectedIncome { get; init; }
        public decimal? ExpectedExpense { get; init; }
        public decimal? ExpectedSaving { get; init; }
        public decimal? OpeningBalance { get; init; }
        public decimal? ExpectedClosingBalance { get; init; }

        public string CashFlowDirection { get; init; } = "Unknown";
        public string Confidence { get; init; } = "Unknown";
        public int HistoricalMonthsUsed { get; init; }
        public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
        public bool ForecastAvailable { get; init; }
    }

    public sealed class ForecastExplanation
    {
        public string Summary { get; init; } = "";
        public string ExpectedIncome { get; init; } = "";
        public string ExpectedExpense { get; init; } = "";
        public string ExpectedSaving { get; init; } = "";
        public string ExpectedClosingBalance { get; init; } = "";
        public string CashFlowTrend { get; init; } = "";
        public string ActionableObservation { get; init; } = "";
        public string Uncertainty { get; init; } = "";
    }

    public interface IForecastExplanationService
    {
        ForecastExplanation BuildExplanation(VerifiedCashFlowForecast forecast);
    }

    public sealed class ForecastExplanationService : IForecastExplanationService
    {
        public ForecastExplanation BuildExplanation(VerifiedCashFlowForecast forecast)
        {
            if (forecast == null)
                throw new ArgumentNullException(nameof(forecast));

            if (!forecast.ForecastAvailable)
            {
                return new ForecastExplanation
                {
                    Summary = "There is not enough verified historical financial data to produce a reliable forecast at this time.",
                    Uncertainty = BuildUncertainty(forecast),
                    ActionableObservation = "Add more income and expense history so a more useful forecast can be generated."
                };
            }

            return new ForecastExplanation
            {
                Summary = BuildSummary(forecast),
                ExpectedIncome = ExplainAmount("expected income", forecast.ExpectedIncome),
                ExpectedExpense = ExplainAmount("expected expenses", forecast.ExpectedExpense),
                ExpectedSaving = ExplainAmount("expected savings", forecast.ExpectedSaving),
                ExpectedClosingBalance = BuildClosingBalance(forecast),
                CashFlowTrend = BuildCashFlowTrend(forecast),
                ActionableObservation = BuildAction(forecast),
                Uncertainty = BuildUncertainty(forecast)
            };
        }

        private static string BuildSummary(VerifiedCashFlowForecast f)
        {
            if (f.ExpectedIncome.HasValue && f.ExpectedExpense.HasValue)
            {
                return $"Based on your recent financial pattern, your expected income is {Money(f.ExpectedIncome.Value)} " +
                       $"and your expected expenses are {Money(f.ExpectedExpense.Value)} for the forecast period.";
            }

            return "A forecast is available based on the verified historical financial data.";
        }

        private static string ExplainAmount(string label, decimal? value)
        {
            return value.HasValue
                ? $"Your {label} are {Money(value.Value)} based on the verified forecast."
                : $"Your {label} are not available in the verified forecast.";
        }

        private static string BuildClosingBalance(VerifiedCashFlowForecast f)
        {
            if (!f.ExpectedClosingBalance.HasValue)
                return "Expected closing balance is not available because a verified closing-balance forecast was not provided.";

            return $"Your expected closing balance is {Money(f.ExpectedClosingBalance.Value)}.";
        }

        private static string BuildCashFlowTrend(VerifiedCashFlowForecast f)
        {
            return f.CashFlowDirection switch
            {
                "Positive" => "The forecast indicates a positive cash flow outlook.",
                "Negative" => "The forecast indicates a negative cash flow outlook.",
                _ => "The forecast does not provide a verified cash flow direction."
            };
        }

        private static string BuildAction(VerifiedCashFlowForecast f)
        {
            if (f.CashFlowDirection == "Negative")
                return "Consider reviewing non-essential expenses because the verified forecast indicates negative cash flow.";

            if (f.CashFlowDirection == "Positive")
                return "Maintain awareness of your spending so the positive cash flow outlook can be supported by your actual financial behavior.";

            return "Review the forecast together with your actual financial activity as new data becomes available.";
        }

        private static string BuildUncertainty(VerifiedCashFlowForecast f)
        {
            var parts = new List<string>
            {
                "This forecast is an estimate based on available historical data and may change if your income or spending behavior changes."
            };

            if (f.HistoricalMonthsUsed > 0)
                parts.Add($"The forecast uses {f.HistoricalMonthsUsed} historical month(s).");

            if (!string.IsNullOrWhiteSpace(f.Confidence) && f.Confidence != "Unknown")
                parts.Add($"Forecast confidence: {f.Confidence}.");

            if (f.Limitations.Count > 0)
                parts.Add("Limitations: " + string.Join("; ", f.Limitations));

            return string.Join(" ", parts);
        }

        private static string Money(decimal value)
            => $"PKR {value:N0}";
    }

    // Optional LLM boundary:
    // Send only the ForecastExplanation or the original VerifiedCashFlowForecast
    // to the selected LLM provider with strict no-invention instructions.
    //
    // IMPORTANT:
    // - Do not ask the LLM to calculate financial values.
    // - Do not allow the LLM to replace backend values.
    // - If a field is null, the LLM must say it is unavailable.
}
