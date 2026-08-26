using Microsoft.AspNetCore.Mvc;
using RelicDealFinder.Services;

namespace RelicDealFinder.Controllers;

[ApiController]
[Route("api/v1")]
public class AppController( MarketService marketService, ILogger<AppController> logger) : ControllerBase
{
    [HttpPost]
    [Route("refresh")]
    public async Task<ActionResult<string>> RefreshRelics(CancellationToken ct)
    {
        logger.LogInformation("Refreshing database...");
        var success = await marketService.RefreshDatabase();
        if (!success)
        {
            logger.LogInformation("Database refresh failed.");
            return StatusCode(500, "Database request failed.");
        }

        logger.LogInformation("Database refresh success.");
        return Ok("Database has been refreshed.");
    }
}