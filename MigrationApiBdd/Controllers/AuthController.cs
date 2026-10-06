using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MigrationApiBdd.Dtos.Auth;
using MigrationApiBdd.Services.Auth;


namespace MigrationApiBdd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private const string RefreshTokenCookieName = "refresh_token";

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            // Implementation for user registration
            var result = await _authService.RegisterAsync(registerDto);

            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    Message = "La création du compte a échoué.",
                    Errors = result.Errors!
                });
            }

            return StatusCode(StatusCodes.Status201Created, new
            {
                Message = "Utilisateur créé avec succès.",
                UserId = result.UserId,
                Email = result.Email,
                ClientId = result.ClientId
            });
        }


        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginDto loginDto)
        {
            var result = await _authService.LoginAsync(loginDto);

            if (result is null)
            {
                return Unauthorized();
            }

            SetRefreshTokenCookie(
                result.RefreshToken,
                result.RefreshTokenExpiresAtUtc);

            return Ok(result.Response);
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<ActionResult<LoginResponseDto>> Refresh()
        {
            var refreshToken = Request.Cookies[RefreshTokenCookieName];

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return Unauthorized();
            }

            var result = await _authService.RefreshAsync(refreshToken);

            if (result is null)
            {
                DeleteRefreshTokenCookie();

                return Unauthorized();
            }

            SetRefreshTokenCookie(
                result.RefreshToken,
                result.RefreshTokenExpiresAtUtc);

            return Ok(result.Response);
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout()
        {
            var refreshToken = Request.Cookies[RefreshTokenCookieName];

            await _authService.LogoutAsync(refreshToken);

            DeleteRefreshTokenCookie();

            return NoContent();
        }


        private void SetRefreshTokenCookie(string refreshToken, DateTime expiresAtUtc)
        {
            Response.Cookies.Append(
                RefreshTokenCookieName,
                refreshToken,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/api/auth",
                    Expires = new DateTimeOffset(expiresAtUtc)
                });
        }

        private void DeleteRefreshTokenCookie()
        {
            Response.Cookies.Delete(
                RefreshTokenCookieName,
                new CookieOptions
                {
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/api/auth"
                });
        }
    }
}
