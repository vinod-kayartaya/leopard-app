using Microsoft.AspNetCore.Mvc;
using admin_web.Models;
using admin_web.Data;
using admin_web.ViewModels;
using Microsoft.EntityFrameworkCore;
using admin_web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.Logging;

namespace admin_web.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ICertificateService _certificateService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ApplicationDbContext context,
            ICertificateService certificateService,
            ILogger<AccountController> logger)
        {
            _context = context;
            _certificateService = certificateService;
            _logger = logger;
        }

        public async Task<IActionResult> SetPassword(string email, string token)
        {
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email == email && e.PasswordResetToken == token);

            if (employee == null || !employee.TokenExpiryTime.HasValue || 
                employee.TokenExpiryTime.Value < DateTime.UtcNow)
            {
                return View("InvalidToken");
            }

            var model = new SetPasswordViewModel
            {
                Email = email,
                Token = token
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email == model.Email && e.PasswordResetToken == model.Token);

            if (employee == null || !employee.TokenExpiryTime.HasValue || 
                employee.TokenExpiryTime.Value < DateTime.UtcNow)
            {
                return View("InvalidToken");
            }

            // Hash the password and update employee
            employee.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);
            employee.PasswordResetToken = null;
            employee.TokenExpiryTime = null;

            await _context.SaveChangesAsync();

            // Store employee ID in session
            HttpContext.Session.SetInt32("EmployeeId", employee.Id);

            // Check if certificate needs to be downloaded
            if (employee.CertDownload == "N")
            {
                return RedirectToAction("DownloadCertificate");
            }

            TempData["SuccessMessage"] = "Password set successfully. Please login.";
            return RedirectToAction("Login");
        }

        public IActionResult Login()
        {
            if (HttpContext.Session.GetInt32("EmployeeId") != null)
            {
                return RedirectToAction("Dashboard");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email == model.Email);

            if (employee == null || employee.PasswordHash == null || 
                !BCrypt.Net.BCrypt.Verify(model.Password, employee.PasswordHash))
            {
                TempData["ErrorMessage"] = "Invalid email or password.";
                return View(model);
            }

            // Store employee ID in session
            HttpContext.Session.SetInt32("EmployeeId", employee.Id);

            // Redirect based on CertDownload status
            if (employee.CertDownload == "N")
            {
                return RedirectToAction("DownloadCertificate");
            }
            return RedirectToAction("Dashboard");
        }

        [Authorize(AuthenticationSchemes = CertificateAuthenticationDefaults.AuthenticationScheme)]
        [Authorize(Policy = "CertificateRequired")]
        public async Task<IActionResult> Dashboard()
        {
            var employeeId = HttpContext.Session.GetInt32("EmployeeId");
            if (employeeId == null)
            {
                _logger.LogWarning("No employee ID in session");
                return RedirectToAction("Login");
            }

            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null)
            {
                _logger.LogWarning("Employee not found: {EmployeeId}", employeeId);
                return NotFound();
            }

            // Verify that the client certificate matches the one we issued
            var clientCert = await HttpContext.Connection.GetClientCertificateAsync();
            _logger.LogInformation("Client certificate: {SerialNumber}", clientCert?.SerialNumber);
            _logger.LogInformation("Expected certificate: {CertificateId}", employee.CertificateId);

            if (clientCert == null || clientCert.SerialNumber != employee.CertificateId)
            {
                _logger.LogWarning("Certificate mismatch or missing. Client: {ClientSerial}, Expected: {StoredSerial}", 
                    clientCert?.SerialNumber, employee.CertificateId);
                TempData["ErrorMessage"] = "Valid client certificate not found. Please install your certificate.";
                return RedirectToAction("CertificateRequired");
            }

            return View(employee);
        }

        public IActionResult CertificateRequired()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> DownloadCertificate()
        {
            var employeeId = HttpContext.Session.GetInt32("EmployeeId");
            if (employeeId == null)
            {
                return RedirectToAction("Login");
            }

            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null)
            {
                return NotFound();
            }

            // If certificate is already downloaded, redirect to dashboard
            if (employee.CertDownload == "Y")
            {
                return RedirectToAction("Dashboard");
            }

            // Show the download certificate view
            return View(employee);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessCertificateDownload()
        {
            var employeeId = HttpContext.Session.GetInt32("EmployeeId");
            if (employeeId == null)
            {
                return RedirectToAction("Login");
            }

            var employee = await _context.Employees.FindAsync(employeeId);
            if (employee == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(employee.CertificateId))
            {
                TempData["ErrorMessage"] = "No certificate found for download.";
                return RedirectToAction("Dashboard");
            }

            var certificateBytes = await _certificateService.DownloadCertificateAsync(employee.CertificateId);
            if (certificateBytes == null)
            {
                TempData["ErrorMessage"] = "Failed to download certificate. Please try again.";
                return RedirectToAction("DownloadCertificate");
            }

            // Update CertDownload status
            employee.CertDownload = "Y";
            await _context.SaveChangesAsync();

            // Return the certificate file
            return File(
                certificateBytes,
                "application/x-pkcs12",
                $"certificate_{employee.FirstName}_{employee.LastName}.p12"
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
} 