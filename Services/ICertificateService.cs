using System.Security.Cryptography.X509Certificates;

namespace admin_web.Services
{
    public interface ICertificateService
    {
        Task<string?> IssueCertificateAsync(string firstName, string lastName, string email);
        Task<byte[]?> DownloadCertificateAsync(string serialNumber);
    }
} 