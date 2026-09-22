using System.Threading;
using System.Threading.Tasks;

namespace HisabDo.AI.Day15;

// NOTE FOR THE TEAM: Omesha's classifier (QueryClassificationService.cs,
// FinancialQueryCategory / AskHisabDoQueryClassifier) and Taha's
// orchestration service (AskHisabDoAIService.cs, AskHisabDoIntent /
// IAskHisabDoIntentClassifier) were built independently and were never
// connected — their enums even use slightly different names for the same
// category (SpendingCategory vs. SpendingPattern). This adapter is the
// missing glue. Please confirm the naming mismatch is intentional or
// reconcile the two enums directly in a future pass.

public sealed class AskHisabDoIntentClassifierAdapter : IAskHisabDoIntentClassifier
{
    private readonly IFinancialQueryClassifier _classifier;

    public AskHisabDoIntentClassifierAdapter(IFinancialQueryClassifier classifier)
    {
        _classifier = classifier;
    }

    public Task<AskHisabDoIntent> ClassifyAsync(string question, CancellationToken cancellationToken = default)
    {
        var result = _classifier.Classify(question);
        return Task.FromResult(Map(result.Category));
    }

    public static AskHisabDoIntent Map(FinancialQueryCategory category) => category switch
    {
        FinancialQueryCategory.IncomeExpense => AskHisabDoIntent.IncomeExpense,
        FinancialQueryCategory.SpendingCategory => AskHisabDoIntent.SpendingPattern,
        FinancialQueryCategory.Budget => AskHisabDoIntent.Budget,
        FinancialQueryCategory.FinancialHealth => AskHisabDoIntent.FinancialHealth,
        FinancialQueryCategory.Anomaly => AskHisabDoIntent.Anomaly,
        FinancialQueryCategory.CashFlowForecast => AskHisabDoIntent.CashFlowForecast,
        FinancialQueryCategory.BudgetRecommendation => AskHisabDoIntent.BudgetRecommendation,
        FinancialQueryCategory.Ambiguous => AskHisabDoIntent.ClarificationRequired,
        _ => AskHisabDoIntent.Unsupported,
    };
}
