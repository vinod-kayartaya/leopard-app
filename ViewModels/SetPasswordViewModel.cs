using System.ComponentModel.DataAnnotations;

namespace admin_web.ViewModels
{
    public class SetPasswordViewModel
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
} 