using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataVerification.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health")]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Liveness probe. Returns 200 while the API process is able to serve requests. Explicitly
    /// anonymous — load balancers and container orchestrators call it without credentials.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new
    {
        status = "Healthy",
        service = "DataVerification.API",
        version = typeof(HealthController).Assembly.GetName().Version?.ToString(),
        utcNow = DateTime.UtcNow,
    });
}
