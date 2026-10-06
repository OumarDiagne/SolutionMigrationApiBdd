namespace MigrationApiBdd.Dtos.Auth
{
    /// <summary>
    /// Represents the response returned after a successful login operation.
    /// </summary>
    public class LoginResponseDto
    {
        public string UserId { get; init; } = string.Empty;
        
        public string AccessToken { get; init; } = string.Empty;

        public DateTime ExpiresAtUtc { get; init; }

        public string Email { get; init; } = string.Empty;
    }
}
