using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos.Auth
{
    public class RefreshTokenRequestDto
    {
        [Required]
        public string RefreshToken { get; set; } = null!;
    }
}
