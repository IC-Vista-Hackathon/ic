using Microsoft.AspNetCore.Mvc;

namespace IC.BillerExperience.Api.Controllers;

[ApiController]
[Route("")]
public sealed class ServiceController : ControllerBase
{
    [HttpGet]
    public ActionResult<ServiceInfo> Get() =>
        Ok(new ServiceInfo("IC.BillerExperience.Api", "ready", "Biller onboarding and experience publication"));
}
