using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using HouseRentalSystem.Models;

namespace HouseRentalSystem.Services
{
    // The one place the sign-in cookie's claims are assembled, so SecurityController.Login
    // and ProfileController (which re-issues the cookie whenever the name or photo changes,
    // so the header updates without forcing a re-login) can't drift apart.
    public static class AuthClaims
    {
        public const string ProfilePhotoClaimType = "ProfilePhoto";

        public static ClaimsIdentity BuildIdentity(User user)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(ProfilePhotoClaimType, user.ProfilePhoto ?? string.Empty)
            };
            return new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
