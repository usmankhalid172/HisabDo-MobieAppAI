using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.AI.Day16;

// NOTE FOR THE TEAM: TahaRecommendationEngine (this file's companion,
// RecommendationEngine.cs) is used completely unmodified — every rule,
// threshold and ordering is exactly as written in the spec's reference
// implementation. This controller only adds the missing HTTP layer and a
// demo data aggregator (RecommendationDemoDataAggregator.cs) standing in
// for Jaffer's real Day 10-14 integration work. Please review/take
// ownership of the aggregation step before merging — the engine itself
// needs no changes.

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class RecommendationController(
    TahaRecommendationEngine engine,
    RecommendationDemoDataAggregator dataAggregator) : ControllerBase
{
    [HttpGet("recommendations/{userId}")]
    public ActionResult<RecommendationResult> GetRecommendations(string userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(claim) || !string.Equals(claim, userId, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "An authenticated user ID matching the requested userId is required." });

        try
        {
            var input = dataAggregator.BuildInput(userId);
            var result = engine.Generate(input);
            return Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            // Covers both the engine's own "UserId is required" guard and
            // the aggregator's "no demo data on file" guard.
            return NotFound(new { error = exception.Message });
        }
    }
}
