using System.Net;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

[Collection(ApiCollection.Nom)]
public class CommandeEndpointsTests
{
    private readonly ApiFactory _factory;

    public CommandeEndpointsTests(ApiFactory factory) => _factory = factory;

    private static UpdateCommandeDto Modification(string rowVersion, int produitId, int quantite) => new()
    {
        RowVersion = rowVersion,
        LignesCommande = [new LignesCommandeDto { ProduitId = produitId, Quantite = quantite }]
    };

    /// <summary>Un produit de 10 unités à 10 € et un utilisateur prêt à commander.</summary>
    private async Task<(HttpClient admin, UtilisateurTest utilisateur, ProduitDto produit, int clientId)> PreparerAsync(int stock = 10)
    {
        var admin = await _factory.CreerAdminAsync();
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var produit = await admin.CreerProduitAsync(stock, prix: 10m);
        // Chaque utilisateur inscrit possède son client (relation 0..1) : il ne commande que pour lui.
        return (admin, utilisateur, produit, utilisateur.ClientId);
    }

    // =====================================================================
    // Création
    // =====================================================================

    [Fact]
    public async Task Create_Valide_Retourne201AvecLeTotalEtLesLignes()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 3),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        Assert.NotNull(reponse.Headers.Location);
        var commande = await reponse.LireAsync<CommandeDto>();
        Assert.True(commande.CommandeId > 0);
        Assert.Equal(StatutCommande.EnCours, commande.Statut);
        Assert.Equal(30m, commande.TotalCommandeTTC);
        var ligne = Assert.Single(commande.LignesCommande);
        Assert.Equal(produit.ProduitId, ligne.ProduitId);
        Assert.Equal(3, ligne.Quantite);
    }

    [Fact]
    public async Task Create_DecrementeLeStock()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);

        await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 3);

        Assert.Equal(7, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Create_JournaliseUnMouvementDeSortieEtUnAuditInsert()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);

        await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 3);

        var mouvements = await _factory.DansLaBaseAsync(db => db.StockMouvements
            .Where(m => m.ProduitId == produit.ProduitId && m.SourceOperation == "CreationCommande")
            .ToListAsync());
        var mouvement = Assert.Single(mouvements);
        Assert.Equal(TypeMouvementStock.Sortie, mouvement.TypeMouvement);
        Assert.Equal(10, mouvement.StockAvant);
        Assert.Equal(7, mouvement.StockApres);
        Assert.Equal(3, mouvement.Quantite); // magnitude positive, le sens est porté par TypeMouvement
        Assert.False(string.IsNullOrWhiteSpace(mouvement.OperationId));

        // Le mouvement et l'audit partagent le même identifiant de corrélation.
        var audits = await _factory.DansLaBaseAsync(db => db.AuditLogs
            .Where(a => a.CorrelationId == mouvement.OperationId)
            .ToListAsync());
        var audit = Assert.Single(audits);
        Assert.Equal(nameof(Commandes), audit.EntityName);
        Assert.Equal("INSERT", audit.ActionType);
    }

    [Fact]
    public async Task Create_SansToken_Retourne401()
    {
        var (_, _, produit, clientId) = await PreparerAsync();
        var anonyme = _factory.CreerClient();

        var reponse = await anonyme.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 1),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_SansCleDIdempotence_Retourne400()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 1),
            idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_MemeCleMemeContenu_RenvoieLaMemeCommandeEtNeDecrementeQuUneFois()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        var dto = ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 3);
        var cle = Guid.NewGuid().ToString("N");

        var premiere = await utilisateur.Client.PostJsonAsync("/api/Commande", dto, cle);
        var rejeu = await utilisateur.Client.PostJsonAsync("/api/Commande", dto, cle);

        Assert.Equal(HttpStatusCode.Created, premiere.StatusCode);
        Assert.Equal(HttpStatusCode.Created, rejeu.StatusCode);
        var c1 = await premiere.LireAsync<CommandeDto>();
        var c2 = await rejeu.LireAsync<CommandeDto>();
        Assert.Equal(c1.CommandeId, c2.CommandeId);
        Assert.Equal(7, (await admin.LireProduitAsync(produit.ProduitId)).Stock); // 10 - 3, pas 10 - 6
    }

    [Fact]
    public async Task Create_MemeCleContenuDifferent_Retourne422()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        var cle = Guid.NewGuid().ToString("N");
        await utilisateur.Client.PostJsonAsync("/api/Commande", ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 3), cle);

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande", ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 4), cle);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, reponse.StatusCode);
        Assert.Equal(7, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Create_StockInsuffisant_Retourne400EtLeStockNeBougePas()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 2);

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 5),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
        Assert.Equal(2, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Create_ProduitIndisponible_Retourne400()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        await admin.SupprimerAsync($"/api/Produit/{produit.ProduitId}", produit.RowVersion); // archive le produit

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 1),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_PourLeClientDUnAutreUtilisateur_Retourne403()
    {
        var (_, utilisateur, produit, _) = await PreparerAsync();
        var autre = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(autre.ClientId, produit.ProduitId, 1),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_Administrateur_PeutCommanderPourNImporteQuelClient()
    {
        var (admin, _, produit, _) = await PreparerAsync();
        var clientId = await _factory.UnClientIdAsync(); // client du jeu de données initial

        var reponse = await admin.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 2),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_ClientDesactive_Retourne400()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var client = await utilisateur.Client.LireClientAsync(clientId);
        await utilisateur.Client.SupprimerAsync($"/api/Client/{clientId}", client.RowVersion); // désactive son client

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, produit.ProduitId, 1),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_ClientInexistant_Retourne400()
    {
        var (admin, _, produit, _) = await PreparerAsync();

        var reponse = await admin.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(999999999, produit.ProduitId, 1),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_ProduitInexistant_Retourne400()
    {
        var (_, utilisateur, _, clientId) = await PreparerAsync();

        var reponse = await utilisateur.Client.PostJsonAsync(
            "/api/Commande",
            ApiTestHelpers.NouvelleCommande(clientId, 999999999, 1),
            Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    // =====================================================================
    // Lecture et contrôle d'accès
    // =====================================================================

    [Fact]
    public async Task Get_Proprietaire_Retourne200()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);

        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);

        Assert.Equal(commande.CommandeId, lue.CommandeId);
    }

    [Fact]
    public async Task Get_CommandeDUnAutreUtilisateur_Retourne404()
    {
        var (_, proprietaire, produit, clientId) = await PreparerAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var commande = await proprietaire.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);

        var reponse = await intrus.Client.GetAsync($"/api/Commande/{commande.CommandeId}");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Get_CommandeEnCacheDUnAutreUtilisateur_Retourne404()
    {
        // Le propriétaire lit d'abord sa commande (mise en cache), l'intrus la demande ensuite.
        var (_, proprietaire, produit, clientId) = await PreparerAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var commande = await proprietaire.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        await proprietaire.Client.LireCommandeAsync(commande.CommandeId);

        var reponse = await intrus.Client.GetAsync($"/api/Commande/{commande.CommandeId}");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Get_Administrateur_PeutLireLaCommandeDUnUtilisateur()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);

        var lue = await admin.LireCommandeAsync(commande.CommandeId);

        Assert.Equal(commande.CommandeId, lue.CommandeId);
    }

    [Fact]
    public async Task GetListe_UtilisateurStandard_NeVoitQueSesCommandes()
    {
        var (_, utilisateurA, produit, clientId) = await PreparerAsync();
        var utilisateurB = await _factory.CreerUtilisateurAsync();
        var commandeA = await utilisateurA.Client.CreerCommandeAsync(clientId, produit.ProduitId, 1);
        var commandeB = await utilisateurB.Client.CreerCommandeAsync(utilisateurB.ClientId, produit.ProduitId, 1);

        var reponse = await utilisateurA.Client.GetAsync("/api/Commande");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var ids = (await reponse.LireAsync<List<CommandeDto>>()).Select(c => c.CommandeId).ToList();
        Assert.Contains(commandeA.CommandeId, ids);
        Assert.DoesNotContain(commandeB.CommandeId, ids);
    }

    [Fact]
    public async Task GetListe_Administrateur_VoitLesCommandesDeTousLesUtilisateurs()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 1);

        var reponse = await admin.GetAsync("/api/Commande");

        var ids = (await reponse.LireAsync<List<CommandeDto>>()).Select(c => c.CommandeId).ToList();
        Assert.Contains(commande.CommandeId, ids);
    }

    // =====================================================================
    // Modification
    // =====================================================================

    [Fact]
    public async Task Put_AugmenteLaQuantite_AjusteLeStockEtLeTotal()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 3); // stock 7
        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);

        var reponse = await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(lue.RowVersion, produit.ProduitId, 5));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        var modifiee = await reponse.LireAsync<CommandeDto>();
        Assert.Equal(50m, modifiee.TotalCommandeTTC);
        Assert.Equal(5, Assert.Single(modifiee.LignesCommande).Quantite);
        Assert.Equal(5, (await admin.LireProduitAsync(produit.ProduitId)).Stock); // 10 - 5
    }

    [Fact]
    public async Task Put_JournaliseLaVariationNetteDeStockAvecLeStockReel()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 3); // stock 7
        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);

        await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(lue.RowVersion, produit.ProduitId, 5));

        var mouvements = await _factory.DansLaBaseAsync(db => db.StockMouvements
            .Where(m => m.ProduitId == produit.ProduitId && m.SourceOperation == "ModificationCommande")
            .ToListAsync());
        var mouvement = Assert.Single(mouvements);
        Assert.Equal(TypeMouvementStock.Sortie, mouvement.TypeMouvement);
        Assert.Equal(7, mouvement.StockAvant);
        Assert.Equal(5, mouvement.StockApres);
        Assert.Equal(2, mouvement.Quantite);
    }

    [Fact]
    public async Task Put_RowVersionPerimee_Retourne409EtLeStockNeBougePas()
    {
        var (admin, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 3);
        var versionInitiale = (await utilisateur.Client.LireCommandeAsync(commande.CommandeId)).RowVersion;
        var premiere = await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(versionInitiale, produit.ProduitId, 4));
        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode); // stock 6

        var perimee = await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(versionInitiale, produit.ProduitId, 8));

        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
        Assert.Equal(6, (await admin.LireProduitAsync(produit.ProduitId)).Stock);
    }

    [Fact]
    public async Task Put_RowVersionInvalide_Retourne400()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);

        var reponse = await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification("pas-du-base64!", produit.ProduitId, 3));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Put_StockInsuffisant_Retourne400()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync(stock: 10);
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 3);
        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);

        var reponse = await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(lue.RowVersion, produit.ProduitId, 50));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Put_CommandeDUnAutreUtilisateur_Retourne404()
    {
        var (_, proprietaire, produit, clientId) = await PreparerAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var commande = await proprietaire.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        var lue = await proprietaire.Client.LireCommandeAsync(commande.CommandeId);

        var reponse = await intrus.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(lue.RowVersion, produit.ProduitId, 1));

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    // =====================================================================
    // Archivage
    // =====================================================================

    [Fact]
    public async Task Archive_AvecIfMatch_Retourne204EtPasseLeStatutAArchivee()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);

        var reponse = await utilisateur.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", lue.RowVersion);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        var apres = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);
        Assert.Equal(StatutCommande.Archivee, apres.Statut);
    }

    [Fact]
    public async Task Archive_JournaliseUnAuditArchive()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);

        await utilisateur.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", lue.RowVersion);

        var audits = await _factory.DansLaBaseAsync(db => db.AuditLogs
            .Where(a => a.EntityName == nameof(Commandes)
                        && a.EntityId == commande.CommandeId.ToString()
                        && a.ActionType == "ARCHIVE")
            .ToListAsync());
        var audit = Assert.Single(audits);
        Assert.NotNull(audit.OldValue);
        Assert.NotNull(audit.NewValue);
    }

    [Fact]
    public async Task Archive_SansIfMatch_Retourne428()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);

        var reponse = await utilisateur.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, reponse.StatusCode);
    }

    [Fact]
    public async Task Archive_IfMatchPerime_Retourne409()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        var versionInitiale = (await utilisateur.Client.LireCommandeAsync(commande.CommandeId)).RowVersion;
        await utilisateur.Client.PutJsonAsync(
            $"/api/Commande/{commande.CommandeId}", Modification(versionInitiale, produit.ProduitId, 3));

        var reponse = await utilisateur.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", versionInitiale);

        Assert.Equal(HttpStatusCode.Conflict, reponse.StatusCode);
    }

    [Fact]
    public async Task Archive_DejaArchivee_Retourne400()
    {
        var (_, utilisateur, produit, clientId) = await PreparerAsync();
        var commande = await utilisateur.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        var lue = await utilisateur.Client.LireCommandeAsync(commande.CommandeId);
        await utilisateur.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", lue.RowVersion);

        var reponse = await utilisateur.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", lue.RowVersion);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Archive_CommandeDUnAutreUtilisateur_Retourne404()
    {
        var (_, proprietaire, produit, clientId) = await PreparerAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var commande = await proprietaire.Client.CreerCommandeAsync(clientId, produit.ProduitId, 2);
        var lue = await proprietaire.Client.LireCommandeAsync(commande.CommandeId);

        var reponse = await intrus.Client.SupprimerAsync($"/api/Commande/delete/{commande.CommandeId}", lue.RowVersion);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }
}
