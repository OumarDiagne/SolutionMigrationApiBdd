using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Options
{
        public class JwtOptions
        {
        public const string SectionName = "Jwt";

        [Required(ErrorMessage = "Jwt:Issuer est obligatoire.")]
        public string Issuer { get; set; } = string.Empty;

        [Required(ErrorMessage = "Jwt:Audience est obligatoire.")]
        public string Audience { get; set; } = string.Empty;

        [Required(ErrorMessage = "Jwt:SigningKey est obligatoire.")]
        [MinLength(32, ErrorMessage = "Jwt:SigningKey doit contenir au moins 32 caractères.")]
        public string SigningKey { get; set; } = string.Empty;

        [Range(
            1,
            1440,
            ErrorMessage = "Jwt:AccessTokenExpirationMinutes doit être compris entre 1 et 1440.")]
        public int AccessTokenExpirationMinutes { get; set; }
    }
}
