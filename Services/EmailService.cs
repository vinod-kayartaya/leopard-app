using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using admin_web.Data;

namespace admin_web.Services
{
    public interface IEmailService
    {
        Task SendPasswordSetupEmailAsync(string toEmail, string firstName, string resetLink);
        string GeneratePasswordResetToken();
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _context;

        public EmailService(IConfiguration configuration, ApplicationDbContext context)
        {
            _configuration = configuration;
            _context = context;
        }

        public async Task SendPasswordSetupEmailAsync(string toEmail, string firstName, string resetLink)
        {
            var email = new MimeMessage();
            email.From.Add(MailboxAddress.Parse(_configuration["Email:From"]));
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = "Welcome - Set Up Your Password";
            
            var builder = new BodyBuilder();
            builder.HtmlBody = $@"
                <h2>Welcome {firstName}!</h2>
                <p>Your account has been created. Please click the link below to set up your password:</p>
                <p><a href='{resetLink}'>Set Up Password</a></p>
                <p>If you didn't request this, please ignore this email.</p>
                <p>This link will expire in 24 hours.</p>";

            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(_configuration["Email:Username"], _configuration["Email:Password"]);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }

        public string GeneratePasswordResetToken()
        {
            return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        }
    }
} 