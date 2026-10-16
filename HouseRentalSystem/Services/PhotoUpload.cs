namespace HouseRentalSystem.Services
{
    // Shared guard for user-supplied image uploads (property photos, maintenance photos,
    // profile pictures). Without this, an uploaded file is written straight into wwwroot
    // and served from the app's own origin - an .html/.svg upload then becomes a stored
    // XSS vector, and with no size cap a large file is a disk-fill DoS.
    public static class PhotoUpload
    {
        public static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        public const long MaxBytes = 5 * 1024 * 1024;

        // True if the file is a non-empty image within the size limit and with an allowed
        // extension. On failure, 'error' carries a message suitable for ModelState/TempData.
        public static bool IsValidImage(IFormFile? file, out string error)
        {
            error = string.Empty;

            if (file == null || file.Length == 0)
            {
                error = "The uploaded file is empty.";
                return false;
            }

            if (file.Length > MaxBytes)
            {
                error = "Each photo must be 5 MB or smaller.";
                return false;
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                error = "Photos must be JPG, PNG, GIF or WEBP images.";
                return false;
            }

            return true;
        }

        // The (lower-cased, validated) extension to give the stored file - never the raw
        // client-supplied name.
        public static string SafeExtension(IFormFile file) =>
            Path.GetExtension(file.FileName).ToLowerInvariant();
    }
}
