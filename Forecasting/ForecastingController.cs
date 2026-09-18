using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.Forecasting;

[ApiController]
[Route("api/forecasting")]
[Authorize]
public sealed class ForecastingController(ForecastingService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ForecastResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ForecastResult>> Forecast(
        [FromBody] ForecastRequest request,
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claim, out var authenticatedUserId))
            return Unauthorized(new { error = "An authenticated user ID is required." });

        try
        {
            return Ok(await service.ForecastAsync(request, authenticatedUserId, cancellationToken));
        }
        catch (ForecastValidationException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
        catch (ForecastUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message });
        }
        catch (ForecastEngineException exception)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = exception.Message });
        }
    }
}