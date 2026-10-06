using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Dtos.Auth;
using MigrationApiBdd.Models;

namespace MigrationApiBdd.Tests.Integration;

public sealed record UtilisateurTest(HttpClient Client, string UserId, string Email, int ClientId);

internal sealed record InscriptionReponse(string UserId, string Email, int ClientId);

public static class ApiTestHelpers
{
    public const string MotDePasseUtilisateur = "Motdepasse!123";
    public const string NomParDefaut = "Testeur";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Client HTTPS (le cookie du refresh token est Secure) avec ou sans gestion des cookies.</summary>
    public static HttpClient CreerClient(this ApiFactory factory, bool gererLesCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = gererLesCookies
        });

    public static Task<HttpResponseMessage> PostJsonAsync<T>(
        this HttpClient client, string url, T body, string? idempotencyKey = null)
    {
        var requete = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        if (idempotencyKey is not null)
        {
            requete.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return client.SendAsync(requete);
    }

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> SupprimerAsync(this HttpClient client, string url, string? ifMatch)
    {
        var requete = new HttpRequestMessage(HttpMethod.Delete, url);
        if (ifMatch is not null)
        {
            requete.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }
        return client.SendAsync(requete);
    }

    public static async Task<T> LireAsync<T>(this HttpResponseMessage reponse) =>
        (await reponse.Content.ReadFromJsonAsync<T>(Json))!;

    // ---------- Authentification ----------

    public static async Task<LoginResponseDto> LoginAsync(this HttpClient client, string email, string motDePasse)
    {
        var reponse = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = motDePasse });
        reponse.EnsureSuccessStatusCode();
        return await reponse.LireAsync<LoginResponseDto>();
    }

    public static void UtiliserToken(this HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    /// <summary>Crée un nouvel utilisateur (rôle User), le connecte et retourne un client authentifié.</summary>
    public static async Task<UtilisateurTest> CreerUtilisateurAsync(this ApiFactory factory)
    {
        var email = $"user-{Guid.NewGuid():N}@tests.local";
        var client = factory.CreerClient();

        var inscription = await client.PostJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = email,
            Nom = NomParDefaut,
            Prenom = "Alice",
            Password = MotDePasseUtilisateur,
            ConfirmPassword = MotDePasseUtilisateur
        });
        inscription.EnsureSuccessStatusCode();
        var compte = await inscription.LireAsync<InscriptionReponse>();

        var login = await client.LoginAsync(email, MotDePasseUtilisateur);
        client.UtiliserToken(login.AccessToken);
        return new UtilisateurTest(client, login.UserId, email, compte.ClientId);
    }

    public static async Task<HttpClient> CreerAdminAsync(this ApiFactory factory)
    {
        var client = factory.CreerClient();
        var login = await client.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        client.UtiliserToken(login.AccessToken);
        return client;
    }

    // ---------- Données ----------

    public static async Task<ProduitDto> CreerProduitAsync(
        this HttpClient admin, int stock, decimal prix = 10m, bool estDisponible = true)
    {
        var dto = new CreateProduitDto
        {
            NomProduit = $"IT-{Guid.NewGuid():N}"[..15],
            Description = "Produit de test d'intégration",
            PrixUnitaireTTC = prix,
            Stock = stock
        };

        var reponse = await admin.PostJsonAsync("/api/Produit", dto, Guid.NewGuid().ToString("N"));
        reponse.EnsureSuccessStatusCode();
        return await reponse.LireAsync<ProduitDto>();
    }

    public static async Task<ProduitDto> LireProduitAsync(this HttpClient client, int produitId)
    {
        var reponse = await client.GetAsync($"/api/Produit/{produitId}");
        reponse.EnsureSuccessStatusCode();
        return await reponse.LireAsync<ProduitDto>();
    }

    public static async Task<CommandeDto> LireCommandeAsync(this HttpClient client, int commandeId)
    {
        var reponse = await client.GetAsync($"/api/Commande/{commandeId}");
        reponse.EnsureSuccessStatusCode();
        return await reponse.LireAsync<CommandeDto>();
    }

    /// <summary>Un client existant (les clients sont créés par le jeu de données initial de l'API).</summary>
    public static Task<int> UnClientIdAsync(this ApiFactory factory) =>
        factory.DansLaBaseAsync(db => db.Clients.OrderBy(c => c.ClientId).Select(c => c.ClientId).FirstAsync());

    public static async Task<ClientDto> LireClientAsync(this HttpClient client, int clientId)
    {
        var reponse = await client.GetAsync($"/api/Client/{clientId}");
        reponse.EnsureSuccessStatusCode();
        return await reponse.LireAsync<ClientDto>();
    }

    public static CreateCommandeDto NouvelleCommande(int clientId, int produitId, int quantite) => new()
    {
        ClientId = clientId,
        LignesCommande = [new CreateLigneCommandeDto { ProduitId = produitId, Quantite = quantite }]
    };

    public static async Task<CommandeDto> CreerCommandeAsync(
        this HttpClient client, int clientId, int produitId, int quantite)
    {
        var reponse = await client.PostJsonAsync(
            "/api/Commande", NouvelleCommande(clientId, produitId, quantite), Guid.NewGuid().ToString("N"));
        reponse.EnsureSuccessStatusCode();
        return await reponse.LireAsync<CommandeDto>();
    }
}
