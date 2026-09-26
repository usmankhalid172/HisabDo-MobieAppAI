using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.AI.Day15;

// NOTE FOR THE TEAM: same situation as Days 12-14 — Omesha's classifier
// and Taha's orchestration service each existed as standalone C# files
// with no HTTP controller, and no implementation of
// IAskHisabDoDataService anywhere. This controller (plus
// AskHisabDoIntentClassifierAdapter.cs and AskDataService.cs alongside
// it) completes the wiring end to end: User Question -> Classifier ->
// Intent + Required Data -> Data Service -> AskHisabDoService -> Answer.
// Please review/take ownership before merging.

public sealed record AskHisabDoRequestDto(string Question);

public sealed record AskHisabDoApiResponse(
    string Category, double Confidence, string ClassificationReason,
    IReadOnlyList<string> RequiredData, string Answer,
    IReadOnlyList<VerifiedValue> VerifiedValues,
    IReadOnlyList<string> Limitations, bool IsVerified,
    bool IsUnsupported, bool NeedsClarification,
    IReadOnlyList<string> ClarificationPrompts);

[ApiController]
[Route("api/ai")]
[Authorize]
public sealed class AskHisabDoController(
    AskHisabDoService askHisabDoService,
    IFinancialQueryClassifier classifier,
    AskHisabDoDataService dataService) : ControllerBase
{
    [HttpPost("ask-hisabdo/{userId}")]
    public async Task<ActionResult<AskHisabDoApiResponse>> Ask(
        string userId, [FromBody] AskHisabDoRequestDto body, CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(claim) || !string.Equals(claim, userId, StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "An authenticated user ID matching the requested userId is required." });

        if (!Guid.TryParse(userId, out var userGuid))
            return BadRequest(new { error = "userId must be a valid GUID." });

        if (string.IsNullOrWhiteSpace(body?.Question))
            return BadRequest(new { error = "question is required." });

        try
        {
            // Rich classification metadata for the UI (category, confidence,
            // reason, required data) — Taha's AskHisabDoService below runs
            // its own internal classification for the actual answer flow;
            // calling the classifier here too is cheap and keeps this
            // controller from having to modify either teammate's sealed class.
            var classification = classifier.Classify(body.Question);

            var response = await askHisabDoService.AskAsync(
                new AskHisabDoRequest(userGuid, body.Question), cancellationToken);

            var verifiedValues = response.IsVerified
                ? dataService.BuildVerifiedValues(AskHisabDoIntentClassifierAdapter.Map(classification.Category), body.Question)
                : Array.Empty<VerifiedValue>();

            return Ok(new AskHisabDoApiResponse(
                classification.Category.ToString(),
                classification.Confidence,
                classification.Reason,
                classification.RequiredData,
                response.Answer,
                verifiedValues,
                response.Limitations,
                response.IsVerified,
                classification.Category == FinancialQueryCategory.Unsupported,
                classification.Category == FinancialQueryCategory.Ambiguous,
                classification.MissingClarification));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
