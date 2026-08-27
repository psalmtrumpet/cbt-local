using System.ComponentModel.DataAnnotations;

namespace NCS.CBT.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class StudentLoginViewModel
{
    [Required(ErrorMessage = "Email address is required")]
    [EmailAddress(ErrorMessage = "Enter a valid email address")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Access PIN is required")]
    [Display(Name = "Access PIN")]
    [DataType(DataType.Password)]
    public string AccessCode { get; set; } = string.Empty;
}
