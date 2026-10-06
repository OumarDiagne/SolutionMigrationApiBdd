using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Options
{
    public class RefreshTokenOptions
    {
        public const string SectionName = "RefreshToken";

        [Range(1, 30)]
        public int ExpirationDays { get; set; } = 7;
    }
}
