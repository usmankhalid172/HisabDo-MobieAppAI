using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day15;

/// <summary>
/// Day 15 — Aarti: Ask HisabDo AI Explanation Layer.
/// This layer converts VERIFIED backend data into natural language.
/// It must never calculate, alter, or invent financial values.
/// </summary>

public sealed record VerifiedFinancialContext
{
    public required string UserId { get; init; }
    public required string Intent { get; init; }
    public required string AnswerContext { get; init; }
    public IReadOnlyList<string> VerifiedFacts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    public bool HasSufficientData { get; init; }
}

public sealed record AskHisabDoAiResponse
{
    public required string Summary { get; init; }
    public required string Answer { get; init; }
    public IReadOnlyList<string> KeyFacts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();
    public bool UsedVerifiedDataOnly { get; init; }
}

public interface IAskHisabDoLlm
{
    Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public sealed class AskHisabDoAiExplanationService
{
    private readonly IAskHisabDoLlm _llm;

    public AskHisabDoAiExplanationService(IAskHisabDoLlm llm)
    {
        _llm = llm;
    }

    public async Task<AskHisabDoAiResponse> AnswerAsync(
        string userQuestion,
        VerifiedFinancialContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userQuestion))
            throw new ArgumentException("Question is required.", nameof(userQuestion));

        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(context.UserId))
            throw new InvalidOperationException("Authenticated UserId is required.");

        // Do not let the LLM answer if verified backend data is unavailable.
        if (!context.HasSufficientData)
        {
            var limitation = context.Limitations.Count > 0
                ? string.Join(" ", context.Limitations)
                : "There is not enough verified HisabDo data to answer this question.";

            return new AskHisabDoAiResponse
            {
                Summary = "I need more verified data to answer this safely.",
                Answer = limitation,
                KeyFacts = context.VerifiedFacts,
                Limitations = context.Limitations,
                UsedVerifiedDataOnly = true
            };
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(userQuestion, context);

        var generated = await _llm.GenerateAsync(
            systemPrompt,
            userPrompt,
            cancellationToken);

        // Production recommendation:
        // Validate the generated response against the verified facts before returning it.
        // A response containing unsupported financial values must be rejected/regenerated.
        var safeAnswer = SanitizeAgainstVerifiedFacts(generated, context);

        return new AskHisabDoAiResponse
        {
            Summary = BuildSafeSummary(context),
            Answer = safeAnswer,
            KeyFacts = context.VerifiedFacts,
            Limitations = context.Limitations,
            UsedVerifiedDataOnly = true
        };
    }

    private static string BuildSystemPrompt() =>
"""
You are Ask HisabDo AI, the explanation layer of HisabDo.

ROLE:
- Answer the user's financial question in simple, clear language.
- Use ONLY the verified financial data supplied by the backend.
- Explain verified values; do not calculate new financial values.
- Do not invent amounts, percentages, dates, categories, scores, transactions,
  forecasts, budgets, anomaly severity, or recommendations.
- Do not use outside financial data to fill missing HisabDo values.
- If required data is missing or insufficient, say so clearly.
- Never claim fraud, theft, or unauthorized activity unless the trusted backend
  explicitly provides that conclusion.
- Respect the detected intent and answer only what the supplied verified data supports.
- If the question is unsupported, say that HisabDo cannot answer it from the available data.
- Keep financial explanations simple and practical.

RESPONSE STRUCTURE:
1. Direct answer
2. Key verified facts
3. Simple explanation
4. Action/observation only when supported by verified data
5. Limitation/uncertainty when applicable

NO-INVENTION RULE:
Every financial number in the answer must exist in the supplied verified backend context.
Do not create a replacement number if a value is missing.
""";

    private static string BuildUserPrompt(
        string question,
        VerifiedFinancialContext context)
    {
        var facts = context.VerifiedFacts.Count == 0
            ? "None"
            : string.Join("\n- ", context.VerifiedFacts);

        var limitations = context.Limitations.Count == 0
            ? "None"
            : string.Join("\n- ", context.Limitations);

        return $"""
USER QUESTION:
{question}

DETECTED INTENT:
{context.Intent}

VERIFIED BACKEND CONTEXT:
{context.AnswerContext}

VERIFIED FACTS:
- {facts}

LIMITATIONS:
- {limitations}

INSTRUCTIONS:
Answer the user using only the verified context above.
Do not invent or recalculate any financial number.
If the verified context does not support an answer, explicitly state the limitation.
""";
    }

    private static string BuildSafeSummary(VerifiedFinancialContext context) =>
        $"Answer generated for verified intent: {context.Intent}.";

    private static string SanitizeAgainstVerifiedFacts(
        string generated,
        VerifiedFinancialContext context)
    {
        // Reference boundary only.
        // Production should implement structured output + numeric/entity validation.
        // If validation fails, reject the answer and regenerate with a stricter prompt.
        if (string.IsNullOrWhiteSpace(generated))
            return "I could not generate an answer from the verified data.";

        return generated.Trim();
    }
}

/// <summary>
/// Optional deterministic guard for important numeric facts.
/// In production, prefer structured LLM output and compare extracted numeric tokens
/// against an allow-list generated from the verified backend result.
/// </summary>
public static class AiGroundingRules
{
    public static readonly string[] ForbiddenBehaviors =
    {
        "Invent a financial amount",
        "Change a backend percentage",
        "Recalculate a verified score",
        "Create a forecast without a verified forecast result",
        "Create a budget without a verified recommendation",
        "Claim an anomaly not present in backend results",
        "Use another user's financial data",
        "Use external data to fill missing HisabDo values",
        "Present uncertainty as certainty"
    };
}
