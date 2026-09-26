using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.AI.Day12.AnomalyDetection;

// NOTE FOR THE TEAM: this controller did not exist in the repo — only the
// service class (SpendingAnomalyDetectionService.cs, Jaffer/Omesha) and the
// explanation service (Day12_AI_Anomaly_Explanation_Service.cs, Aarti) did,
// with no HTTP layer connecting them to anything. Written here to complete
// the wiring per the spec's Section 11 recommended contract
// (GET /api/ai/anomalies/{userId}) so the UI has a real endpoint to
// integrate against and this could actually be run and tested. Please
// review/take ownership before merging to main — same integration-boundary
// caveat the spec itself calls out in Section 19 (IExpenseTransactionRepository
// needs to be mapped to the real HisabDo data model).

public sealed record AnomalyAlertDto(
    string? TransactionId, string Type, string Severity, decimal Amount,
    string? Category, DateTime Date, decimal? BaselineValue, decimal? DeviationPercent,
    string ReasonCode, bool IsDuplicateCandidate, AnomalyExplanation Explanation);

public sealed record AnomalyAlertsResponse(
    string UserId, PeriodDto AnalysisPeriod, int BaselinePeriods,
    IReadOnlyList<AnomalyAlertDto> Anomalies,
    IReadOnlyList<CategorySpikeResult> PeriodSpikes);

public sealed record PeriodDto(string Start, string End);

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class AnomalyController(
    SpendingAnomalyDetectionService detectionService,
    AnomalyExplanationService explanationService) : ControllerBase
{
    [HttpGet("anomalies/{userId}")]
    [ProducesResponseType(typeof(AnomalyAlertsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AnomalyAlertsResponse>> GetAnomalies(
        string userId,
        [FromQuery] DateOnly startDate,
        [FromQuery] DateOnly endDate,
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(claim) || claim != userId)
            return Unauthorized(new { error = "An authenticated user ID matching the requested userId is required." });

        try
        {
            var result = await detectionService.DetectAsync(userId, startDate, endDate, cancellationToken);

            var enriched = result.Anomalies.Select(a =>
            {
                var typeText = ToDisplayType(a.Type);
                var explanation = explanationService.CreateExplanation(new AnomalyExplanationInput(
                    typeText, a.Severity.ToString(), a.Category ?? "Uncategorized",
                    a.Amount, a.BaselineValue, a.DeviationPercent, a.Date, null, a.ReasonCode));

                return new AnomalyAlertDto(
                    a.TransactionId, typeText, a.Severity.ToString(), a.Amount, a.Category,
                    a.Date, a.BaselineValue, a.DeviationPercent, a.ReasonCode,
                    a.IsDuplicateCandidate, explanation);
            }).ToList();

            return Ok(new AnomalyAlertsResponse(
                result.UserId,
                new PeriodDto(result.AnalysisStart.ToString("yyyy-MM-dd"), result.AnalysisEnd.ToString("yyyy-MM-dd")),
                result.BaselinePeriods, enriched, result.PeriodSpikes));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    private static string ToDisplayType(AnomalyType type) => type switch
    {
        AnomalyType.UnusualAmount => "Unusual Amount",
        AnomalyType.SuddenSpendingSpike => "Sudden Spending Spike",
        AnomalyType.UnusualCategory => "Unusual Category",
        AnomalyType.DuplicateTransaction => "Duplicate Transaction",
        _ => type.ToString()
    };
}
