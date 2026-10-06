using MigrationApiBdd.Dtos.Auth;
using MigrationApiBdd.Services.Auth.models;

namespace MigrationApiBdd.Services.Auth
{
    public interface IAuthService
    {
        Task<AuthResultDto> RegisterAsync(RegisterDto registerDto);
        Task<AuthTokensResult?> LoginAsync(LoginDto loginDto);
        Task<AuthTokensResult?> RefreshAsync(string refreshToken);
        Task LogoutAsync(string? refreshToken);
    }
}
