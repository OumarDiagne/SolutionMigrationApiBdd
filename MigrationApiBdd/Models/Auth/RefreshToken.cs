using MigrationApiBdd.Models.Identity;

namespace MigrationApiBdd.Models.Auth
{
    public class RefreshToken
    {
        public Guid Id { get; set; }

        public string ApplicationUserId { get; set; } = null!;

        public ApplicationUser ApplicationUser { get; set; } = null!;

        public string TokenHash { get; set; } = null!;

        public DateTime CreatedAtUtc { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public DateTime? RevokedAtUtc { get; set; }

        public string? ReplacedByTokenHash { get; set; }

        public string? RevocationReason { get; set; }

        public bool IsActive =>
            RevokedAtUtc is null &&
            ExpiresAtUtc > DateTime.UtcNow;
    }
}
