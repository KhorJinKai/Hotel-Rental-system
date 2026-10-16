using System.Net;
using System.Net.Mail;

namespace HouseRentalSystem.Services
{
    // Sends real email via SMTP. Credentials come from configuration (Smtp:* keys) -
    // set them with user-secrets in development, e.g.
    //   dotnet user-secrets set "Smtp:Host" "smtp.gmail.com"
    //   dotnet user-secrets set "Smtp:User" "you@gmail.com"
    //   dotnet user-secrets set "Smtp:Password" "app-password"
    // (same pattern as the Stripe secret key in Program.cs). Only registered when
    // Smtp:Host is actually set - see Program.cs.
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;

        public SmtpEmailSender(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendAsync(string toEmail, string subject, string body, bool isHtml = false)
        {
            var host = _config["Smtp:Host"];
            var port = int.Parse(_config["Smtp:Port"] ?? "587");
            var user = _config["Smtp:User"];
            var password = _config["Smtp:Password"];
            var fromEmail = _config["Smtp:FromEmail"] ?? user;
            var fromName = _config["Smtp:FromName"] ?? "RentEasy";
            var enableSsl = bool.Parse(_config["Smtp:EnableSsl"] ?? "true");

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(user, password),
                EnableSsl = enableSsl
            };

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail!, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = isHtml
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);
        }
    }
}
