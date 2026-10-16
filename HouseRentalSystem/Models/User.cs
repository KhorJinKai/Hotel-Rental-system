using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HouseRentalSystem.Models
{
    public enum UserRole
    {
        Admin,
        Owner,
        Tenant
    }

    public class User
    {
        [Key]
        public int UserID { get; set; }

        [Required, StringLength(100)]
        public string FullName { get; set; }

        [Required, EmailAddress, StringLength(150)]
        public string Email { get; set; }

        [Required, StringLength(200)]
        public string PasswordHash { get; set; }

        [Phone, StringLength(20)]
        public string Phone { get; set; }

        [Required]
        public UserRole Role { get; set; }

        [Required]
        [DataType(DataType.Date)]
        public DateTime DateOfBirth { get; set; }

        [StringLength(250)]
        public string ProfilePhoto { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        // Registration is blocked from signing in until this is true (see
        // SecurityController.Login/VerifyEmail). VerificationCode/Expiry hold the
        // most recently emailed code; both are cleared once verified.
        public bool EmailVerified { get; set; } = false;

        [StringLength(10)]
        public string? VerificationCode { get; set; }

        public DateTime? VerificationCodeExpiry { get; set; }

        // Lets ResendVerificationCode throttle re-sends instead of spamming the inbox.
        public DateTime? VerificationCodeSentAt { get; set; }

        // Wrong-guess counter for the current code. Once it hits the cap the code is
        // discarded and a new one must be requested, so a 6-digit code can't be
        // brute-forced within its lifetime. Reset whenever a fresh code is issued.
        public int VerificationCodeAttempts { get; set; }

        // Account lockout tracking - incremented on each failed login attempt,
        // reset on success. LockoutEnd is set once the failure count hits the
        // lockout threshold and cleared automatically once it expires.
        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutEnd { get; set; }

        // Forgot-password token - a random hex string emailed as a reset link (never the
        // password itself). Cleared once used, per SecurityController.ResetPassword.
        [StringLength(100)]
        public string? PasswordResetToken { get; set; }

        public DateTime? PasswordResetTokenExpiry { get; set; }

        // Lets ForgotPassword throttle re-sends instead of spamming the inbox, same pattern
        // as VerificationCodeSentAt.
        public DateTime? PasswordResetSentAt { get; set; }

        // Change-password-while-signed-in confirmation code (ProfileController). Kept
        // separate from VerificationCode - that one gates first sign-in and this one is
        // used repeatedly afterwards, so sharing a field would let one flow clobber the
        // other's in-flight code.
        [StringLength(10)]
        public string? PasswordChangeCode { get; set; }

        public DateTime? PasswordChangeCodeExpiry { get; set; }

        public DateTime? PasswordChangeCodeSentAt { get; set; }

        // Wrong-guess counter for the current password-change code, same purpose as
        // VerificationCodeAttempts. Reset whenever a fresh code is issued.
        public int PasswordChangeCodeAttempts { get; set; }

        // Navigation properties
        public virtual ICollection<Property> Properties { get; set; }
        public virtual ICollection<RentalApplication> RentalApplications { get; set; }
        public virtual ICollection<MaintenanceRequest> MaintenanceRequests { get; set; }
    }
}
