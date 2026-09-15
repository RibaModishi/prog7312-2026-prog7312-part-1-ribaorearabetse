using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Services;
using SmartX.Shared.Contracts;

namespace SmartX.Api.Controllers;

/// <summary>
/// Dashboard read of the currently open alert groups. The grouping has already happened at ingest
/// time, so this endpoint is a snapshot rather than a query that recomputes windows on the way out.
/// </summary>
[ApiController]
[Route("api/alerts")]
public class AlertsController(AlertAggregationService alerts) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<AlertGroup>> GetActive() => Ok(alerts.GetActiveGroups());
}
