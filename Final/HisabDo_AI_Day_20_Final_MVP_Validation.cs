// HisabDo AI — Day 20 Final MVP Validation Reference Implementation
// Target: .NET 8 / C#
// Purpose: orchestrate final MVP checks without allowing AI to become
// the source of truth for financial calculations.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.Day20;

public enum ValidationStatus { Pass, Fail, Warning, NotRun }

public sealed record ValidationResult(
    string Area,
    ValidationStatus Status,
    string Message,
    IReadOnlyList<string>? Evidence = null);

public sealed record FinalMvpInput(
    string UserId,
    decimal FinancialHealthScore,
    bool HealthScoreVerified,
    bool SpendingPatternsVerified,
    bool AnomaliesVerified,
    bool CashFlowVerified,
    bool BudgetRecommendationVerified,
    bool AskHisabVerified,
    bool RecommendationEngineVerified,
    bool MobileIntegrationVerified,
    bool WebIntegrationVerified,
    bool UserIsolationVerified,
    bool AiNoInventionRuleEnabled);

public sealed record FinalMvpReport(
    DateTimeOffset GeneratedAt,
    string UserId,
    bool Accepted,
    IReadOnlyList<ValidationResult> Results,
    IReadOnlyList<string> OpenIssues);

public interface IFinalMvpDataVerifier
{
    Task<ValidationResult> VerifyFinancialHealthAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifySpendingPatternsAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyAnomaliesAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyCashFlowAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyBudgetRecommendationAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyAskHisabAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyRecommendationEngineAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyMobileAsync(
        FinalMvpInput input, CancellationToken ct);

    Task<ValidationResult> VerifyWebAsync(
        FinalMvpInput input, CancellationToken ct);
}

public sealed class FinalMvpValidator
{
    private readonly IFinalMvpDataVerifier _verifier;

    public FinalMvpValidator(IFinalMvpDataVerifier verifier)
    {
        _verifier = verifier;
    }

    public async Task<FinalMvpReport> ValidateAsync(
        FinalMvpInput input,
        IReadOnlyList<string>? openIssues = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.UserId))
            throw new ArgumentException("UserId is required.", nameof(input));

        if (input.FinancialHealthScore is < 0 or > 100)
            throw new ArgumentOutOfRangeException(
                nameof(input.FinancialHealthScore),
                "Financial health score must be between 0 and 100.");

        var results = new List<ValidationResult>
        {
            await _verifier.VerifyFinancialHealthAsync(input, ct),
            await _verifier.VerifySpendingPatternsAsync(input, ct),
            await _verifier.VerifyAnomaliesAsync(input, ct),
            await _verifier.VerifyCashFlowAsync(input, ct),
            await _verifier.VerifyBudgetRecommendationAsync(input, ct),
            await _verifier.VerifyAskHisabAsync(input, ct),
            await _verifier.VerifyRecommendationEngineAsync(input, ct),
            await _verifier.VerifyMobileAsync(input, ct),
            await _verifier.VerifyWebAsync(input, ct),

            input.UserIsolationVerified
                ? new ValidationResult(
                    "User Data Isolation",
                    ValidationStatus.Pass,
                    "User isolation/authorization flag is verified.")
                : new ValidationResult(
                    "User Data Isolation",
                    ValidationStatus.Fail,
                    "User isolation/authorization is not verified."),

            input.AiNoInventionRuleEnabled
                ? new ValidationResult(
                    "AI No-Invention Rule",
                    ValidationStatus.Pass,
                    "AI is configured to explain verified backend results only.")
                : new ValidationResult(
                    "AI No-Invention Rule",
                    ValidationStatus.Fail,
                    "AI no-invention rule is not enabled.")
        };

        var issues = openIssues ?? Array.Empty<string>();

        // MVP acceptance requires every mandatory validation to pass.
        // Open issues are not automatically failures because the team may
        // explicitly document/defer non-blocking issues.
        var accepted =
            results.All(r => r.Status == ValidationStatus.Pass) &&
            !issues.Any(i => i.StartsWith("[BLOCKER]", StringComparison.OrdinalIgnoreCase) ||
                             i.StartsWith("[CRITICAL]", StringComparison.OrdinalIgnoreCase));

        return new FinalMvpReport(
            DateTimeOffset.UtcNow,
            input.UserId,
            accepted,
            results,
            issues);
    }
}

// Example verifier implementation for deterministic checks.
// In production, replace placeholders with real repositories/services.
public sealed class ExampleFinalMvpDataVerifier : IFinalMvpDataVerifier
{
    public Task<ValidationResult> VerifyFinancialHealthAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.HealthScoreVerified
                ? Pass("Financial Health Score", "Backend score marked as verified.")
                : Fail("Financial Health Score", "Backend score is not verified."));

    public Task<ValidationResult> VerifySpendingPatternsAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.SpendingPatternsVerified
                ? Pass("Spending Pattern Intelligence", "Verified pattern outputs are available.")
                : Fail("Spending Pattern Intelligence", "Pattern outputs are not verified."));

    public Task<ValidationResult> VerifyAnomaliesAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.AnomaliesVerified
                ? Pass("Anomaly Detection", "Verified anomaly outputs are available.")
                : Fail("Anomaly Detection", "Anomaly outputs are not verified."));

    public Task<ValidationResult> VerifyCashFlowAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.CashFlowVerified
                ? Pass("Cash-Flow Forecasting", "Forecast values are backend-verified.")
                : Fail("Cash-Flow Forecasting", "Forecast values are not verified."));

    public Task<ValidationResult> VerifyBudgetRecommendationAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.BudgetRecommendationVerified
                ? Pass("Budget Recommendation", "Budget recommendation outputs are verified.")
                : Fail("Budget Recommendation", "Budget recommendation is not verified."));

    public Task<ValidationResult> VerifyAskHisabAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.AskHisabVerified
                ? Pass("Ask HisabDo AI", "Question-to-data-to-answer flow is verified.")
                : Fail("Ask HisabDo AI", "Ask HisabDo flow is not verified."));

    public Task<ValidationResult> VerifyRecommendationEngineAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.RecommendationEngineVerified
                ? Pass("AI Recommendation Engine", "Recommendation rules and verified outputs are available.")
                : Fail("AI Recommendation Engine", "Recommendation engine is not verified."));

    public Task<ValidationResult> VerifyMobileAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.MobileIntegrationVerified
                ? Pass("Mobile Integration", "Mobile integration is verified.")
                : Fail("Mobile Integration", "Mobile integration is not verified."));

    public Task<ValidationResult> VerifyWebAsync(
        FinalMvpInput input, CancellationToken ct) =>
        Task.FromResult(
            input.WebIntegrationVerified
                ? Pass("Web Integration", "Web integration is verified.")
                : Fail("Web Integration", "Web integration is not verified."));

    private static ValidationResult Pass(string area, string message) =>
        new(area, ValidationStatus.Pass, message);

    private static ValidationResult Fail(string area, string message) =>
        new(area, ValidationStatus.Fail, message);
}

/*
Example usage:

var input = new FinalMvpInput(
    UserId: "USER-001",
    FinancialHealthScore: 72,
    HealthScoreVerified: true,
    SpendingPatternsVerified: true,
    AnomaliesVerified: true,
    CashFlowVerified: true,
    BudgetRecommendationVerified: true,
    AskHisabVerified: true,
    RecommendationEngineVerified: true,
    MobileIntegrationVerified: true,
    WebIntegrationVerified: true,
    UserIsolationVerified: true,
    AiNoInventionRuleEnabled: true);

var validator = new FinalMvpValidator(new ExampleFinalMvpDataVerifier());

var report = await validator.ValidateAsync(
    input,
    openIssues: new[]
    {
        "[MEDIUM] Improve empty-state copy on one mobile screen."
    });

Console.WriteLine($"Accepted: {report.Accepted}");

Important production rule:
1. Financial calculations remain in the backend/calculation engine.
2. AI receives verified DTOs/results for explanation only.
3. AI must never invent or modify financial numbers.
4. Every user-scoped query must enforce authorization/user isolation.
5. Critical/blocker defects must be resolved or explicitly accepted before delivery.
*/
