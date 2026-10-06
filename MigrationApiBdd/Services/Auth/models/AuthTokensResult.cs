using MigrationApiBdd.Dtos.Auth;

namespace MigrationApiBdd.Services.Auth.models
{
    public class AuthTokensResult
    {
        public LoginResponseDto Response { get; set; } = null!;

        public string RefreshToken { get; set; } = null!;

        public DateTime RefreshTokenExpiresAtUtc { get; set; }
    }
}
