using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LuxuryApp.Controllers
{
    [ApiController]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("api/health/public-callback")]
    public class PublicCallbackHealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() =>
            Ok(new
            {
                status = "ok",
                utc = DateTime.UtcNow
            });
    }
}
