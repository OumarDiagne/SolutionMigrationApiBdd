using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MigrationApiBdd.Models.Identity;
using MigrationApiBdd.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MigrationApiBdd.Services.Auth
{
    public sealed class JwtService : IJwtService
    {
        private readonly JwtOptions _jwtOptions;

        public JwtService(IOptions<JwtOptions> jwtOptions)
        {
            _jwtOptions = jwtOptions.Value;
        }

        public string GenerateToken(ApplicationUser user, IEnumerable<string> roles)
        {
            var claims = new List<Claim>
{
                new(ClaimTypes.NameIdentifier, user.Id),
                new(ClaimTypes.Name, user.Email!),
                new(ClaimTypes.Email, user.Email!)
};

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));

            var credentials = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
