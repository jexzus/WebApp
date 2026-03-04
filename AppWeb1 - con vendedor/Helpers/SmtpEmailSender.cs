using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppWeb1.Helpers
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly SmtpClient _smtp;
        private readonly string _from;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> logger)
        {
            _logger = logger;

            // Validar configuración
            _from = cfg["Email:From"] ?? throw new ArgumentException("Email:From no está configurado");
            var smtpHost = cfg["Email:SmtpHost"] ?? throw new ArgumentException("Email:SmtpHost no está configurado");
            var smtpPortStr = cfg["Email:SmtpPort"] ?? throw new ArgumentException("Email:SmtpPort no está configurado");
            var emailUser = cfg["Email:User"] ?? throw new ArgumentException("Email:User no está configurado");
            var emailPass = cfg["Email:Pass"] ?? throw new ArgumentException("Email:Pass no está configurado");

            if (!int.TryParse(smtpPortStr, out int smtpPort))
            {
                throw new ArgumentException("Email:SmtpPort debe ser un número válido");
            }

            _smtp = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(emailUser, emailPass),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30000 // 30 segundos
            };

            _logger.LogInformation("SMTP configurado: {Host}:{Port} para {User}", smtpHost, smtpPort, emailUser);
        }

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            try
            {
                _logger.LogInformation("Intentando enviar email a {To} con asunto: {Subject}", to, subject);

                var msg = new MailMessage(_from, to, subject, htmlBody)
                {
                    IsBodyHtml = true
                };

                await _smtp.SendMailAsync(msg);

                _logger.LogInformation("Email enviado exitosamente a {To}", to);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "Error SMTP al enviar email a {To}: {Message}", to, ex.Message);
                throw new InvalidOperationException($"No se pudo enviar el email: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error general al enviar email a {To}", to);
                throw;
            }
        }
    }
}