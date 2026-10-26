using Microsoft.AspNetCore.Mvc;

namespace BaseWebServer.Api.Common.Api;

// Auth skipped for v1: no [Authorize] until JWT is wired up.
[ApiController]
[Route("api/[controller]")]
public abstract class ApiControllerBase : ControllerBase;
