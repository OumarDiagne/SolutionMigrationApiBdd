using System.Net;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

[Collection(ApiCollection.Nom)]
public class ProduitEndpointsTests
{
    private readonly ApiFactory _factory;

    public ProduitEndpointsTests(ApiFactory factory) => _factory = factory;

    private static CreateProduitDto NouveauProduit(int stock = 10) => new()
    {
        NomProduit = $"IT-{Guid.NewGuid():N}"[..15],
        PrixUnitaireTTC = 10m,
        Stock = stock
    };

    [Fact]
    public async Task Create_Administrateur_Retourne201EtLeProduit()
    {
        var admin = await _factory.CreerAdminAsync();
        var dto = NouveauProduit(stock: 12);

        var reponse = await admin.PostJsonAsync("/api/Produit", dto, Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.NotNull(reponse.Headers.Location);
        var produit = await reponse.LireAsync<ProduitDto>();
        Assert.True(produit.ProduitId > 0);
        Assert.Equal(dto.NomProduit, produit.NomProduit);
        Assert.Equal(12, produit.Stock);
        Assert.True(produit.EstDisponible);
        Assert.False(string.IsNullOrWhiteSpace(produit.RowVersion));
    }

    [Fact]
    public async Task Create_JournaliseLeStockInitialEtUnAuditInsert()
    {
        var admin = await _factory.CreerAdminAsync();

        var produit = await admin.CreerProduitAsync(stock: 12);

        var mouvements = await _factory.DansLaBaseAsync(db =>
            db.StockMouvements.Where(m => m.ProduitId == produit.ProduitId).ToListAsync());
        var mouvement = Assert.Single(mouvements);
        Assert.Equal(TypeMouvementStock.Entree, mouvement.TypeMouvement);
        Assert.Equal(12, mouvement.Quantite);
        Assert.Equal(0, mouvement.StockAvant);
        Assert.Equal(12, mouvement.StockApres);

        var audits = await _factory.DansLaBaseAsync(db =>
            db.AuditLogs.Where(a => a.EntityName == nameof(Produits) && a.EntityId == produit.ProduitId.ToString()).ToListAsync());
        var audit = Assert.Single(audits);
        Assert.Equal("INSERT", audit.ActionType);
        Assert.Null(audit.OldValue);
        Assert.NotNull(audit.NewValue);
    }

    [Fact]
    public async Task Create_UtilisateurStandard_Retourne403()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.PostJsonAsync("/api/Produit", NouveauProduit(), Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_SansToken_Retourne401()
    {
        var client = _factory.CreerClient();

        var reponse = await client.PostJsonAsync("/api/Produit", NouveauProduit(), Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_SansCleDIdempotence_Retourne400()
    {
        var admin = await _factory.CreerAdminAsync();

        var reponse = await admin.PostJsonAsync("/api/Produit", NouveauProduit(), idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_MemeCleMemeContenu_RenvoieLeMemeProduitSansDoublon()
    {
        var admin = await _factory.CreerAdminAsync();
        var dto = NouveauProduit();
        var cle = Guid.NewGuid().ToString("N");

        var premiere = await admin.PostJsonAsync("/api/Produit", dto, cle);
        var rejeu = await admin.PostJsonAsync("/api/Produit", dto, cle);

        Assert.Equal(HttpStatusCode.Created, premiere.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rejeu.StatusCode);
        var p1 = await premiere.LireAsync<ProduitDto>();
        var p2 = await rejeu.LireAsync<ProduitDto>();
        Assert.Equal(p1.ProduitId, p2.ProduitId);

        var nombre = await _factory.DansLaBaseAsync(db => db.Produits.CountAsync(p => p.NomProduit == dto.NomProduit));
        Assert.Equal(1, nombre);
    }

    [Fact]
    public async Task Create_MemeCleContenuDifferent_Retourne422()
    {
        var admin = await _factory.CreerAdminAsync();
        var cle = Guid.NewGuid().ToString("N");
        await admin.PostJsonAsync("/api/Produit", NouveauProduit(stock: 5), cle);

        var reponse = await admin.PostJsonAsync("/api/Produit", NouveauProduit(stock: 99), cle);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
    }

    [Fact]
    public async Task Get_ProduitInexistant_Retourne404()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.GetAsync("/api/Produit/999999999");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Get_UtilisateurStandard_PeutLireUnProduit()
    {
        var admin = await _factory.CreerAdminAsync();
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var produit = await admin.CreerProduitAsync(stock: 4);

        var lu = await utilisateur.Client.LireProduitAsync(produit.ProduitId);

        Assert.Equal(produit.NomProduit, lu.NomProduit);
        Assert.Equal(4, lu.Stock);
    }

    [Fact]
    public async Task Put_ModifieLeProduitEtChangeLaRowVersion()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.PutJsonAsync($"/api/Produit/{produit.ProduitId}", new UpdateProduitDto
        {
            NomProduit = produit.NomProduit,
            PrixUnitaireTTC = 25m,
            Stock = 10,
            EstDisponible = true,
            RowVersion = produit.RowVersion
        });

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var modifie = await reponse.LireAsync<ProduitDto>();
        Assert.Equal(25m, modifie.PrixUnitaireTTC);
        Assert.NotEqual(produit.RowVersion, modifie.RowVersion);
    }

    [Fact]
    public async Task Put_RowVersionPerimee_Retourne409()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);
        UpdateProduitDto Modification(decimal prix) => new()
        {
            NomProduit = produit.NomProduit,
            PrixUnitaireTTC = prix,
            Stock = 10,
            EstDisponible = true,
            RowVersion = produit.RowVersion // toujours la version lue au départ
        };
        var premiere = await admin.PutJsonAsync($"/api/Produit/{produit.ProduitId}", Modification(20m));
        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);

        var perimee = await admin.PutJsonAsync($"/api/Produit/{produit.ProduitId}", Modification(30m));

        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        var enBase = await admin.LireProduitAsync(produit.ProduitId);
        Assert.Equal(20m, enBase.PrixUnitaireTTC);
    }

    [Fact]
    public async Task Put_UtilisateurStandard_Retourne403()
    {
        var admin = await _factory.CreerAdminAsync();
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await utilisateur.Client.PutJsonAsync($"/api/Produit/{produit.ProduitId}", new UpdateProduitDto
        {
            NomProduit = produit.NomProduit,
            PrixUnitaireTTC = 1m,
            Stock = 10,
            RowVersion = produit.RowVersion
        });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Delete_SansIfMatch_Retourne428()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.SupprimerAsync($"/api/Produit/{produit.ProduitId}", ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, reponse.StatusCode);
    }

    [Fact]
    public async Task Delete_AvecIfMatch_ArchiveLeProduit()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);

        var reponse = await admin.SupprimerAsync($"/api/Produit/{produit.ProduitId}", produit.RowVersion);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        var archive = await admin.LireProduitAsync(produit.ProduitId);
        Assert.False(archive.EstDisponible);
    }

    [Fact]
    public async Task Delete_IfMatchPerime_Retourne409()
    {
        var admin = await _factory.CreerAdminAsync();
        var produit = await admin.CreerProduitAsync(stock: 10);
        await admin.PutJsonAsync($"/api/Produit/{produit.ProduitId}", new UpdateProduitDto
        {
            NomProduit = produit.NomProduit,
            PrixUnitaireTTC = 11m,
            Stock = 10,
            EstDisponible = true,
            RowVersion = produit.RowVersion
        });

        var reponse = await admin.SupprimerAsync($"/api/Produit/{produit.ProduitId}", produit.RowVersion);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
    }
}
