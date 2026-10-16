namespace HouseRentalSystem.Services
{
    // Fallback used when no Smtp:Host is configured (e.g. a fresh checkout without
    // the user-secrets set). Logs the email instead of failing registration outright,
    // so signup/verification still works for local demo/testing out of the box.
    public class ConsoleEmailSender : IEmailSender
    {
        private readonly ILogger<ConsoleEmailSender> _logger;

        public ConsoleEmailSender(ILogger<ConsoleEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string toEmail, string subject, string body, bool isHtml = false)
        {
            _logger.LogWarning(
                "Smtp:Host not configured - simulating email instead of sending.\nTo: {To}\nSubject: {Subject}\n{Body}",
                toEmail, subject, body);
            return Task.CompletedTask;
        }
    }
}
