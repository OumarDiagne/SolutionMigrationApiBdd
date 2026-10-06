namespace MigrationApiBdd.Dtos.Auth
{
    public class AuthResultDto
    {
        public bool Succeeded { get; init; }

        public string? UserId { get; init; }

        public string? Email { get; init; }

        /// <summary>Client métier créé et relié au compte (relation 0..1 : les clients sans compte restent possibles).</summary>
        public int? ClientId { get; init; }

        public IEnumerable<string> Errors { get; init; } = [];
    }
}
