using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos.Auth
{
    /// <summary>
    /// DTO for user registration, used to create a new user account
    /// </summary>
    public class RegisterDto
    {
        [Required(ErrorMessage = "Le nom est obligatoire.", AllowEmptyStrings = false)]
        [StringLength(50, ErrorMessage = "Le nom ne peut pas dépasser 50 caractères.")]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom est obligatoire.", AllowEmptyStrings = false)]
        [StringLength(50, ErrorMessage = "Le prénom ne peut pas dépasser 50 caractères.")]
        public string Prenom { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'adresse e-mail est obligatoire.")]
        [EmailAddress(ErrorMessage = "Le format de l'adresse e-mail est invalide.")]
        [StringLength(256, ErrorMessage = "L'adresse e-mail ne peut pas dépasser 256 caractères.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [StringLength(
            100,
            MinimumLength = 8,
            ErrorMessage = "Le mot de passe doit contenir entre 8 et 100 caractères.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "La confirmation du mot de passe est obligatoire.")]
        [Compare(nameof(Password), ErrorMessage = "Le mot de passe et sa confirmation ne correspondent pas.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
