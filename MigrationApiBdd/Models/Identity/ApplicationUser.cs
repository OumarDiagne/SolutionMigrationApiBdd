using Microsoft.AspNetCore.Identity;
using MigrationApiBdd.Models.Auth;

namespace MigrationApiBdd.Models.Identity
{
    public class ApplicationUser : IdentityUser
    {
        public Clients? Client { get; set; }
        public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    }
}
