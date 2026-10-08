using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

/// <summary>
/// Vérifie le décrément de stock atomique : des commandes simultanées ne vendent jamais plus que le stock,
/// et ne se refusent pas entre elles quand le stock suffit.
/// </summary>
[Collection(ApiCollection.Nom)]
public class CommandeConcurrenceTests
{
    private readonly ApiFactory _factory;

    public CommandeConcurrenceTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_CommandesSimultanees_NeVendentJamaisPlusQueLeStock()
    {
        const int stock = 5;
        const int demandes = 12;

        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock);

        // Chaque utilisateur commande 1 unité pour son propre client (relation 0..1).
        var utilisateurs = new List<UtilisateurTest>();
        for (var i = 0; i < demandes; i++)
        {
            utilisateurs.Add(await _factory.CreerUtilisateurAsync());
        }

        // Toutes les requêtes sont prêtes avant d'être lâchées en même temps.
        var depart = new TaskCompletionSource();
        var requetes = utilisateurs.Select(async utilisateur =>
        {
            await depart.Task;
            return await utilisateur.Client.PostJsonAsync(
                "/api/Commande",
                ApiTestHelpers.NouvelleCommande(utilisateur.ClientId, produit.ProduitId, 1),
                Guid.NewGuid().ToString("N"));
        }).ToList();

        depart.SetResult();
        var reponses = await Task.WhenAll(requetes);

        var acceptees = reponses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var refusees = reponses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();

        // Exactement `stock` commandes passent, les autres sont refusées pour stock insuffisant
        // (400 si le stock vide était déjà visible, 409 s'il a été pris entre-temps), jamais d'erreur serveur.
        Assert.Equal(stock, acceptees);
        Assert.Equal(demandes - stock, refusees.Count);
        Assert.All(refusees, r =>
            Assert.True(
                r.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
                $"Statut inattendu : {(int)r.StatusCode}"));

        // Le stock est à zéro, jamais négatif, et le journal des mouvements est cohérent.
        Assert.Equal(0, (await admin.LireProduitAsync(produit.ProduitId)).Stock);

        var mouvements = await _factory.DansLaBaseAsync(db => db.StockMouvements
            .Where(m => m.ProduitId == produit.ProduitId && m.SourceOperation == "CreationCommande")
            .ToListAsync());
        Assert.Equal(stock, mouvements.Count);
        Assert.Equal(stock, mouvements.Sum(m => m.Quantite));
        Assert.All(mouvements, m => Assert.True(m.StockApres >= 0));
        Assert.All(mouvements, m => Assert.Equal(m.StockAvant - m.Quantite, m.StockApres));
        // Les stocks après chaque sortie forment exactement la suite 0..4 : aucune vente « en double ».
        Assert.Equal(
            Enumerable.Range(0, stock).ToList(),
            mouvements.Select(m => m.StockApres).OrderBy(s => s).ToList());
    }

    [Fact]
    public async Task Create_CommandesSimultanees_QuandLeStockSuffit_ToutesPassent()
    {
        // Avant le décrément atomique, une commande pouvait être rejetée par un conflit de RowVersion
        // même si le stock suffisait pour toutes.
        const int demandes = 8;

        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 100);

        var utilisateurs = new List<UtilisateurTest>();
        for (var i = 0; i < demandes; i++)
        {
            utilisateurs.Add(await _factory.CreerUtilisateurAsync());
        }

        var depart = new TaskCompletionSource();
        var requetes = utilisateurs.Select(async utilisateur =>
        {
            await depart.Task;
            return await utilisateur.Client.PostJsonAsync(
                "/api/Commande",
                ApiTestHelpers.NouvelleCommande(utilisateur.ClientId, produit.ProduitId, 2),
                Guid.NewGuid().ToString("N"));
        }).ToList();

        depart.SetResult();
        var reponses = await Task.WhenAll(requetes);

        Assert.All(reponses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(100 - demandes * 2, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }
}
