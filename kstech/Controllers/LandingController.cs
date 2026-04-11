using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace kstech.Controllers
{
    [AllowAnonymous]
    public class LandingController : Controller
    {
        [HttpGet]
        [Route("")]
        [Route("Landing")]
        public IActionResult Index()
        {
            return View();
        }
    }
}
