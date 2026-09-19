using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.AI.Day14;

// NOTE FOR THE TEAM: same situation as Day 12 and Day 13 — this repo had
// BudgetRecommendationService.cs (Taha's real MVP methodology) and
// TahaAIBudgetExplanationService.cs (Taha's real, deterministic
// explanation layer — not an LLM call) as plain classes with no HTTP
// controller connecting either to anything. Written here to complete
// that wiring so this could actually be run and tested end-to-end.
// Please review/take ownership before merging — same integration-boundary
// caveat as the spec itself (Section 4: "Actual HisabDo schema must be
// mapped before production").
//
// There's also a second, near-duplicate file in the repo
// (Budge REcommendation/Day14_Taha_BudgetRecommendation_Methodology.cs)
// defining the same types as records instead of classes. Not used here —
// flagging as a repo hygiene note in the task report.

public sealed record CategoryRecommendationDto(
    string Category, decimal HistoricalAverage, decimal? ExistingBudget,
    decimal RecommendedBudget, decimal? BudgetVariance, decimal? BudgetUtilizationPercent,
    string RecommendationReason, string DataSufficiency, IReadOnlyList<string> Limitations,
    bool IsOverspending, BudgetAIExplanation Explanation);

public sealed record BudgetRecommendationResponse(
    Guid UserId, string AnalysisPeriodStart, string AnalysisPeriodEnd,
    IReadOnlyList<CategoryRecommendationDto> Categories,
    IReadOnlyList<string> OverspendingCategories,
    IReadOnlyList<string> ActionableSuggestions);

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class BudgetRecommendationController(
    BudgetRecommendationService recommendationService,
    TahaAIBudgetExplanationService explanationService) : ControllerBase
{
    [HttpGet("budget-recommendations/{userId}")]
    public async Task<ActionResult<BudgetRecommendationResponse>> GetRecommendations(
        string userId,
        [FromQuery] string? start,
        [FromQuery] string? end,
        [FromQuery] int months = 3,
        CancellationToken cancellationToken = default)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(claim) || !string.Equals(claim, userId, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "An authenticated user ID matching the requested userId is required." });

        if (!Guid.TryParse(userId, out var userGuid))
            return BadRequest(new { error = "userId must be a valid GUID." });

        DateTime analysisStart, analysisEnd;
        if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
        {
            if (!DateTime.TryParse(start, out analysisStart) || !DateTime.TryParse(end, out analysisEnd))
                return BadRequest(new { error = "start/end must be valid dates (YYYY-MM-DD)." });
        }
        else
        {
            var today = DateTime.UtcNow.Date;
            var firstOfCurrentMonth = new DateTime(today.Year, today.Month, 1);
            analysisEnd = firstOfCurrentMonth.AddDays(-1);          // last day of the previous month
            analysisStart = firstOfCurrentMonth.AddMonths(-months); // first day of the month `months` back
        }

        try
        {
            var result = await recommendationService.GenerateAsync(userGuid, analysisStart, analysisEnd, cancellationToken);

            var enriched = result.Recommendations.Select(r =>
            {
                var verified = new VerifiedBudgetRecommendation(
                    r.Category, r.HistoricalAverage, r.ExistingBudget, r.RecommendedBudget,
                    r.BudgetVariance, r.BudgetUtilizationPercent, r.RecommendationReason,
                    r.DataSufficiency, r.Limitations);
                var explanation = explanationService.BuildExplanation(verified);
                var isOverspending = r.ExistingBudget.HasValue && r.HistoricalAverage > r.ExistingBudget.Value;

                return new CategoryRecommendationDto(
                    r.Category, r.HistoricalAverage, r.ExistingBudget, r.RecommendedBudget,
                    r.BudgetVariance, r.BudgetUtilizationPercent, r.RecommendationReason,
                    r.DataSufficiency, r.Limitations, isOverspending, explanation);
            }).ToList();

            var overspending = enriched.Where(c => c.IsOverspending).Select(c => c.Category).ToList();
            var actions = enriched.Select(c => c.Explanation.Action).Distinct().ToList();

            return Ok(new BudgetRecommendationResponse(
                userGuid, analysisStart.ToString("yyyy-MM-dd"), analysisEnd.ToString("yyyy-MM-dd"),
                enriched, overspending, actions));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
