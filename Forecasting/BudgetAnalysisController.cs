using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HisabDo.Forecasting;

[ApiController]
[Route("api/budgets")]
[Authorize]
public sealed class BudgetAnalysisController(BudgetAnalysisService service) : ControllerBase
{
    [HttpPost("analysis")]
    [ProducesResponseType(typeof(BudgetAnalysisResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<BudgetAnalysisResult>> Analyze(
        [FromBody] BudgetAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(claim, out var authenticatedUserId))
            return Unauthorized(new { error = "An authenticated user ID is required." });

        try
        {
            return Ok(await service.AnalyzeAsync(request, authenticatedUserId, cancellationToken));
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