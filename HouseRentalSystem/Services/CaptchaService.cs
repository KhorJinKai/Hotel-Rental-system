using System.Text;
using Microsoft.AspNetCore.Http;

namespace HouseRentalSystem.Services
{
    // A small self-contained CAPTCHA - no external API, no NuGet package, no network
    // call. The challenge text is generated server-side, kept in Session (never sent to
    // the browser as text), and drawn into a deliberately distorted SVG so it is awkward
    // for a script to read straight off the page. Wired into Register / Login /
    // ForgotPassword - see SecurityController and CaptchaController.
    public class CaptchaService
    {
        private const string SessionKey = "captcha_code";

        // 0/O/1/I/L are left out - they are ambiguous once the glyphs are rotated.
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        // Generates a fresh code, stores it in Session, and returns it so the caller
        // can render an image of it.
        public string NewChallenge(ISession session, int length = 5)
        {
            var rng = Random.Shared;
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = Alphabet[rng.Next(Alphabet.Length)];
            }

            var code = new string(chars);
            session.SetString(SessionKey, code);
            return code;
        }

        // One challenge = one attempt: the stored code is cleared whether or not it
        // matched, so a wrong guess (or a replayed answer) always needs a new image.
        public bool Verify(ISession session, string? answer)
        {
            var expected = session.GetString(SessionKey);
            session.Remove(SessionKey);

            return !string.IsNullOrWhiteSpace(expected)
                && !string.IsNullOrWhiteSpace(answer)
                && string.Equals(expected, answer.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Builds the distorted image: a noisy background, a few wavy lines, speckles,
        // then each character placed with its own random offset, rotation and size.
        public string RenderSvg(string code)
        {
            var rng = Random.Shared;
            const int width = 170;
            const int height = 56;

            var sb = new StringBuilder();
            sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' " +
                      $"viewBox='0 0 {width} {height}' role='img' aria-label='CAPTCHA image'>");
            sb.Append($"<rect width='{width}' height='{height}' fill='#eef1f5'/>");

            // Wavy distraction lines across the whole image.
            for (var i = 0; i < 4; i++)
            {
                var y1 = rng.Next(height);
                var y2 = rng.Next(height);
                var cy = rng.Next(-10, height + 10);
                sb.Append($"<path d='M0 {y1} Q {width / 2} {cy} {width} {y2}' " +
                          $"stroke='#{RandomColor(rng, 140, 205)}' stroke-width='1.5' fill='none'/>");
            }

            // Speckle noise.
            for (var i = 0; i < 45; i++)
            {
                sb.Append($"<circle cx='{rng.Next(width)}' cy='{rng.Next(height)}' " +
                          $"r='{rng.Next(1, 3)}' fill='#{RandomColor(rng, 110, 190)}'/>");
            }

            // The characters themselves.
            var step = (width - 24) / code.Length;
            for (var i = 0; i < code.Length; i++)
            {
                var x = 16 + i * step + rng.Next(-3, 4);
                var y = 38 + rng.Next(-5, 7);
                var rotate = rng.Next(-28, 29);
                var size = rng.Next(27, 34);
                var fill = "#" + RandomColor(rng, 25, 105);

                sb.Append($"<text x='{x}' y='{y}' " +
                          $"font-family='Georgia,\"Times New Roman\",serif' font-size='{size}' " +
                          $"font-weight='700' fill='{fill}' " +
                          $"transform='rotate({rotate} {x} {y})'>{code[i]}</text>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string RandomColor(Random rng, int min, int max)
        {
            int r = rng.Next(min, max), g = rng.Next(min, max), b = rng.Next(min, max);
            return $"{r:x2}{g:x2}{b:x2}";
        }
    }
}
