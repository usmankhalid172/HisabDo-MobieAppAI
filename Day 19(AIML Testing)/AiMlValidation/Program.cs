using System;
using System.Threading.Tasks;
using HisabDo.AI.Validation;

Console.WriteLine("HisabDo AI/ML Validation — Day 19");
Console.WriteLine($"Run at (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");

await AnomalyDetectionValidation.RunAll();
await CashFlowForecastValidation.RunAll();
await BudgetRecommendationValidation.RunAll();
RecommendationEngineValidation.RunAll();
await SpendingPatternValidation.RunAll();

Report.PrintFinalSummary();

return Report.Failed == 0 ? 0 : 1;
