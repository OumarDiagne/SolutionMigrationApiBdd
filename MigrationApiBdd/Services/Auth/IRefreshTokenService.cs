namespace MigrationApiBdd.Services.Auth
{
    public interface IRefreshTokenService
    {
        string GenerateToken();

        string ComputeHash(string refreshToken);
    }
}
