using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.Forecasting;

[ApiController]
[Route("api/recommendations")]
[Authorize]
public sealed class RecommendationController(RecommendationService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(RecommendationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecommendationResponse>> Generate(
        [FromBody] RecommendationRequest request,
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claim, out var authenticatedUserId))
            return Unauthorized(new { error = "An authenticated user ID is required." });

        try
        {
            return Ok(await service.GenerateAsync(request, authenticatedUserId, cancellationToken));
        }
        catch (ForecastValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (ForecastUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message });
        }
    }
}