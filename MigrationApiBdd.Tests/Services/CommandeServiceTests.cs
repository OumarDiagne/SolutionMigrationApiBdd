using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MigrationApiBdd.DAL;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Helpers;
using MigrationApiBdd.Models;
using MigrationApiBdd.Services.Auth;
using MigrationApiBdd.Services.Classes;
using MigrationApiBdd.Tests.Support;
using Xunit;

namespace MigrationApiBdd.Tests.Services;

/// <summary>
/// Tests unitaires de CommandeService (Get / Update / Archive + gardes de Create).
/// La création complète (transaction + idempotence en base) est couverte par les tests
/// d'intégration : elle dépend de MigApiContext et de SQL Server.
/// </summary>
public class CommandeServiceTests : IDisposable
{
    private static readonly byte[] RowVersionCommande = [9, 8, 7, 6, 5, 4, 3, 2];

    private readonly Mock<ICommandeRepository> _commandeRepo = new();
    private readonly Mock<IClientRepository> _clientRepo = new();
    private readonly Mock<IProduitRepository> _produitRepo = new();
    private readonly Mock<IOperationLogRepository> _operationLogRepo = new();
    private readonly Mock<IValidator<CreateCommandeDto>> _validator = new();
    private readonly Mock<IStockMouvementRepository> _stockRepo = new();
    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IDbContextTransaction> _transaction = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private readonly List<AuditLog> _audits = [];
    private readonly List<StockMouvement> _mouvements = [];

    public CommandeServiceTests()
    {
        TestMapster.Init();

        // Utilisateur standard connecté par défaut.
        _currentUser.SetupGet(u => u.UserId).Returns("user-1");
        _currentUser.SetupGet(u => u.UserName).Returns("alice");
        _currentUser.SetupGet(u => u.IsAdmin).Returns(false);

        _auditRepo
            .Setup(r => r.AddRange(It.IsAny<List<AuditLog>>()))
            .Callback<List<AuditLog>>(l => _audits.AddRange(l));
        _stockRepo
            .Setup(r => r.AddRange(It.IsAny<List<StockMouvement>>()))
            .Callback<List<StockMouvement>>(l => _mouvements.AddRange(l));

        // Le repository réel applique le stock par un UPDATE atomique en base. Ici le service a déjà calculé
        // le stock en mémoire : l'UPDATE réussit par défaut (le cas « stock pris entre-temps » a son propre test).
        _produitRepo
            .Setup(r => r.TryAppliquerVariationStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _commandeRepo
            .Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_transaction.Object);
    }

    public void Dispose() => _cache.Dispose();

    // MigApiContext n'est utilisé que par CreateCommandeAsync (après les gardes) : null ici.
    private CommandeService CreerService() => new(
        _commandeRepo.Object,
        _clientRepo.Object,
        _produitRepo.Object,
        _operationLogRepo.Object,
        _validator.Object,
        _stockRepo.Object,
        _auditRepo.Object,
        dbContext: null!,
        _currentUser.Object,
        _cache);

    // ---------- Données de test ----------

    private static Produits CreerProduit(int id, int stock, decimal prix = 10m, bool disponible = true) => new()
    {
        ProduitId = id,
        NomProduit = $"Produit {id}",
        PrixUnitaireTTC = prix,
        Stock = stock,
        EstDisponible = disponible,
        RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]
    };

    private static Commandes CreerCommande(
        int id = 100,
        string proprietaire = "user-1",
        StatutCommande statut = StatutCommande.EnCours,
        (int produitId, int quantite, decimal prix)[]? lignes = null)
    {
        lignes ??= [];
        var commande = new Commandes
        {
            CommandeId = id,
            ClientId = 1,
            CreatedByUserId = proprietaire,
            Statut = statut,
            DateCommande = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RowVersion = RowVersionCommande
        };

        foreach (var (produitId, quantite, prix) in lignes)
        {
            commande.LignesCommande.Add(new LignesCommande
            {
                CommandeId = id,
                ProduitId = produitId,
                Quantite = quantite,
                PrixUnitaireTTC = prix
            });
        }

        commande.TotalCommandeTTC = lignes.Sum(l => l.quantite * l.prix);
        return commande;
    }

    private void CommandeExiste(Commandes commande) =>
        _commandeRepo
            .Setup(r => r.GetCommandeByIdAsync(commande.CommandeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(commande);

    private void ProduitsExistent(params Produits[] produits) =>
        _produitRepo
            .Setup(r => r.GetAllProduitsByIdsAsync(It.IsAny<List<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ICollection<Produits>)produits.ToList());

    private static UpdateCommandeDto CreerUpdateDto(params (int produitId, int quantite)[] lignes) => new()
    {
        RowVersion = Convert.ToBase64String(RowVersionCommande),
        LignesCommande = lignes
            .Select(l => new LignesCommandeDto { ProduitId = l.produitId, Quantite = l.quantite })
            .ToList()
    };

    private static string RowVersionBase64 => Convert.ToBase64String(RowVersionCommande);

    // =====================================================================
    // GetCommandeByIdAsync
    // =====================================================================

    [Fact]
    public async Task GetById_CommandeInexistante_RetourneNull()
    {
        _commandeRepo
            .Setup(r => r.GetCommandeByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commandes?)null);

        var resultat = await CreerService().GetCommandeByIdAsync(404, CancellationToken.None);

        Assert.Null(resultat);
    }

    [Fact]
    public async Task GetById_CommandeDUnAutreUtilisateur_RetourneNullCommeSiElleNExistaitPas()
    {
        CommandeExiste(CreerCommande(proprietaire: "autre-user", lignes: [(1, 2, 10m)]));

        var resultat = await CreerService().GetCommandeByIdAsync(100, CancellationToken.None);

        Assert.Null(resultat);
    }

    [Fact]
    public async Task GetById_Proprietaire_RetourneLeDto()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));

        var resultat = await CreerService().GetCommandeByIdAsync(100, CancellationToken.None);

        Assert.NotNull(resultat);
        Assert.Equal(100, resultat!.CommandeId);
        Assert.Equal(20m, resultat.TotalCommandeTTC);
    }

    [Fact]
    public async Task GetById_Admin_PeutLireLaCommandeDUnAutreUtilisateur()
    {
        _currentUser.SetupGet(u => u.IsAdmin).Returns(true);
        CommandeExiste(CreerCommande(proprietaire: "autre-user", lignes: [(1, 2, 10m)]));

        var resultat = await CreerService().GetCommandeByIdAsync(100, CancellationToken.None);

        Assert.NotNull(resultat);
    }

    [Fact]
    public async Task GetById_DeuxiemeAppel_EstServiDepuisLeCache()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        var service = CreerService();

        await service.GetCommandeByIdAsync(100, CancellationToken.None);
        await service.GetCommandeByIdAsync(100, CancellationToken.None);

        _commandeRepo.Verify(r => r.GetCommandeByIdAsync(100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_CommandeEnCache_LeControleDAccesEstRefaitPourUnAutreUtilisateur()
    {
        CommandeExiste(CreerCommande(proprietaire: "user-1", lignes: [(1, 2, 10m)]));
        var service = CreerService();
        await service.GetCommandeByIdAsync(100, CancellationToken.None); // mise en cache par user-1

        _currentUser.SetupGet(u => u.UserId).Returns("user-2");
        var resultat = await service.GetCommandeByIdAsync(100, CancellationToken.None);

        Assert.Null(resultat);
    }

    [Fact]
    public async Task GetById_SansUtilisateurIdentifie_Leve401()
    {
        _currentUser.SetupGet(u => u.UserId).Returns((string?)null);
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().GetCommandeByIdAsync(100, CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
    }

    // =====================================================================
    // GetAllCommandesAsync
    // =====================================================================

    [Fact]
    public async Task GetAll_SansUtilisateurIdentifie_Leve401()
    {
        _currentUser.SetupGet(u => u.UserId).Returns((string?)null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().GetAllCommandesAsync(CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task GetAll_UtilisateurStandard_NeRetourneQueSesCommandes()
    {
        _commandeRepo
            .Setup(r => r.GetAllCommandesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Commandes> { CreerCommande(1, "user-1"), CreerCommande(2, "autre-user") });
        _commandeRepo
            .Setup(r => r.GetByOwnerAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Commandes> { CreerCommande(1, "user-1") });

        var resultat = (await CreerService().GetAllCommandesAsync(CancellationToken.None)).ToList();

        var commande = Assert.Single(resultat);
        Assert.Equal(1, commande.CommandeId);
    }

    [Fact]
    public async Task GetAll_Admin_RetourneToutesLesCommandes()
    {
        _currentUser.SetupGet(u => u.IsAdmin).Returns(true);
        _commandeRepo
            .Setup(r => r.GetAllCommandesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Commandes> { CreerCommande(1, "user-1"), CreerCommande(2, "autre-user") });

        var resultat = (await CreerService().GetAllCommandesAsync(CancellationToken.None)).ToList();

        Assert.Equal(2, resultat.Count);
    }

    // =====================================================================
    // CreateCommandeAsync : gardes exécutées avant toute écriture en base
    // =====================================================================

    private static CreateCommandeDto CreerCreateDto() => new()
    {
        ClientId = 1,
        LignesCommande = [new CreateLigneCommandeDto { ProduitId = 1, Quantite = 2 }]
    };

    [Fact]
    public async Task Create_SansUtilisateurIdentifie_Leve401()
    {
        _currentUser.SetupGet(u => u.UserId).Returns((string?)null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().CreateCommandeAsync(CreerCreateDto(), "cle-1", CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_SansCleDIdempotence_Leve400(string? cle)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().CreateCommandeAsync(CreerCreateDto(), cle, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Contains("Idempotency-Key", ex.Message);
    }

    [Fact]
    public async Task Create_CleDIdempotenceDePlusDe128Caracteres_Leve400()
    {
        var cleTropLongue = new string('a', 129);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().CreateCommandeAsync(CreerCreateDto(), cleTropLongue, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Contains("128", ex.Message);
    }

    // =====================================================================
    // UpdateCommandeAsync
    // =====================================================================

    [Fact]
    public async Task Update_SansUtilisateurIdentifie_Leve401()
    {
        _currentUser.SetupGet(u => u.UserId).Returns((string?)null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 1)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task Update_CommandeInexistante_Leve404()
    {
        _commandeRepo
            .Setup(r => r.GetCommandeByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commandes?)null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(404, CreerUpdateDto((1, 1)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Update_CommandeDUnAutreUtilisateur_Leve404SansRevelerSonExistence()
    {
        CommandeExiste(CreerCommande(proprietaire: "autre-user", statut: StatutCommande.Archivee, lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 1)), CancellationToken.None));

        // 404 (et non le message de statut) : le contrôle d'accès passe avant les règles métier.
        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
        Assert.Equal("Commande introuvable.", ex.Message);
    }

    [Theory]
    [InlineData(StatutCommande.Acquittee)]
    [InlineData(StatutCommande.Annulee)]
    [InlineData(StatutCommande.Archivee)]
    public async Task Update_CommandeNonEnCours_Leve400(StatutCommande statut)
    {
        CommandeExiste(CreerCommande(statut: statut, lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 1)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ProduitIntrouvable_Leve400EtJournaliseUnOperationLog()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8)); // le produit 99 demandé n'existe pas
        List<OperationLog>? logs = null;
        _operationLogRepo
            .Setup(r => r.AddRange(It.IsAny<List<OperationLog>>()))
            .Callback<List<OperationLog>>(l => logs = l);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((99, 1)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        var log = Assert.Single(logs!);
        Assert.Equal("Warning", log.Level);
        Assert.Contains("99", log.Message);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_CasNominal_RestitueLAncienStockPuisReserveLeNouveau()
    {
        // Commande : 2 x produit 1 (stock en base = 8, donc 10 avant la commande).
        var commande = CreerCommande(lignes: [(1, 2, 10m)]);
        var produit = CreerProduit(1, stock: 8, prix: 10m);
        CommandeExiste(commande);
        ProduitsExistent(produit);

        var resultat = await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        // 8 + 2 (restitution) - 3 (nouvelle réservation) = 7
        Assert.Equal(7, produit.Stock);
        var ligne = Assert.Single(resultat.LignesCommande);
        Assert.Equal(3, ligne.Quantite);
        Assert.Equal(30m, resultat.TotalCommandeTTC);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_CasNominal_ApplaiqueLaRowVersionDuClient()
    {
        var commande = CreerCommande(lignes: [(1, 2, 10m)]);
        CommandeExiste(commande);
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        _commandeRepo.Verify(r => r.SetOriginalRowVersion(
            commande,
            It.Is<byte[]>(b => b.SequenceEqual(RowVersionCommande))), Times.Once);
    }

    [Fact]
    public async Task Update_CasNominal_JournaliseMouvementDeStockEtAuditUpdate()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        var mouvement = Assert.Single(_mouvements);
        Assert.Equal(1, mouvement.ProduitId);
        Assert.Equal(TypeMouvementStock.Sortie, mouvement.TypeMouvement);
        Assert.Equal("ModificationCommande", mouvement.SourceOperation);

        var audit = Assert.Single(_audits);
        Assert.Equal(nameof(Commandes), audit.EntityName);
        Assert.Equal("100", audit.EntityId);
        Assert.Equal("UPDATE", audit.ActionType);
        Assert.Equal("user-1", audit.ChangedBy);
        Assert.False(string.IsNullOrWhiteSpace(audit.OldValue));
        Assert.False(string.IsNullOrWhiteSpace(audit.NewValue));
        Assert.Equal(audit.CorrelationId, mouvement.OperationId);
    }

    [Fact]
    public async Task Update_AncienneValeurAuditee_ContientLesAnciennesLignes()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        var audit = Assert.Single(_audits);
        using var ancien = JsonDocument.Parse(audit.OldValue!);
        using var nouveau = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal(2, ancien.RootElement.GetProperty("Lignes")[0].GetProperty("Quantite").GetInt32());
        Assert.Equal(3, nouveau.RootElement.GetProperty("Lignes")[0].GetProperty("Quantite").GetInt32());
    }

    [Fact]
    public async Task Update_StockInsuffisant_Leve400()
    {
        // Après restitution des 2 unités : 1 + 2 = 3 disponibles, on en demande 5.
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 1));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 5)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Contains("Stock insuffisant", ex.Message);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_ProduitIndisponible_Leve400()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8, disponible: false));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 1)), CancellationToken.None));

        Assert.Contains("n'est pas disponible", ex.Message);
    }

    [Fact]
    public async Task Update_QuantiteNulle_Leve400()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 0)), CancellationToken.None));

        Assert.Contains("supérieure à zéro", ex.Message);
    }

    [Fact]
    public async Task Update_ConflitDeConcurrence_Leve409EtGardeLeCache()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));
        _commandeRepo
            .Setup(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        _cache.Set("commande:100", "valeur en cache");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.True(_cache.TryGetValue("commande:100", out _));
    }

    [Fact]
    public async Task Update_Reussie_InvalideLesCachesCommandeEtProduit()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));
        _cache.Set("commande:100", "valeur en cache");
        _cache.Set(ProduitCacheKeys.Produit(1), "produit en cache");

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        Assert.False(_cache.TryGetValue("commande:100", out _));
        Assert.False(_cache.TryGetValue(ProduitCacheKeys.Produit(1), out _));
    }

    [Fact]
    public async Task Update_Admin_PeutModifierLaCommandeDUnAutreUtilisateur()
    {
        _currentUser.SetupGet(u => u.IsAdmin).Returns(true);
        CommandeExiste(CreerCommande(proprietaire: "autre-user", lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        var resultat = await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        Assert.Equal(100, resultat.CommandeId);
    }

    // =====================================================================
    // ArchiveCommandeByIdAsync
    // =====================================================================

    [Fact]
    public async Task Archive_CommandeInexistante_Leve404()
    {
        _commandeRepo
            .Setup(r => r.GetCommandeByIdAsync(404, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Commandes?)null);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ArchiveCommandeByIdAsync(404, RowVersionBase64, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Archive_CommandeDUnAutreUtilisateur_Leve404()
    {
        CommandeExiste(CreerCommande(proprietaire: "autre-user", lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Archive_CommandeDejaArchivee_Leve400()
    {
        CommandeExiste(CreerCommande(statut: StatutCommande.Archivee, lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Archive_SansRowVersion_Leve428(string? rowVersion)
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ArchiveCommandeByIdAsync(100, rowVersion, CancellationToken.None));

        Assert.Equal(StatusCodes.Status428PreconditionRequired, ex.StatusCode);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Archive_CasNominal_PasseLeStatutAArchiveeEtSauvegarde()
    {
        var commande = CreerCommande(lignes: [(1, 2, 10m)]);
        CommandeExiste(commande);

        await CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None);

        Assert.Equal(StatutCommande.Archivee, commande.Statut);
        _commandeRepo.Verify(r => r.SetOriginalRowVersion(
            commande,
            It.Is<byte[]>(b => b.SequenceEqual(RowVersionCommande))), Times.Once);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Archive_CasNominal_EcritUnAuditLogArchiveAvecAncienEtNouveauStatut()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));

        await CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None);

        var audit = Assert.Single(_audits);
        Assert.Equal("ARCHIVE", audit.ActionType);
        Assert.Equal(nameof(Commandes), audit.EntityName);
        Assert.Equal("100", audit.EntityId);
        Assert.Equal("alice", audit.ChangedBy);

        using var ancien = JsonDocument.Parse(audit.OldValue!);
        using var nouveau = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal("EnCours", ancien.RootElement.GetProperty("Statut").GetString());
        Assert.Equal("Archivee", nouveau.RootElement.GetProperty("Statut").GetString());
    }

    [Fact]
    public async Task Archive_SansNomUtilisateur_AuditSousLeNomAnonymous()
    {
        _currentUser.SetupGet(u => u.UserName).Returns((string?)null);
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));

        await CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None);

        Assert.Equal("Anonymous", Assert.Single(_audits).ChangedBy);
    }

    [Fact]
    public async Task Archive_ConflitDeConcurrence_Leve409()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        _commandeRepo
            .Setup(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task Archive_Reussie_InvalideLeCacheDeLaCommande()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        _cache.Set("commande:100", "valeur en cache");

        await CreerService().ArchiveCommandeByIdAsync(100, RowVersionBase64, CancellationToken.None);

        Assert.False(_cache.TryGetValue("commande:100", out _));
    }

    // =====================================================================
    // Corrections : RowVersion invalide, doublons, mouvements, cache de liste
    // =====================================================================

    [Fact]
    public async Task Update_RowVersionInvalide_Leve400SansModifierLeStock()
    {
        var produit = CreerProduit(1, stock: 8);
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(produit);
        var dto = new UpdateCommandeDto
        {
            RowVersion = "pas-du-base64!",
            LignesCommande = [new LignesCommandeDto { ProduitId = 1, Quantite = 3 }]
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, dto, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Equal(8, produit.Stock);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Archive_RowVersionInvalide_Leve400()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ArchiveCommandeByIdAsync(100, "pas-du-base64!", CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_MemeProduitSurPlusieursLignes_RegroupeLesQuantites()
    {
        var produit = CreerProduit(1, stock: 8, prix: 10m);
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(produit);

        var resultat = await CreerService().UpdateCommandeAsync(
            100, CreerUpdateDto((1, 2), (1, 3)), CancellationToken.None);

        var ligne = Assert.Single(resultat.LignesCommande);
        Assert.Equal(5, ligne.Quantite);
        Assert.Equal(50m, resultat.TotalCommandeTTC);
        Assert.Equal(5, produit.Stock); // 8 + 2 (restitution) - 5
    }

    [Fact]
    public async Task Update_Mouvement_ConsigneLeStockReelAvantEtApres()
    {
        // Stock en base : 8. La commande contenait 2 unités, on passe à 3 : le stock devient 7.
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        var mouvement = Assert.Single(_mouvements);
        Assert.Equal(8, mouvement.StockAvant);   // et non 10 (stock + quantité de la nouvelle ligne)
        Assert.Equal(7, mouvement.StockApres);
        Assert.Equal(1, mouvement.Quantite); // magnitude positive, le sens est porté par TypeMouvement
        Assert.Equal(TypeMouvementStock.Sortie, mouvement.TypeMouvement);
    }

    [Fact]
    public async Task Update_CasNominal_AppliqueLaVariationNetteParUnUpdateAtomique()
    {
        // 2 -> 3 unités : une seule variation nette de -1 (et non -3 puis +2).
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        var produit = CreerProduit(1, stock: 8);
        ProduitsExistent(produit);

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        _produitRepo.Verify(r => r.TryAppliquerVariationStockAsync(1, -1, It.IsAny<CancellationToken>()), Times.Once);
        _produitRepo.Verify(r => r.RechargerAsync(produit, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_QuantiteInchangee_NAppliqueAucunUpdateDeStock()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 2)), CancellationToken.None);

        _produitRepo.Verify(
            r => r.TryAppliquerVariationStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Update_PlusieursProduits_AppliqueLesVariationsParProduitIdCroissant()
    {
        // Ordre fixe = pas d'interblocage entre deux commandes qui se partagent les mêmes produits.
        CommandeExiste(CreerCommande(lignes: [(5, 1, 10m), (3, 1, 10m)]));
        ProduitsExistent(CreerProduit(5, stock: 10), CreerProduit(3, stock: 10));
        var ordre = new List<int>();
        _produitRepo
            .Setup(r => r.TryAppliquerVariationStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((produitId, variation, ct) => ordre.Add(produitId))
            .ReturnsAsync(true);

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((5, 2), (3, 2)), CancellationToken.None);

        Assert.Equal(new[] { 3, 5 }, ordre);
    }

    [Fact]
    public async Task Update_StockPrisParUneAutreCommande_Leve409EtNeSauvegardePas()
    {
        // Le contrôle en mémoire passe, mais l'UPDATE conditionnel en base refuse : le stock vient d'être pris.
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));
        _produitRepo
            .Setup(r => r.TryAppliquerVariationStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _cache.Set("commande:100", "valeur en cache");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
        Assert.Contains("Stock insuffisant", ex.Message);
        _commandeRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(_cache.TryGetValue("commande:100", out _));
    }

    [Fact]
    public async Task Update_Reussie_ValideLaTransaction()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None);

        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ConflitSurLaCommande_NeValidePasLaTransaction()
    {
        // Si la commande est refusée (RowVersion périmée), la transaction n'est pas validée :
        // le stock déjà modifié par les UPDATE atomiques est annulé avec elle.
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));
        _commandeRepo
            .Setup(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 3)), CancellationToken.None));

        _transaction.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_QuantiteReduite_JournaliseUneEntreeDeStock()
    {
        // 2 -> 1 : une unité retourne en stock (8 -> 9).
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 1)), CancellationToken.None);

        var mouvement = Assert.Single(_mouvements);
        Assert.Equal(TypeMouvementStock.Entree, mouvement.TypeMouvement);
        Assert.Equal(1, mouvement.Quantite);
        Assert.Equal(8, mouvement.StockAvant);
        Assert.Equal(9, mouvement.StockApres);
    }

    [Fact]
    public async Task Update_ProduitRetireDeLaCommande_RestitueEtJournaliseLeStock()
    {
        // Le produit 2 (4 unités) disparaît de la commande ; le produit 1 est inchangé (aucun mouvement).
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m), (2, 4, 5m)]));
        ProduitsExistent(CreerProduit(1, stock: 8), CreerProduit(2, stock: 20));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 2)), CancellationToken.None);

        var mouvement = Assert.Single(_mouvements);
        Assert.Equal(2, mouvement.ProduitId);
        Assert.Equal(TypeMouvementStock.Entree, mouvement.TypeMouvement);
        Assert.Equal(4, mouvement.Quantite);
        Assert.Equal(20, mouvement.StockAvant);
        Assert.Equal(24, mouvement.StockApres);
    }

    [Fact]
    public async Task Update_QuantiteInchangee_NeJournaliseAucunMouvement()
    {
        CommandeExiste(CreerCommande(lignes: [(1, 2, 10m)]));
        ProduitsExistent(CreerProduit(1, stock: 8));

        await CreerService().UpdateCommandeAsync(100, CreerUpdateDto((1, 2)), CancellationToken.None);

        Assert.Empty(_mouvements);
    }

    [Fact]
    public async Task GetAll_UtilisateurStandard_NeChargePasToutesLesCommandes()
    {
        _commandeRepo
            .Setup(r => r.GetByOwnerAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Commandes> { CreerCommande(1, "user-1") });

        await CreerService().GetAllCommandesAsync(CancellationToken.None);

        _commandeRepo.Verify(r => r.GetAllCommandesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAll_ReflecteToujoursLEtatActuelDeLaBase()
    {
        var liste = new List<Commandes> { CreerCommande(1, "user-1") };
        _commandeRepo
            .Setup(r => r.GetByOwnerAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => liste.ToList());
        var service = CreerService();

        var avant = (await service.GetAllCommandesAsync(CancellationToken.None)).ToList();
        liste.Add(CreerCommande(2, "user-1"));
        var apres = (await service.GetAllCommandesAsync(CancellationToken.None)).ToList();

        Assert.Single(avant);
        Assert.Equal(2, apres.Count);
    }
}
