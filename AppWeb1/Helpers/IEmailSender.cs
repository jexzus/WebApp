using System.Threading.Tasks;

namespace AppWeb1.Helpers
{
    public interface IEmailSender
    {
        Task SendAsync(string to, string subject, string htmlBody);
    }
}
