using MigrationApiBdd.Models.Identity;

namespace MigrationApiBdd.Services.Auth
{
    public interface IJwtService
    {
        string GenerateToken(ApplicationUser user, IEnumerable<string> roles);
    }
}
