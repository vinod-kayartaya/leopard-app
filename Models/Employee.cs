using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace admin_web.Models
{
    public class Employee
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;
        
        [Required]
        [EmailAddress]
        [StringLength(255)]
        public string Email { get; set; } = string.Empty;
        
        [Phone]
        [StringLength(20)]
        public string? PhoneNumber { get; set; }
        
        [Required]
        public DateTime HireDate { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Department { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string Position { get; set; } = string.Empty;
        
        [Required]
        [Column(TypeName = "char(1)")]
        public string CertDownload { get; set; } = "N";  // Can only be 'Y' or 'N'

        [StringLength(100)]
        public string? PasswordHash { get; set; }

        [StringLength(100)]
        public string? PasswordResetToken { get; set; }

        public DateTime? TokenExpiryTime { get; set; }

        [StringLength(100)]  // Changed back to a reasonable size for serial numbers
        public string? CertificateId { get; set; }

        [NotMapped]  // This won't be stored in database
        public bool IssueCertificate { get; set; } = true;  // Default checked for new employees
    }
} 