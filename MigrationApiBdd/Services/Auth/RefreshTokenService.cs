using System.Security.Cryptography;
using System.Text;

namespace MigrationApiBdd.Services.Auth
{
    public class RefreshTokenService : IRefreshTokenService
    {
        public string ComputeHash(string refreshToken)
        {
            var hashBytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(refreshToken));

            return Convert.ToHexString(hashBytes);
        }

        public string GenerateToken()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }
    }
}
