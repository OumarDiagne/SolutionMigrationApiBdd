using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos.Auth
{
    public class LogoutRequestDto
    {
        [Required]
        public string RefreshToken { get; set; } = null!;
    }
}
