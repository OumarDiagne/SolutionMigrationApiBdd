namespace MigrationApiBdd.Services.Auth
{
    public interface ICurrentUserService
    {
        string? UserId { get; }

        string? UserName { get; }

        bool IsAuthenticated { get; }

        bool IsAdmin { get; }
    }
}
