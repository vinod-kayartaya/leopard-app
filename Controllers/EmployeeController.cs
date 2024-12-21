using Microsoft.AspNetCore.Mvc;
using admin_web.Data;
using admin_web.Models;
using Microsoft.EntityFrameworkCore;
using admin_web.Services;

namespace admin_web.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ICertificateService _certificateService;

        public EmployeeController(
            ApplicationDbContext context, 
            IEmailService emailService,
            ICertificateService certificateService)
        {
            _context = context;
            _emailService = emailService;
            _certificateService = certificateService;
        }

        public async Task<IActionResult> Index()
        {
            var employees = await _context.Employees.ToListAsync();
            return View(employees);
        }

        public IActionResult Create()
        {
            return View(new Employee { HireDate = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee employee)
        {
            if (ModelState.IsValid)
            {
                // Generate password reset token
                employee.PasswordResetToken = _emailService.GeneratePasswordResetToken();
                employee.TokenExpiryTime = DateTime.UtcNow.AddHours(24);

                // Handle certificate issuance
                if (employee.IssueCertificate)
                {
                    var certificateId = await _certificateService.IssueCertificateAsync(
                        employee.FirstName,
                        employee.LastName,
                        employee.Email);

                    if (certificateId == null)
                    {
                        ModelState.AddModelError("", "Failed to issue certificate. Please try again.");
                        return View(employee);
                    }

                    employee.CertificateId = certificateId;
                }

                _context.Add(employee);
                await _context.SaveChangesAsync();

                var resetLink = Url.Action("SetPassword", "Account", 
                    new { email = employee.Email, token = employee.PasswordResetToken }, 
                    Request.Scheme) ?? throw new InvalidOperationException("Could not generate reset link");

                await _emailService.SendPasswordSetupEmailAsync(
                    employee.Email,
                    employee.FirstName,
                    resetLink);

                return RedirectToAction(nameof(Index));
            }
            return View(employee);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
            {
                return NotFound();
            }

            // Set IssueCertificate to false for edit mode
            employee.IssueCertificate = false;
            
            return View("Create", employee);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingEmployee = await _context.Employees.FindAsync(id);
                    if (existingEmployee == null)
                    {
                        return NotFound();
                    }

                    // Preserve password-related fields
                    employee.PasswordHash = existingEmployee.PasswordHash;
                    employee.PasswordResetToken = existingEmployee.PasswordResetToken;
                    employee.TokenExpiryTime = existingEmployee.TokenExpiryTime;

                    // Handle certificate issuance
                    if (employee.IssueCertificate)
                    {
                        var certificateId = await _certificateService.IssueCertificateAsync(
                            employee.FirstName,
                            employee.LastName,
                            employee.Email);

                        if (certificateId == null)
                        {
                            ModelState.AddModelError("", "Failed to issue certificate. Please try again.");
                            return View("Create", employee);
                        }

                        employee.CertificateId = certificateId;
                    }
                    else
                    {
                        employee.CertificateId = existingEmployee.CertificateId;
                    }

                    // Update the entity
                    _context.Entry(existingEmployee).CurrentValues.SetValues(employee);
                    await _context.SaveChangesAsync();

                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Employees.AnyAsync(e => e.Id == id))
                    {
                        return NotFound();
                    }
                    throw;
                }
            }
            return View("Create", employee);
        }
    }
} 