using System.Net;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

[Collection(ApiCollection.Nom)]
public class StockEndpointsTests
{
    private readonly ApiFactory _factory;

    public StockEndpointsTests(ApiFactory factory) => _factory = factory;

    private static string Url(int produitId) => $"/api/Stock/reapprovisionner/{produitId}";

    private static ReapprovisionnerStockDto Reappro(int quantite, string rowVersion, string? motif = null) => new()
    {
        Quantite = quantite,
        RowVersion = rowVersion,
        Motif = motif
    };

    [Fact]
    public async Task Reapprovisionner_SansToken_Retourne401()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);
        var anonyme = _factory.CreerClient();

        var reponse = await anonyme.PostJsonAsync(Url(produit.ProduitId), Reappro(5, produit.RowVersion));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
        Assert.Equal(10, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Reapprovisionner_UtilisateurStandard_Retourne403()
    {
        var admin = await _factory.CreerAdminAsync();
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await utilisateur.Client.PostJsonAsync(Url(produit.ProduitId), Reappro(5, produit.RowVersion));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
        Assert.Equal(10, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Reapprovisionner_Administrateur_AugmenteLeStock()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.PostJsonAsync(Url(produit.ProduitId), Reappro(5, produit.RowVersion, "Livraison fournisseur"));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var corps = await reponse.LireAsync<ProduitDto>();
        Assert.Equal(15, corps.Stock);
        Assert.Equal(15, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Reapprovisionner_JournaliseLeMouvementEtUnAudit()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        await admin.PostJsonAsync(Url(produit.ProduitId), Reappro(5, produit.RowVersion, "Livraison fournisseur"));

        var mouvements = await _factory.DansLaBaseAsync(db => db.StockMouvements
            .Where(m => m.ProduitId == produit.ProduitId && m.SourceOperation == "Reapprovisionnement")
            .ToListAsync());
        var mouvement = Assert.Single(mouvements);
        Assert.Equal(TypeMouvementStock.Entree, mouvement.TypeMouvement);
        Assert.Equal(5, mouvement.Quantite);
        Assert.Equal(10, mouvement.StockAvant);
        Assert.Equal(15, mouvement.StockApres);
        Assert.Equal("Livraison fournisseur", mouvement.Motif);

        var audits = await _factory.DansLaBaseAsync(db => db.AuditLogs
            .Where(a => a.EntityName == nameof(Produits)
                        && a.EntityId == produit.ProduitId.ToString()
                        && a.ActionType == "REAPPROVISIONNEMENT")
            .ToListAsync());
        var audit = Assert.Single(audits);
        Assert.Equal(ApiFactory.AdminEmail, audit.ChangedBy);
        Assert.NotNull(audit.OldValue);
        Assert.NotNull(audit.NewValue);
    }

    [Fact]
    public async Task Reapprovisionner_RowVersionPerimee_Retourne409EtLeStockResteCelDeLaPremiereOperation()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);
        var premiere = await admin.PostJsonAsync(Url(produit.ProduitId), Reappro(5, produit.RowVersion));
        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);

        var perimee = await admin.PostJsonAsync(Url(produit.ProduitId), Reappro(5, produit.RowVersion));

        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        Assert.Equal(15, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Reapprovisionner_RowVersionInvalide_Retourne400()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.PostJsonAsync(Url(produit.ProduitId), Reappro(5, "pas-du-base64!"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(10, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Reapprovisionner_QuantiteNonPositive_Retourne400(int quantite)
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.PostJsonAsync(Url(produit.ProduitId), Reappro(quantite, produit.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Reapprovisionner_ProduitInexistant_Retourne400()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.PostJsonAsync(Url(999999999), Reappro(5, produit.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }
}
