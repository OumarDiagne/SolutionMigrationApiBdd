using Azure.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MigrationApiBdd.Dtos.Auth;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Auth;
using MigrationApiBdd.Models.Context;
using MigrationApiBdd.Models.Identity;
using MigrationApiBdd.Options;
using MigrationApiBdd.Services.Auth.models;

namespace MigrationApiBdd.Services.Auth
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IJwtService _jwtService;
        private readonly MigApiContext _context;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly IOptions<RefreshTokenOptions> _refreshTokenOptions;
        private readonly IOptions<JwtOptions> _jwtOptions;

        public AuthService(UserManager<ApplicationUser> userManager, IJwtService jwtService, MigApiContext context, IRefreshTokenService refreshTokenService, IOptions<RefreshTokenOptions> refreshTokenOptions, IOptions<JwtOptions> jwtOptions)
        {
            _userManager = userManager;
            _jwtService = jwtService;
            _context = context;
            _refreshTokenService = refreshTokenService;
            _refreshTokenOptions = refreshTokenOptions;
            _jwtOptions = jwtOptions;
        }

    

        public async Task<AuthTokensResult?> LoginAsync(LoginDto loginDto)
        {
            var user = await _userManager.FindByEmailAsync(loginDto.Email);

            if (user is null)
            {
                return null;
            }

            var passwordIsValid = await _userManager.CheckPasswordAsync(user, loginDto.Password);

            if (!passwordIsValid)
            {
                return null;
            }

            var roles = await _userManager.GetRolesAsync(user);

            return await CreateTokensAsync(user, roles);
        }

        public async Task LogoutAsync(string? refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return;
            }

            var tokenHash = _refreshTokenService.ComputeHash(refreshToken);

            var storedToken = await _context.RefreshTokens
                .SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (storedToken is null || storedToken.RevokedAtUtc is not null)
            {
                return;
            }

            storedToken.RevokedAtUtc = DateTime.UtcNow;
            storedToken.RevocationReason = "Logout";

            await _context.SaveChangesAsync();
        }

        public async Task<AuthTokensResult?> RefreshAsync(string refreshToken)
        {
            var now = DateTime.UtcNow;

            var tokenHash = _refreshTokenService.ComputeHash(refreshToken);

            var storedToken = await _context.RefreshTokens
                .Include(rt => rt.ApplicationUser)
                .SingleOrDefaultAsync(rt => rt.TokenHash == tokenHash);

            if (storedToken is null)
            {
                return null;
            }

            if (storedToken.RevokedAtUtc is not null)
            {
                await RevokeActiveTokensAsync(
                    storedToken.ApplicationUserId,
                    "ReuseDetected");

                return null;
            }

            if (storedToken.ExpiresAtUtc <= now)
            {
                return null;
            }

            var user = storedToken.ApplicationUser;

            var roles = await _userManager.GetRolesAsync(user);

            var accessToken = _jwtService.GenerateToken(user, roles);

            var newRefreshToken = _refreshTokenService.GenerateToken();

            var newRefreshTokenHash =
                _refreshTokenService.ComputeHash(newRefreshToken);

            var newRefreshTokenEntity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                ApplicationUserId = user.Id,
                TokenHash = newRefreshTokenHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(
                    _refreshTokenOptions.Value.ExpirationDays)
            };

            storedToken.RevokedAtUtc = now;
            storedToken.RevocationReason = "Rotation";
            storedToken.ReplacedByTokenHash = newRefreshTokenHash;

            _context.RefreshTokens.Add(newRefreshTokenEntity);

            await _context.SaveChangesAsync();

            return new AuthTokensResult
            {
                Response = new LoginResponseDto
                {
                    AccessToken = accessToken,
                    ExpiresAtUtc = now.AddMinutes(
                        _jwtOptions.Value.AccessTokenExpirationMinutes),
                    UserId = user.Id,
                    Email = user.Email!
                },
                RefreshToken = newRefreshToken,
                RefreshTokenExpiresAtUtc = newRefreshTokenEntity.ExpiresAtUtc
            };
        }

        private async Task<AuthTokensResult> CreateTokensAsync( ApplicationUser user,IList<string> roles,
                                                                CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var accessToken = _jwtService.GenerateToken(user, roles);

            var refreshToken = _refreshTokenService.GenerateToken();
            var refreshTokenHash = _refreshTokenService.ComputeHash(refreshToken);

            var refreshTokenEntity = new RefreshToken
            {
                Id = Guid.NewGuid(),
                ApplicationUserId = user.Id,
                TokenHash = refreshTokenHash,
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(
                    _refreshTokenOptions.Value.ExpirationDays)
            };

            _context.RefreshTokens.Add(refreshTokenEntity);

            await _context.SaveChangesAsync(cancellationToken);

            return new AuthTokensResult
            {
                Response = new LoginResponseDto
                {
                    AccessToken = accessToken,
                    ExpiresAtUtc = now.AddMinutes(
                        _jwtOptions.Value.AccessTokenExpirationMinutes),
                    UserId = user.Id,
                    Email = user.Email!
                },
                RefreshToken = refreshToken,
                RefreshTokenExpiresAtUtc = refreshTokenEntity.ExpiresAtUtc
            };
        }

        private async Task RevokeActiveTokensAsync(
    string userId,
    string reason,
    CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var tokens = await _context.RefreshTokens
                .Where(rt =>
                    rt.ApplicationUserId == userId &&
                    rt.RevokedAtUtc == null &&
                    rt.ExpiresAtUtc > now)
                .ToListAsync(cancellationToken);

            foreach (var token in tokens)
            {
                token.RevokedAtUtc = now;
                token.RevocationReason = reason;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<AuthResultDto> RegisterAsync(RegisterDto registerDto)
        {
            // Un compte (Identity) et son client métier (relation 0..1 côté client : un client peut aussi exister sans compte) sont créés ensemble :
            // tout ou rien. Sans commit, la transaction est annulée à la sortie de la méthode.
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var user = new ApplicationUser
            {
                UserName = registerDto.Email,
                Email = registerDto.Email,
                Client = new Clients
                {
                    Nom = registerDto.Nom.Trim(),
                    Prenom = registerDto.Prenom.Trim()
                }
            };

            var result = await _userManager.CreateAsync(user, registerDto.Password);

            if (!result.Succeeded)
            {
                return new AuthResultDto
                {
                    Succeeded = false,
                    Errors = result.Errors.Select(error => error.Description)
                };
            }
            var roleResult = await _userManager.AddToRoleAsync(user, "User");

            if (!roleResult.Succeeded)
            {
                return new AuthResultDto
                {
                    Succeeded = false,
                    Errors = roleResult.Errors
                        .Select(error => error.Description)
                        .ToList()
                };
            }

            await transaction.CommitAsync();

            return new AuthResultDto
            {
                Succeeded = true,
                UserId = user.Id,
                Email = user.Email,
                ClientId = user.Client?.ClientId
            };
        }
    }
}
