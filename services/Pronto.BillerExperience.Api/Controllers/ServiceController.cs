using Microsoft.AspNetCore.Mvc;

namespace Pronto.BillerExperience.Api.Controllers;

[ApiController]
[Route("")]
public sealed class ServiceController : ControllerBase
{
    [HttpGet]
    public ActionResult<ServiceInfo> Get() =>
        Ok(new ServiceInfo("Pronto.BillerExperience.Api", "ready", "Biller onboarding and experience publication"));
}
