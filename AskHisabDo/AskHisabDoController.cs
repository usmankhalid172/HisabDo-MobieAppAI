using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.Forecasting;

[ApiController]
[Route("api/ask-hisabdo")]
[Authorize]
public sealed class AskHisabDoController(AskHisabDoService service) : ControllerBase
{
    [HttpPost("context")]
    [ProducesResponseType(typeof(AskHisabDoResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AskHisabDoResponse>> CreateContext(
        [FromBody] AskHisabDoRequest request,
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claim, out var authenticatedUserId))
            return Unauthorized(new { error = "An authenticated user ID is required." });

        try
        {
            return Ok(await service.BuildContextAsync(request, authenticatedUserId, cancellationToken));
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
