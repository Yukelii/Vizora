using System.ComponentModel.DataAnnotations;

namespace Vizora.Models
{
    public class LoginViewModel
    {
        [Required]
        [StringLength(100)]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; } = true;

        public string? ReturnUrl { get; set; }
    }
}
