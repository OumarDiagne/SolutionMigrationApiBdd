using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos.Auth
{
    /// <summary>
    /// DTO for user login , ask for token generation
    /// </summary>
    public class LoginDto
    {
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
    }
}
