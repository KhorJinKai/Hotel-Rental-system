using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using HouseRentalSystem.Data;
using HouseRentalSystem.Models;
using HouseRentalSystem.Services;

namespace HouseRentalSystem.Controllers
{
    public class SecurityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;
        private readonly CaptchaService _captcha;
        // PasswordHasher is just a hashing utility from the Identity package -
        // this is NOT the full ASP.NET Core Identity membership system.
        // Login/logout/roles below are all implemented manually with cookies,
        // per the assignment's requirement to avoid the Identity framework.
        private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>();

        private const int MaxFailedLoginAttempts = 5;
        private const int MaxCodeAttempts = 5;
        private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan VerificationCodeLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan PasswordResetResendCooldown = TimeSpan.FromSeconds(60);

        public SecurityController(ApplicationDbContext context, IEmailSender emailSender, CaptchaService captcha)
        {
            _context = context;
            _emailSender = emailSender;
            _captcha = captcha;
        }

        // GET: /Security/Login
        public IActionResult Login(string returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        // POST: /Security/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

            if (user == null || !user.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(model);
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.Now)
            {
                var minutesLeft = Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes);
                ModelState.AddModelError(string.Empty,
                    $"This account is temporarily locked due to too many failed login attempts. Try again in {minutesLeft} minute(s).");
                return View(model);
            }

            // VerifyHashedPassword throws FormatException (rather than returning Failed) if the
            // stored hash isn't valid Base64 - e.g. a row whose PasswordHash was hand-edited in
            // the database. Treat a corrupt hash as a failed login instead of a 500.
            PasswordVerificationResult result;
            try
            {
                result = _hasher.VerifyHashedPassword(user, user.PasswordHash, model.Password);
            }
            catch (FormatException)
            {
                result = PasswordVerificationResult.Failed;
            }

            if (result == PasswordVerificationResult.Failed)
            {
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
                {
                    user.LockoutEnd = DateTime.Now.Add(LockoutDuration);
                    user.FailedLoginAttempts = 0;
                    await _context.SaveChangesAsync();

                    ModelState.AddModelError(string.Empty,
                        $"Too many failed login attempts. This account has been locked for {(int)LockoutDuration.TotalMinutes} minutes.");
                    return View(model);
                }

                await _context.SaveChangesAsync();
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                return View(model);
            }

            // Successful login - clear any prior failure tracking.
            if (user.FailedLoginAttempts != 0 || user.LockoutEnd.HasValue)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
                await _context.SaveChangesAsync();
            }

            // Password is correct at this point, so it's safe to reveal that this
            // account still needs email verification and send them to finish it.
            if (!user.EmailVerified)
            {
                TempData["VerifyInfo"] = "Please verify your email before signing in.";
                return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
            }

            var identity = AuthClaims.BuildIdentity(user);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        // GET: /Security/Register
        public IActionResult Register()
        {
            return View(new RegisterViewModel { DateOfBirth = DateTime.Today });
        }

        // POST: /Security/Register
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!_captcha.Verify(HttpContext.Session, model.Captcha))
            {
                ModelState.AddModelError(nameof(model.Captcha),
                    "The characters you entered don't match the image. Please try again.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existing = await _context.Users.AnyAsync(u => u.Email == model.Email);
            if (existing)
            {
                ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
                return View(model);
            }

            // Admin accounts are not self-registrable - only Owner/Tenant.
            if (model.Role != UserRole.Owner && model.Role != UserRole.Tenant)
            {
                ModelState.AddModelError(nameof(model.Role), "Invalid account type selected.");
                return View(model);
            }

            var user = new User
            {
                FullName = model.FullName,
                Email = model.Email,
                Phone = model.Phone,
                DateOfBirth = model.DateOfBirth,
                Role = model.Role,
                ProfilePhoto = "",
                IsActive = true,
                EmailVerified = false,
                CreatedDate = DateTime.Now
            };
            user.PasswordHash = _hasher.HashPassword(user, model.Password);
            SetNewVerificationCode(user);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await SendVerificationEmailAsync(user);

            return RedirectToAction(nameof(VerifyEmail), new { email = user.Email });
        }

        // GET: /Security/VerifyEmail
        public async Task<IActionResult> VerifyEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return RedirectToAction(nameof(Register));
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || user.EmailVerified)
            {
                return RedirectToAction(nameof(Login));
            }

            return View(new VerifyEmailViewModel { Email = email });
        }

        // POST: /Security/VerifyEmail
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmail(VerifyEmailViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "We couldn't find an account for that email.");
                return View(model);
            }

            if (user.EmailVerified)
            {
                TempData["RegisterSuccess"] = "Your email is already verified. You can sign in.";
                return RedirectToAction(nameof(Login));
            }

            if (user.VerificationCode == null || user.VerificationCodeExpiry == null ||
                user.VerificationCodeExpiry.Value < DateTime.Now)
            {
                ModelState.AddModelError(string.Empty, "That code has expired. Request a new one below.");
                return View(model);
            }

            // Cap wrong guesses so the 6-digit code can't be brute-forced before it expires.
            if (user.VerificationCodeAttempts >= MaxCodeAttempts)
            {
                user.VerificationCode = null;
                user.VerificationCodeExpiry = null;
                user.VerificationCodeSentAt = null;
                await _context.SaveChangesAsync();
                ModelState.AddModelError(string.Empty, "Too many incorrect attempts. Request a new code below.");
                return View(model);
            }

            if (user.VerificationCode != model.Code)
            {
                user.VerificationCodeAttempts++;
                await _context.SaveChangesAsync();
                ModelState.AddModelError(nameof(model.Code), "That code isn't correct.");
                return View(model);
            }

            user.EmailVerified = true;
            user.VerificationCode = null;
            user.VerificationCodeExpiry = null;
            user.VerificationCodeSentAt = null;
            user.VerificationCodeAttempts = 0;
            await _context.SaveChangesAsync();

            TempData["RegisterSuccess"] = "Email verified! You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        // POST: /Security/ResendVerificationCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendVerificationCode(string email)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user == null || user.EmailVerified)
            {
                return RedirectToAction(nameof(Login));
            }

            if (user.VerificationCodeSentAt.HasValue &&
                DateTime.Now - user.VerificationCodeSentAt.Value < ResendCooldown)
            {
                TempData["VerifyError"] = "Please wait a minute before requesting another code.";
                return RedirectToAction(nameof(VerifyEmail), new { email });
            }

            SetNewVerificationCode(user);
            await _context.SaveChangesAsync();
            await SendVerificationEmailAsync(user);

            TempData["VerifyInfo"] = "A new verification code has been sent.";
            return RedirectToAction(nameof(VerifyEmail), new { email });
        }

        private static void SetNewVerificationCode(User user)
        {
            user.VerificationCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            user.VerificationCodeExpiry = DateTime.Now.Add(VerificationCodeLifetime);
            user.VerificationCodeSentAt = DateTime.Now;
            user.VerificationCodeAttempts = 0;
        }

        private async Task SendVerificationEmailAsync(User user)
        {
            var subject = "Verify your RentEasy email address";
            var body = $"Hi {user.FullName},\n\n" +
                       $"Your RentEasy verification code is: {user.VerificationCode}\n\n" +
                       $"This code expires in {VerificationCodeLifetime.TotalMinutes:0} minutes. " +
                       "If you didn't create this account, you can ignore this email.\n\n" +
                       "- RentEasy";
            await _emailSender.SendAsync(user.Email, subject, body);
        }

        // GET: /Security/ForgotPassword
        public IActionResult ForgotPassword()
        {
            return View(new ForgotPasswordViewModel());
        }

        // POST: /Security/ForgotPassword
        // Always shows the same confirmation regardless of whether the email exists, so the
        // form can't be used to discover which addresses have an account (user enumeration).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!_captcha.Verify(HttpContext.Session, model.Captcha))
            {
                ModelState.AddModelError(nameof(model.Captcha),
                    "The characters you entered don't match the image. Please try again.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user != null && user.IsActive)
            {
                var tooSoon = user.PasswordResetSentAt.HasValue
                    && DateTime.Now - user.PasswordResetSentAt.Value < PasswordResetResendCooldown;

                if (!tooSoon)
                {
                    user.PasswordResetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    user.PasswordResetTokenExpiry = DateTime.Now.Add(PasswordResetTokenLifetime);
                    user.PasswordResetSentAt = DateTime.Now;
                    await _context.SaveChangesAsync();

                    var resetUrl = Url.Action(nameof(ResetPassword), "Security",
                        new { email = user.Email, token = user.PasswordResetToken }, Request.Scheme);

                    var body = $"Hi {user.FullName},\n\n" +
                               "We received a request to reset your RentEasy password. Click the link below to choose a new one:\n\n" +
                               $"{resetUrl}\n\n" +
                               $"This link expires in {PasswordResetTokenLifetime.TotalMinutes:0} minutes. " +
                               "If you didn't request this, you can ignore this email - your password won't change.\n\n" +
                               "- RentEasy";
                    await _emailSender.SendAsync(user.Email, "Reset your RentEasy password", body);
                }
            }

            return View("ForgotPasswordConfirmation");
        }

        // GET: /Security/ResetPassword?email=...&token=...
        // Reached from the emailed link. Validates the token up front so an expired or
        // already-used link fails fast instead of on submit.
        public async Task<IActionResult> ResetPassword(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token) || !await IsResetTokenValidAsync(email, token))
            {
                return View("ResetPasswordInvalid");
            }

            return View(new ResetPasswordViewModel { Email = email, Token = token });
        }

        // POST: /Security/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!await IsResetTokenValidAsync(model.Email, model.Token))
            {
                return View("ResetPasswordInvalid");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

            user.PasswordHash = _hasher.HashPassword(user, model.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiry = null;
            user.PasswordResetSentAt = null;
            // A reset proves the requester currently controls the mailbox, so lift any
            // lockout from earlier failed login attempts too.
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _context.SaveChangesAsync();

            TempData["RegisterSuccess"] = "Your password has been reset. You can now sign in.";
            return RedirectToAction(nameof(Login));
        }

        private async Task<bool> IsResetTokenValidAsync(string email, string token)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            return user != null
                && user.PasswordResetToken != null
                && user.PasswordResetToken == token
                && user.PasswordResetTokenExpiry.HasValue
                && user.PasswordResetTokenExpiry.Value > DateTime.Now;
        }

        // POST: /Security/Logout
        // POST-only with an anti-forgery token so a cross-site request can't sign the user out.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Notification"] = "You have been logged out.";
            return RedirectToAction("Index", "Home");
        }

        // GET: /Security/AccessDenied
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
