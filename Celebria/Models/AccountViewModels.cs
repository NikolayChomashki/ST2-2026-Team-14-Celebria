using System.ComponentModel.DataAnnotations;

namespace Celebria.Models;

/// <summary>Данните от формата за вход.</summary>
public class LoginViewModel
{
    [Display(Name = "Потребителско име")]
    [Required(ErrorMessage = "Въведете потребителско име.")]
    public string Username { get; set; } = string.Empty;

    [Display(Name = "Парола")]
    [Required(ErrorMessage = "Въведете парола.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Запомни ме")]
    public bool RememberMe { get; set; }

    /// <summary>Страницата, към която се връщаме след успешен вход (само локални адреси).</summary>
    public string? ReturnUrl { get; set; }
}

/// <summary>Данните от формата за регистрация.</summary>
public class RegisterViewModel
{
    [Display(Name = "Име за показване")]
    [Required(ErrorMessage = "Въведете име.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "Името трябва да е между 2 и 60 символа.")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Потребителско име")]
    [Required(ErrorMessage = "Въведете потребителско име.")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Потребителското име трябва да е между 3 и 30 символа.")]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "Само латински букви, цифри и . _ -")]
    public string Username { get; set; } = string.Empty;

    [Display(Name = "Парола")]
    [Required(ErrorMessage = "Въведете парола.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Паролата трябва да е поне 8 символа.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Повторете паролата")]
    [Required(ErrorMessage = "Повторете паролата.")]
    [Compare(nameof(Password), ErrorMessage = "Паролите не съвпадат.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}
