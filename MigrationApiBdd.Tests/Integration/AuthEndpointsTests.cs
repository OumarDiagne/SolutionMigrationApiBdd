using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos.Auth;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

[Collection(ApiCollection.Nom)]
public class AuthEndpointsTests
{
    private readonly ApiFactory _factory;

    public AuthEndpointsTests(ApiFactory factory) => _factory = factory;

    private static RegisterDto Inscription(string email, string motDePasse = ApiTestHelpers.MotDePasseUtilisateur) => new()
    {
        Email = email,
        Nom = "Dupont",
        Prenom = "Marie",
        Password = motDePasse,
        ConfirmPassword = motDePasse
    };

    private static string NouvelEmail() => $"auth-{Guid.NewGuid():N}@tests.local";

    /// <summary>Extrait "refresh_token=..." du header Set-Cookie (valeur telle que renvoyée par l'API).</summary>
    private static string ExtraireCookieRefresh(HttpResponseMessage reponse)
    {
        var setCookie = reponse.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refresh_token="));
        return setCookie.Split(';')[0];
    }

    private static async Task<HttpResponseMessage> PostAvecCookieAsync(HttpClient client, string url, string cookie)
    {
        var requete = new HttpRequestMessage(HttpMethod.Post, url);
        requete.Headers.Add("Cookie", cookie);
        return await client.SendAsync(requete);
    }

    // ---------- Inscription ----------

    [Fact]
    public async Task Register_Valide_Retourne201()
    {
        var client = _factory.CreerClient();

        var reponse = await client.PostJsonAsync("/api/auth/register", Inscription(NouvelEmail()));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    [Fact]
    public async Task Register_MotDePasseSansMajuscule_Retourne400()
    {
        var client = _factory.CreerClient();

        var reponse = await client.PostJsonAsync("/api/auth/register", Inscription(NouvelEmail(), "motdepasse123"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Register_ConfirmationDifferente_Retourne400()
    {
        var client = _factory.CreerClient();
        var dto = new RegisterDto
        {
            Email = NouvelEmail(),
            Nom = "Dupont",
            Prenom = "Marie",
            Password = ApiTestHelpers.MotDePasseUtilisateur,
            ConfirmPassword = "Autre-mot-de-passe-1"
        };

        var reponse = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Register_EmailDejaUtilise_Retourne400()
    {
        var client = _factory.CreerClient();
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));

        var reponse = await client.PostJsonAsync("/api/auth/register", Inscription(email));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Register_SansNomNiPrenom_Retourne400()
    {
        var client = _factory.CreerClient();
        var dto = new RegisterDto
        {
            Email = NouvelEmail(),
            Password = ApiTestHelpers.MotDePasseUtilisateur,
            ConfirmPassword = ApiTestHelpers.MotDePasseUtilisateur
        };

        var reponse = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Register_CreeUnClientMetierRelieAuCompte()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.GetAsync("/api/client/me");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var client = await reponse.LireAsync<MigrationApiBdd.Dtos.ClientDto>();
        Assert.Equal(utilisateur.ClientId, client.ClientId);
        Assert.Equal(ApiTestHelpers.NomParDefaut, client.Nom);
    }

    [Fact]
    public async Task Register_EnEchec_NeCreeAucunClientOrphelin()
    {
        var client = _factory.CreerClient();
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email, "motdepasse123")); // refusé : pas de majuscule

        var nombre = await _factory.DansLaBaseAsync(db =>
            db.Clients.CountAsync(c => c.ApplicationUser != null && c.ApplicationUser.Email == email));
        Assert.Equal(0, nombre);
    }

    // ---------- Connexion ----------

    [Fact]
    public async Task Login_Valide_RetourneUnJwtEtPoseUnCookieRefreshSecurise()
    {
        var client = _factory.CreerClient();
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));

        var reponse = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = email, Password = ApiTestHelpers.MotDePasseUtilisateur });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var corps = await reponse.LireAsync<LoginResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(corps.AccessToken));
        Assert.Equal(email, corps.Email);

        var cookie = reponse.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("refresh_token=")).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/api/auth", cookie);
    }

    [Fact]
    public async Task Login_MauvaisMotDePasse_Retourne401()
    {
        var client = _factory.CreerClient();
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));

        var reponse = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = email, Password = "Mauvais-mot-de-passe-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Login_EmailInconnu_Retourne401()
    {
        var client = _factory.CreerClient();

        var reponse = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = NouvelEmail(), Password = ApiTestHelpers.MotDePasseUtilisateur });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    // ---------- Protection des endpoints ----------

    [Fact]
    public async Task EndpointProtege_SansToken_Retourne401()
    {
        var client = _factory.CreerClient();

        var reponse = await client.GetAsync("/api/Produit");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task EndpointProtege_TokenInvalide_Retourne401()
    {
        var client = _factory.CreerClient();
        client.UtiliserToken("ceci.n-est-pas.un-jwt");

        var reponse = await client.GetAsync("/api/Produit");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task EndpointProtege_AvecToken_Retourne200()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.GetAsync("/api/Produit");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task EndpointAdmin_UtilisateurStandard_Retourne403()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.GetAsync("/api/Commande/admin-test");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task EndpointAdmin_Administrateur_Retourne200()
    {
        var admin = await _factory.CreerAdminAsync();

        var reponse = await admin.GetAsync("/api/Commande/admin-test");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    // ---------- Refresh token ----------

    [Fact]
    public async Task Refresh_AvecLeCookieDeConnexion_RetourneUnNouveauToken()
    {
        var client = _factory.CreerClient(); // gère les cookies comme un navigateur
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));
        var login = await client.LoginAsync(email, ApiTestHelpers.MotDePasseUtilisateur);

        var reponse = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var corps = await reponse.LireAsync<LoginResponseDto>();
        Assert.False(string.IsNullOrWhiteSpace(corps.AccessToken));
        Assert.Equal(login.UserId, corps.UserId);
    }

    [Fact]
    public async Task Refresh_SansCookie_Retourne401()
    {
        var client = _factory.CreerClient(gererLesCookies: false);

        var reponse = await client.PostAsync("/api/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Refresh_RejeuDUnAncienCookie_Retourne401()
    {
        var client = _factory.CreerClient(gererLesCookies: false);
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));
        var connexion = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = email, Password = ApiTestHelpers.MotDePasseUtilisateur });
        var ancienCookie = ExtraireCookieRefresh(connexion);

        var premier = await PostAvecCookieAsync(client, "/api/auth/refresh", ancienCookie);
        var rejeu = await PostAvecCookieAsync(client, "/api/auth/refresh", ancienCookie);

        Assert.Equal(HttpStatusCode.OK, premier.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rejeu.StatusCode);
    }

    [Fact]
    public async Task Refresh_ApresRejeu_LeNouveauCookieEstAussiRevoque()
    {
        // Détection de réutilisation : le rejeu de l'ancien token révoque toute la famille.
        var client = _factory.CreerClient(gererLesCookies: false);
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));
        var connexion = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = email, Password = ApiTestHelpers.MotDePasseUtilisateur });
        var ancienCookie = ExtraireCookieRefresh(connexion);

        var rotation = await PostAvecCookieAsync(client, "/api/auth/refresh", ancienCookie);
        var nouveauCookie = ExtraireCookieRefresh(rotation);
        await PostAvecCookieAsync(client, "/api/auth/refresh", ancienCookie); // rejeu = vol présumé

        var avecNouveau = await PostAvecCookieAsync(client, "/api/auth/refresh", nouveauCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, avecNouveau.StatusCode);
    }

    // ---------- Déconnexion ----------

    [Fact]
    public async Task Logout_RevoqueLeRefreshToken()
    {
        var client = _factory.CreerClient(gererLesCookies: false);
        var email = NouvelEmail();
        await client.PostJsonAsync("/api/auth/register", Inscription(email));
        var connexion = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = email, Password = ApiTestHelpers.MotDePasseUtilisateur });
        var cookie = ExtraireCookieRefresh(connexion);

        var logout = await PostAvecCookieAsync(client, "/api/auth/logout", cookie);
        var refresh = await PostAvecCookieAsync(client, "/api/auth/refresh", cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Logout_SansCookie_Retourne204()
    {
        var client = _factory.CreerClient(gererLesCookies: false);

        var reponse = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
    }
}
