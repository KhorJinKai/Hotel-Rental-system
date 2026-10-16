using HouseRentalSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace HouseRentalSystem.Controllers
{
    // Serves the CAPTCHA image. The <img> on the sign-in / register / forgot-password
    // forms points here; every request mints a fresh challenge and stashes the answer
    // in Session for SecurityController to check on POST. The reload button on those
    // forms just re-points the <img> at this action with a cache-busting query string.
    public class CaptchaController : Controller
    {
        private readonly CaptchaService _captcha;

        public CaptchaController(CaptchaService captcha)
        {
            _captcha = captcha;
        }

        // GET: /Captcha/Image
        [HttpGet]
        public IActionResult Image()
        {
            var code = _captcha.NewChallenge(HttpContext.Session);
            var svg = _captcha.RenderSvg(code);

            // Never let a proxy or the browser reuse a challenge image.
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";

            return Content(svg, "image/svg+xml");
        }
    }
}
