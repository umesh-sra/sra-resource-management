using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SraRms.Api.Controllers;

[ApiController]
[Authorize] // deny anonymous by default; actions further restrict by policy
public abstract class BaseApiController : ControllerBase
{
    // Return ActionResult (not IActionResult) so these implicitly convert to ActionResult<T>.
    protected ActionResult NotFoundProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: detail);

    protected ActionResult ConflictProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: detail);

    protected ActionResult BadRequestProblem(string detail) =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail);
}
