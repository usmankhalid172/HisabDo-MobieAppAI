using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day15;

public enum AskHisabDoIntent
{
    IncomeExpense,
    SpendingPattern,
    Budget,
    FinancialHealth,
    Anomaly,
    CashFlowForecast,
    BudgetRecommendation,
    Unsupported,
    ClarificationRequired
}

public sealed record AskHisabDoRequest(Guid UserId, string Question);

public sealed record VerifiedFinancialContext(
    AskHisabDoIntent Intent,
    string AnswerContext,
    IReadOnlyList<string> Limitations,
    bool HasVerifiedData);

public sealed record AskHisabDoResponse(
    string Answer,
    IReadOnlyList<string> Limitations,
    bool IsVerified);

public interface IAskHisabDoIntentClassifier
{
    Task<AskHisabDoIntent> ClassifyAsync(
        string question,
        CancellationToken cancellationToken = default);
}

public interface IAskHisabDoDataService
{
    Task<VerifiedFinancialContext> GetVerifiedContextAsync(
        Guid userId,
        AskHisabDoIntent intent,
        string question,
        CancellationToken cancellationToken = default);
}

public sealed class AskHisabDoService
{
    private readonly IAskHisabDoIntentClassifier _classifier;
    private readonly IAskHisabDoDataService _dataService;

    public AskHisabDoService(
        IAskHisabDoIntentClassifier classifier,
        IAskHisabDoDataService dataService)
    {
        _classifier = classifier;
        _dataService = dataService;
    }

    public async Task<AskHisabDoResponse> AskAsync(
        AskHisabDoRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId == Guid.Empty)
            throw new ArgumentException("Authenticated UserId is required.");

        if (string.IsNullOrWhiteSpace(request.Question))
            return new AskHisabDoResponse(
                "Please provide a financial question.",
                Array.Empty<string>(), false);

        var intent = await _classifier.ClassifyAsync(
            request.Question, cancellationToken);

        if (intent == AskHisabDoIntent.Unsupported)
            return new AskHisabDoResponse(
                "I can answer questions about your HisabDo income, expenses, spending patterns, budgets, financial health, anomalies, forecasts and verified recommendations.",
                Array.Empty<string>(), false);

        if (intent == AskHisabDoIntent.ClarificationRequired)
            return new AskHisabDoResponse(
                "I need a little more detail to answer accurately. Please specify the period or category.",
                Array.Empty<string>(), false);

        var context = await _dataService.GetVerifiedContextAsync(
            request.UserId, intent, request.Question, cancellationToken);

        if (!context.HasVerifiedData)
            return new AskHisabDoResponse(
                "The requested information is not available from verified HisabDo data. I will not estimate a missing financial value.",
                context.Limitations, false);

        // LLM integration boundary:
        // Send ONLY verified AnswerContext + Limitations to the selected LLM.
        // Strict prompt rules:
        // 1. Answer only from verified context.
        // 2. Never invent/change financial amounts.
        // 3. Never calculate replacement source-of-truth values.
        // 4. Never guess missing data.
        // 5. Mention relevant limitations.
        //
        // Until an LLM provider is connected, this safe implementation returns
        // the verified backend context directly.
        return new AskHisabDoResponse(
            context.AnswerContext,
            context.Limitations,
            true);
    }
}

// Example mapping:
// "How much did I spend?" -> IncomeExpense
// "Where is most of my money going?" -> SpendingPattern
// "Am I overspending?" -> Budget
// "What is my score?" -> FinancialHealth
// "Any unusual spending?" -> Anomaly
// "What will I spend next month?" -> CashFlowForecast
// "What budget should I use?" -> BudgetRecommendation
//
// Production note:
// Map IAskHisabDoDataService to the real HisabDo repositories/services,
// authentication and Day 10–14 verified calculation engines.
// Do not duplicate financial calculations inside this service.
