using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using MigrationApiBdd.Dtos.Auth;
using MigrationApiBdd.Models.Auth;
using MigrationApiBdd.Models.Context;
using MigrationApiBdd.Models.Identity;
using MigrationApiBdd.Options;
using MigrationApiBdd.Services.Auth;
using Xunit;

namespace MigrationApiBdd.Tests.Services;

/// <summary>
/// Tests d'AuthService : UserManager et JwtService sont simulés, la base est EF Core InMemory,
/// et RefreshTokenService est la vraie implémentation (hash SHA-256 déterministe).
/// </summary>
public class AuthServiceTests : IDisposable
{
    private const string Email = "alice@example.com";
    private const string MotDePasse = "Motdepasse!123";

    private readonly Mock<UserManager<ApplicationUser>> _userManager = CreerUserManager();
    private readonly Mock<IJwtService> _jwt = new();
    private readonly RefreshTokenService _refreshTokenService = new();
    private readonly MigApiContext _context;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<MigApiContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // InMemory ne gère pas les transactions : RegisterAsync en ouvre une (utilisateur + client).
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _context = new MigApiContext(options);

        _jwt
            .Setup(j => j.GenerateToken(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .Returns("jwt-access-token");

        _service = new AuthService(
            _userManager.Object,
            _jwt.Object,
            _context,
            _refreshTokenService,
            Microsoft.Extensions.Options.Options.Create(new RefreshTokenOptions { ExpirationDays = 7 }),
            Microsoft.Extensions.Options.Options.Create(new JwtOptions { AccessTokenExpirationMinutes = 15 }));
    }

    public void Dispose() => _context.Dispose();

    private static Mock<UserManager<ApplicationUser>> CreerUserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private static ApplicationUser CreerUtilisateur(string id = "user-1") => new()
    {
        Id = id,
        UserName = Email,
        Email = Email
    };

    private void UtilisateurConnuAvecMotDePasseValide(ApplicationUser user, params string[] roles)
    {
        _userManager.Setup(m => m.FindByEmailAsync(Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, MotDePasse)).ReturnsAsync(true);
        _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(roles.ToList());
    }

    /// <summary>Insère un refresh token en base et retourne sa valeur brute (celle que le client détient).</summary>
    private async Task<(string brut, RefreshToken entite)> AjouterRefreshTokenAsync(
        ApplicationUser user,
        TimeSpan? expireDans = null,
        DateTime? revokeLe = null,
        string? raisonRevocation = null)
    {
        var brut = _refreshTokenService.GenerateToken();
        var entite = new RefreshToken
        {
            Id = Guid.NewGuid(),
            ApplicationUser = user,
            ApplicationUserId = user.Id,
            TokenHash = _refreshTokenService.ComputeHash(brut),
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            ExpiresAtUtc = DateTime.UtcNow.Add(expireDans ?? TimeSpan.FromDays(3)),
            RevokedAtUtc = revokeLe,
            RevocationReason = raisonRevocation
        };

        _context.RefreshTokens.Add(entite);
        await _context.SaveChangesAsync();
        return (brut, entite);
    }

    // =====================================================================
    // LoginAsync
    // =====================================================================

    private static LoginDto CreerLoginDto(string motDePasse = MotDePasse) =>
        new() { Email = Email, Password = motDePasse };

    [Fact]
    public async Task Login_EmailInconnu_RetourneNullSansCreerDeToken()
    {
        _userManager.Setup(m => m.FindByEmailAsync(Email)).ReturnsAsync((ApplicationUser?)null);

        var resultat = await _service.LoginAsync(CreerLoginDto());

        Assert.Null(resultat);
        Assert.Empty(_context.RefreshTokens);
    }

    [Fact]
    public async Task Login_MauvaisMotDePasse_RetourneNullSansCreerDeToken()
    {
        var user = CreerUtilisateur();
        _userManager.Setup(m => m.FindByEmailAsync(Email)).ReturnsAsync(user);
        _userManager.Setup(m => m.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(false);

        var resultat = await _service.LoginAsync(CreerLoginDto("mauvais-mot-de-passe"));

        Assert.Null(resultat);
        Assert.Empty(_context.RefreshTokens);
        _jwt.Verify(j => j.GenerateToken(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task Login_Valide_RetourneAccessTokenEtRefreshToken()
    {
        var user = CreerUtilisateur();
        UtilisateurConnuAvecMotDePasseValide(user, "User");

        var resultat = await _service.LoginAsync(CreerLoginDto());

        Assert.NotNull(resultat);
        Assert.Equal("jwt-access-token", resultat!.Response.AccessToken);
        Assert.Equal("user-1", resultat.Response.UserId);
        Assert.Equal(Email, resultat.Response.Email);
        Assert.False(string.IsNullOrWhiteSpace(resultat.RefreshToken));
    }

    [Fact]
    public async Task Login_Valide_TransmetLesRolesAuJwt()
    {
        var user = CreerUtilisateur();
        UtilisateurConnuAvecMotDePasseValide(user, "Admin", "User");

        await _service.LoginAsync(CreerLoginDto());

        _jwt.Verify(j => j.GenerateToken(
            user,
            It.Is<IEnumerable<string>>(r => r.OrderBy(x => x).SequenceEqual(new[] { "Admin", "User" }))), Times.Once);
    }

    [Fact]
    public async Task Login_Valide_StockeLeHashDuRefreshTokenEtJamaisLaValeurBrute()
    {
        var user = CreerUtilisateur();
        UtilisateurConnuAvecMotDePasseValide(user, "User");

        var resultat = await _service.LoginAsync(CreerLoginDto());

        var stocke = Assert.Single(_context.RefreshTokens);
        Assert.NotEqual(resultat!.RefreshToken, stocke.TokenHash);
        Assert.Equal(_refreshTokenService.ComputeHash(resultat.RefreshToken), stocke.TokenHash);
        Assert.Equal("user-1", stocke.ApplicationUserId);
        Assert.Null(stocke.RevokedAtUtc);
    }

    [Fact]
    public async Task Login_Valide_LesExpirationsSuiventLaConfiguration()
    {
        var user = CreerUtilisateur();
        UtilisateurConnuAvecMotDePasseValide(user, "User");
        var avant = DateTime.UtcNow;

        var resultat = await _service.LoginAsync(CreerLoginDto());

        var apres = DateTime.UtcNow;
        Assert.InRange(resultat!.RefreshTokenExpiresAtUtc, avant.AddDays(7), apres.AddDays(7));
        Assert.InRange(resultat.Response.ExpiresAtUtc, avant.AddMinutes(15), apres.AddMinutes(15));
    }

    [Fact]
    public async Task Login_DeuxConnexions_DonnentDesRefreshTokensDifferents()
    {
        var user = CreerUtilisateur();
        UtilisateurConnuAvecMotDePasseValide(user, "User");

        var premiere = await _service.LoginAsync(CreerLoginDto());
        var seconde = await _service.LoginAsync(CreerLoginDto());

        Assert.NotEqual(premiere!.RefreshToken, seconde!.RefreshToken);
        Assert.Equal(2, _context.RefreshTokens.Count());
    }

    // =====================================================================
    // RefreshAsync
    // =====================================================================

    [Fact]
    public async Task Refresh_TokenInconnu_RetourneNull()
    {
        var resultat = await _service.RefreshAsync("token-qui-n-existe-pas");

        Assert.Null(resultat);
    }

    [Fact]
    public async Task Refresh_TokenExpire_RetourneNullSansRienModifier()
    {
        var user = CreerUtilisateur();
        var (brut, entite) = await AjouterRefreshTokenAsync(user, expireDans: TimeSpan.FromMinutes(-1));

        var resultat = await _service.RefreshAsync(brut);

        Assert.Null(resultat);
        Assert.Null(entite.RevokedAtUtc);
        Assert.Single(_context.RefreshTokens);
    }

    [Fact]
    public async Task Refresh_TokenValide_RetourneDeNouveauxTokens()
    {
        var user = CreerUtilisateur();
        _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        var (brut, _) = await AjouterRefreshTokenAsync(user);

        var resultat = await _service.RefreshAsync(brut);

        Assert.NotNull(resultat);
        Assert.Equal("jwt-access-token", resultat!.Response.AccessToken);
        Assert.Equal(user.Id, resultat.Response.UserId);
        Assert.NotEqual(brut, resultat.RefreshToken);
    }

    [Fact]
    public async Task Refresh_TokenValide_RevoqueLAncienParRotationEtChaineLeNouveau()
    {
        var user = CreerUtilisateur();
        _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        var (brut, ancien) = await AjouterRefreshTokenAsync(user);

        var resultat = await _service.RefreshAsync(brut);

        var hashNouveau = _refreshTokenService.ComputeHash(resultat!.RefreshToken);
        Assert.NotNull(ancien.RevokedAtUtc);
        Assert.Equal("Rotation", ancien.RevocationReason);
        Assert.Equal(hashNouveau, ancien.ReplacedByTokenHash);

        Assert.Equal(2, _context.RefreshTokens.Count());
        var actif = Assert.Single(_context.RefreshTokens.Where(t => t.RevokedAtUtc == null));
        Assert.Equal(hashNouveau, actif.TokenHash);
    }

    [Fact]
    public async Task Refresh_ReutilisationDUnTokenRevoque_RevoqueTousLesTokensActifsDeLUtilisateur()
    {
        var user = CreerUtilisateur();
        var (brutRevoque, _) = await AjouterRefreshTokenAsync(
            user, revokeLe: DateTime.UtcNow.AddMinutes(-5), raisonRevocation: "Rotation");
        var (_, tokenActif) = await AjouterRefreshTokenAsync(user);

        var resultat = await _service.RefreshAsync(brutRevoque);

        Assert.Null(resultat);
        Assert.NotNull(tokenActif.RevokedAtUtc);
        Assert.Equal("ReuseDetected", tokenActif.RevocationReason);
    }

    [Fact]
    public async Task Refresh_ReutilisationDUnTokenRevoque_NeTouchePasLesTokensDesAutresUtilisateurs()
    {
        var pirate = CreerUtilisateur("user-1");
        var autre = CreerUtilisateur("user-2");
        var (brutRevoque, _) = await AjouterRefreshTokenAsync(
            pirate, revokeLe: DateTime.UtcNow.AddMinutes(-5), raisonRevocation: "Rotation");
        var (_, tokenAutre) = await AjouterRefreshTokenAsync(autre);

        await _service.RefreshAsync(brutRevoque);

        Assert.Null(tokenAutre.RevokedAtUtc);
    }

    // =====================================================================
    // LogoutAsync
    // =====================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Logout_SansToken_NeFaitRien(string? token)
    {
        var user = CreerUtilisateur();
        var (_, entite) = await AjouterRefreshTokenAsync(user);

        await _service.LogoutAsync(token);

        Assert.Null(entite.RevokedAtUtc);
    }

    [Fact]
    public async Task Logout_TokenInconnu_NeLevePasDException()
    {
        var exception = await Record.ExceptionAsync(() => _service.LogoutAsync("token-inconnu"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Logout_TokenValide_LeRevoqueAvecLaRaisonLogout()
    {
        var user = CreerUtilisateur();
        var (brut, entite) = await AjouterRefreshTokenAsync(user);

        await _service.LogoutAsync(brut);

        Assert.NotNull(entite.RevokedAtUtc);
        Assert.Equal("Logout", entite.RevocationReason);
    }

    [Fact]
    public async Task Logout_TokenDejaRevoque_ConserveLaRevocationInitiale()
    {
        var user = CreerUtilisateur();
        var dateInitiale = DateTime.UtcNow.AddHours(-1);
        var (brut, entite) = await AjouterRefreshTokenAsync(
            user, revokeLe: dateInitiale, raisonRevocation: "Rotation");

        await _service.LogoutAsync(brut);

        Assert.Equal(dateInitiale, entite.RevokedAtUtc);
        Assert.Equal("Rotation", entite.RevocationReason);
    }

    // =====================================================================
    // RegisterAsync
    // =====================================================================

    private static RegisterDto CreerRegisterDto() => new()
    {
        Email = Email,
        Nom = "  Diagne ",
        Prenom = "Oumar",
        Password = MotDePasse,
        ConfirmPassword = MotDePasse
    };

    [Fact]
    public async Task Register_CreationEnEchec_RetourneLesErreursSansAttribuerDeRole()
    {
        _userManager
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), MotDePasse))
            .ReturnsAsync(IdentityResult.Failed(
                new IdentityError { Description = "Mot de passe trop faible." },
                new IdentityError { Description = "E-mail déjà utilisé." }));

        var resultat = await _service.RegisterAsync(CreerRegisterDto());

        Assert.False(resultat.Succeeded);
        Assert.Equal(new[] { "Mot de passe trop faible.", "E-mail déjà utilisé." }, resultat.Errors.ToArray());
        _userManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Register_AttributionDuRoleEnEchec_RetourneLesErreurs()
    {
        _userManager
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), MotDePasse))
            .ReturnsAsync(IdentityResult.Success);
        _userManager
            .Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Rôle introuvable." }));

        var resultat = await _service.RegisterAsync(CreerRegisterDto());

        Assert.False(resultat.Succeeded);
        Assert.Equal("Rôle introuvable.", Assert.Single(resultat.Errors));
    }

    [Fact]
    public async Task Register_Valide_CreeLUtilisateurAvecLeRoleUser()
    {
        ApplicationUser? cree = null;
        _userManager
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), MotDePasse))
            .Callback<ApplicationUser, string>((u, _) => cree = u)
            .ReturnsAsync(IdentityResult.Success);
        _userManager
            .Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Success);

        var resultat = await _service.RegisterAsync(CreerRegisterDto());

        Assert.True(resultat.Succeeded);
        Assert.Empty(resultat.Errors);
        Assert.Equal(Email, resultat.Email);
        Assert.NotNull(cree);
        Assert.Equal(Email, cree!.UserName);
        Assert.Equal(Email, cree.Email);
        Assert.Equal(cree.Id, resultat.UserId);
        _userManager.Verify(m => m.AddToRoleAsync(cree, "User"), Times.Once);
    }

    [Fact]
    public async Task Register_Valide_CreeLeClientMetierRelieAuCompte()
    {
        ApplicationUser? cree = null;
        _userManager
            .Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), MotDePasse))
            .Callback<ApplicationUser, string>((u, _) => cree = u)
            .ReturnsAsync(IdentityResult.Success);
        _userManager
            .Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), "User"))
            .ReturnsAsync(IdentityResult.Success);

        await _service.RegisterAsync(CreerRegisterDto());

        // Le client est attaché à l'utilisateur : EF les enregistre dans le même SaveChanges (relation 0..1).
        Assert.NotNull(cree!.Client);
        Assert.Equal("Diagne", cree.Client!.Nom);   // espaces superflus retirés
        Assert.Equal("Oumar", cree.Client.Prenom);
        Assert.True(cree.Client.IsActive);
    }
}
