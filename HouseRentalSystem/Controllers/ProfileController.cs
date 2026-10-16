using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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
    // Self-service account management for any signed-in user, regardless of role - update
    // your own details, change your password, or set a profile picture.
    [Authorize]
    public class ProfileController : Controller
    {
        private static readonly string[] AllowedPhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxPhotoBytes = 5 * 1024 * 1024;
        private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
        private const int MaxCodeAttempts = 5;

        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailSender _emailSender;
        private readonly PasswordHasher<User> _hasher = new PasswordHasher<User>();

        public ProfileController(ApplicationDbContext context, IWebHostEnvironment environment, IEmailSender emailSender)
        {
            _context = context;
            _environment = environment;
            _emailSender = emailSender;
        }

        // GET: /Profile
        public async Task<IActionResult> Index()
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            return View(new UpdateProfileViewModel
            {
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                DateOfBirth = user.DateOfBirth,
                ProfilePhoto = user.ProfilePhoto
            });
        }

        // POST: /Profile/UpdateDetails
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateDetails(UpdateProfileViewModel model)
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                // Email/ProfilePhoto are display-only and never posted back, so they'd
                // otherwise come back blank when redisplaying the form after an error.
                model.Email = user.Email;
                model.ProfilePhoto = user.ProfilePhoto;
                return View(nameof(Index), model);
            }

            user.FullName = model.FullName.Trim();
            user.Phone = model.Phone.Trim();
            user.DateOfBirth = model.DateOfBirth;
            await _context.SaveChangesAsync();

            // The header shows the name from the sign-in cookie, not a fresh DB read each
            // page load - re-issue it so a name change is reflected immediately.
            await RefreshSignInAsync(user);

            TempData["Notification"] = "Your details have been updated.";
            return RedirectToAction(nameof(Index));
        }

        // GET: /Profile/ChangePassword
        // Emails a fresh verification code the first time this is reached (or once the
        // previous one has expired) rather than on every page load, so refreshing the page
        // doesn't spam a new email each time.
        public async Task<IActionResult> ChangePassword()
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            var needsFreshCode = user.PasswordChangeCode == null
                || user.PasswordChangeCodeExpiry == null
                || user.PasswordChangeCodeExpiry.Value < DateTime.Now;

            if (needsFreshCode)
            {
                SetNewPasswordChangeCode(user);
                await _context.SaveChangesAsync();
                await SendPasswordChangeCodeEmailAsync(user);
            }

            return View(new ChangePasswordViewModel());
        }

        // POST: /Profile/ChangePassword
        // The code is checked before anything else - a wrong or expired code leaves the
        // password untouched no matter what was entered for the new password.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (user.PasswordChangeCode == null || user.PasswordChangeCodeExpiry == null || user.PasswordChangeCodeExpiry.Value < DateTime.Now)
            {
                ModelState.AddModelError(nameof(model.Code), "That code has expired. Request a new one below.");
                return View(model);
            }

            // Cap wrong guesses so the 6-digit code can't be brute-forced before it expires.
            if (user.PasswordChangeCodeAttempts >= MaxCodeAttempts)
            {
                user.PasswordChangeCode = null;
                user.PasswordChangeCodeExpiry = null;
                user.PasswordChangeCodeSentAt = null;
                await _context.SaveChangesAsync();
                ModelState.AddModelError(nameof(model.Code), "Too many incorrect attempts. Request a new code below.");
                return View(model);
            }

            if (user.PasswordChangeCode != model.Code)
            {
                user.PasswordChangeCodeAttempts++;
                await _context.SaveChangesAsync();
                ModelState.AddModelError(nameof(model.Code), "That code isn't correct.");
                return View(model);
            }

            user.PasswordHash = _hasher.HashPassword(user, model.NewPassword);
            user.PasswordChangeCode = null;
            user.PasswordChangeCodeExpiry = null;
            user.PasswordChangeCodeSentAt = null;
            user.PasswordChangeCodeAttempts = 0;
            await _context.SaveChangesAsync();

            TempData["Notification"] = "Your password has been changed.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Profile/ResendPasswordChangeCode
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendPasswordChangeCode()
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            if (user.PasswordChangeCodeSentAt.HasValue && DateTime.Now - user.PasswordChangeCodeSentAt.Value < ResendCooldown)
            {
                TempData["Notification"] = "Please wait a minute before requesting another code.";
                return RedirectToAction(nameof(ChangePassword));
            }

            SetNewPasswordChangeCode(user);
            await _context.SaveChangesAsync();
            await SendPasswordChangeCodeEmailAsync(user);

            TempData["Notification"] = "A new verification code has been sent.";
            return RedirectToAction(nameof(ChangePassword));
        }

        // POST: /Profile/UploadPhoto
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadPhoto(IFormFile photo)
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            if (photo == null || photo.Length == 0)
            {
                TempData["Notification"] = "Choose a photo to upload.";
                return RedirectToAction(nameof(Index));
            }

            if (photo.Length > MaxPhotoBytes)
            {
                TempData["Notification"] = "That photo is too large - the limit is 5 MB.";
                return RedirectToAction(nameof(Index));
            }

            var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!AllowedPhotoExtensions.Contains(extension))
            {
                TempData["Notification"] = "Please upload a JPG, PNG, GIF or WEBP image.";
                return RedirectToAction(nameof(Index));
            }

            DeletePhotoFile(user.ProfilePhoto);

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "profiles", user.UserID.ToString());
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await photo.CopyToAsync(stream);
            }

            user.ProfilePhoto = $"/uploads/profiles/{user.UserID}/{fileName}";
            await _context.SaveChangesAsync();
            await RefreshSignInAsync(user);

            TempData["Notification"] = "Profile picture updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Profile/RemovePhoto
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePhoto()
        {
            var user = await CurrentUserAsync();
            if (user == null)
            {
                return NotFound();
            }

            DeletePhotoFile(user.ProfilePhoto);
            user.ProfilePhoto = string.Empty;
            await _context.SaveChangesAsync();
            await RefreshSignInAsync(user);

            TempData["Notification"] = "Profile picture removed.";
            return RedirectToAction(nameof(Index));
        }

        private static void SetNewPasswordChangeCode(User user)
        {
            user.PasswordChangeCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            user.PasswordChangeCodeExpiry = DateTime.Now.Add(CodeLifetime);
            user.PasswordChangeCodeSentAt = DateTime.Now;
            user.PasswordChangeCodeAttempts = 0;
        }

        private async Task SendPasswordChangeCodeEmailAsync(User user)
        {
            var subject = "Confirm your RentEasy password change";
            var body = $"Hi {user.FullName},\n\n" +
                       $"Your verification code to change your password is: {user.PasswordChangeCode}\n\n" +
                       $"This code expires in {CodeLifetime.TotalMinutes:0} minutes. " +
                       "If you didn't request this, you can ignore this email - your password won't change.\n\n" +
                       "- RentEasy";
            await _emailSender.SendAsync(user.Email, subject, body);
        }

        private void DeletePhotoFile(string? profilePhoto)
        {
            if (string.IsNullOrEmpty(profilePhoto))
            {
                return;
            }

            var relativePath = profilePhoto.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var filePath = Path.Combine(_environment.WebRootPath, relativePath);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        private async Task<User?> CurrentUserAsync()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            return await _context.Users.FindAsync(userId);
        }

        private async Task RefreshSignInAsync(User user)
        {
            var identity = AuthClaims.BuildIdentity(user);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        }
    }
}
