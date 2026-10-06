using System.Security.Claims;

namespace MigrationApiBdd.Services.Auth
{
    public sealed class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor= httpContextAccessor;
        }



        public string? UserId =>
       _httpContextAccessor.HttpContext?.User
           .FindFirstValue(ClaimTypes.NameIdentifier);

        public string? UserName
        {
            get
            {
                ClaimsPrincipal? user =
                    _httpContextAccessor.HttpContext?.User;

                return user?.Identity?.Name;
            }
        }

        public bool IsAuthenticated
        {
            get
            {
                ClaimsPrincipal? user =
                    _httpContextAccessor.HttpContext?.User;

                return user?.Identity?.IsAuthenticated ?? false;
            }
        }

        public bool IsAdmin => _httpContextAccessor.HttpContext?.User
            .IsInRole("Admin") ?? false;
    }
}
