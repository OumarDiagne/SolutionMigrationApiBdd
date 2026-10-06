using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
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

public class StockServiceTests : IDisposable
{
    private static readonly byte[] RowVersionProduit = [1, 2, 3, 4, 5, 6, 7, 8];

    private readonly Mock<IAuditLogRepository> _auditRepo = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();
    private readonly Mock<IProduitRepository> _produitRepo = new();
    private readonly Mock<IStockMouvementRepository> _stockRepo = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly List<AuditLog> _auditsCaptures = [];
    private readonly List<StockMouvement> _mouvementsCaptures = [];

    public StockServiceTests()
    {
        // Mapster : la conversion byte[] <-> string est configurée dans MapsterConfig.
        TestMapster.Init();

        _currentUser.SetupGet(u => u.UserName).Returns("oumar");

        _auditRepo
            .Setup(r => r.AddRange(It.IsAny<List<AuditLog>>()))
            .Callback<List<AuditLog>>(logs => _auditsCaptures.AddRange(logs));

        _stockRepo
            .Setup(r => r.Add(It.IsAny<StockMouvement>()))
            .Callback<StockMouvement>(m => _mouvementsCaptures.Add(m));
    }

    public void Dispose() => _cache.Dispose();

    private StockService CreerService() => new(
        NullLogger<StockService>.Instance,
        _auditRepo.Object,
        _currentUser.Object,
        _produitRepo.Object,
        _stockRepo.Object,
        _cache);

    private static Produits CreerProduit(int stock = 10) => new()
    {
        ProduitId = 7,
        NomProduit = "Clavier",
        Description = "Clavier mécanique",
        PrixUnitaireTTC = 59.90m,
        Stock = stock,
        RowVersion = RowVersionProduit
    };

    private static ReapprovisionnerStockDto CreerDto(int quantite = 5, string? motif = null) => new()
    {
        Quantite = quantite,
        RowVersion = Convert.ToBase64String(RowVersionProduit),
        Motif = motif
    };

    private void ProduitExiste(Produits produit) =>
        _produitRepo
            .Setup(r => r.GetProduitByIdAsync(produit.ProduitId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(produit);

    // ---------- Validation ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Reapprovisionner_QuantiteNonPositive_Leve400SansToucherALaBase(int quantite)
    {
        var service = CreerService();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ReapprovisionnerStockAsync(7, CreerDto(quantite), CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        _produitRepo.Verify(r => r.GetProduitByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _stockRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reapprovisionner_ProduitIntrouvable_LeveBusinessRuleException()
    {
        _produitRepo
            .Setup(r => r.GetProduitByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Produits?)null);
        var service = CreerService();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ReapprovisionnerStockAsync(99, CreerDto(), CancellationToken.None));

        Assert.Contains("introuvable", ex.Message);
        _stockRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------- Cas nominal ----------

    [Fact]
    public async Task Reapprovisionner_CasNominal_AugmenteLeStockEtRetourneLeProduitMisAJour()
    {
        ProduitExiste(CreerProduit(stock: 10));
        var service = CreerService();

        var resultat = await service.ReapprovisionnerStockAsync(7, CreerDto(quantite: 5), CancellationToken.None);

        Assert.Equal(15, resultat.Stock);
        Assert.Equal(7, resultat.ProduitId);
        _stockRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reapprovisionner_VerrouilleLaConcurrenceAvecLaRowVersionDuClient()
    {
        var produit = CreerProduit();
        ProduitExiste(produit);
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(), CancellationToken.None);

        _stockRepo.Verify(r => r.SetOriginalRowVersion(
            produit,
            It.Is<byte[]>(b => b.SequenceEqual(RowVersionProduit))), Times.Once);
    }

    // ---------- Journalisation : StockMouvement ----------

    [Fact]
    public async Task Reapprovisionner_EnregistreUnMouvementDeStockEntree()
    {
        ProduitExiste(CreerProduit(stock: 10));
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(quantite: 5), CancellationToken.None);

        var mouvement = Assert.Single(_mouvementsCaptures);
        Assert.Equal(7, mouvement.ProduitId);
        Assert.Equal(TypeMouvementStock.Entree, mouvement.TypeMouvement);
        Assert.Equal(5, mouvement.Quantite);
        Assert.Equal(10, mouvement.StockAvant);
        Assert.Equal(15, mouvement.StockApres);
        Assert.Equal(nameof(OperationProduit.Reapprovisionnement), mouvement.SourceOperation);
    }

    [Fact]
    public async Task Reapprovisionner_SansMotif_UtiliseLeMotifParDefaut()
    {
        ProduitExiste(CreerProduit());
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(motif: null), CancellationToken.None);

        Assert.Equal("Réapprovisionnement manuel.", Assert.Single(_mouvementsCaptures).Motif);
    }

    [Fact]
    public async Task Reapprovisionner_AvecMotif_ConserveLeMotifFourni()
    {
        ProduitExiste(CreerProduit());
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(motif: "Livraison fournisseur"), CancellationToken.None);

        Assert.Equal("Livraison fournisseur", Assert.Single(_mouvementsCaptures).Motif);
    }

    // ---------- Journalisation : AuditLog ----------

    [Fact]
    public async Task Reapprovisionner_EcritUnAuditLogAvecAncienEtNouveauStock()
    {
        ProduitExiste(CreerProduit(stock: 10));
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(quantite: 5), CancellationToken.None);

        var audit = Assert.Single(_auditsCaptures);
        Assert.Equal(nameof(Produits), audit.EntityName);
        Assert.Equal("7", audit.EntityId);
        Assert.Equal("REAPPROVISIONNEMENT", audit.ActionType);
        Assert.Equal("oumar", audit.ChangedBy);
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));

        using var ancien = JsonDocument.Parse(audit.OldValue!);
        using var nouveau = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal(10, ancien.RootElement.GetProperty("Stock").GetInt32());
        Assert.Equal(15, nouveau.RootElement.GetProperty("Stock").GetInt32());
    }

    [Fact]
    public async Task Reapprovisionner_SansUtilisateurConnecte_AuditSousLeNomAdmin()
    {
        _currentUser.SetupGet(u => u.UserName).Returns((string?)null);
        ProduitExiste(CreerProduit());
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(), CancellationToken.None);

        Assert.Equal("Admin", Assert.Single(_auditsCaptures).ChangedBy);
    }

    // ---------- Concurrence ----------

    [Fact]
    public async Task Reapprovisionner_ConflitDeConcurrence_Leve409()
    {
        ProduitExiste(CreerProduit());
        _stockRepo
            .Setup(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        var service = CreerService();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ReapprovisionnerStockAsync(7, CreerDto(), CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, ex.StatusCode);
    }

    // ---------- Cache ----------

    [Fact]
    public async Task Reapprovisionner_InvalideLeCacheDuProduitEtDesListes()
    {
        ProduitExiste(CreerProduit());
        _cache.Set(ProduitCacheKeys.Produit(7), "ancienne valeur");
        var cleListeAvant = ProduitCacheKeys.Liste(_cache, null);
        var service = CreerService();

        await service.ReapprovisionnerStockAsync(7, CreerDto(), CancellationToken.None);

        Assert.False(_cache.TryGetValue(ProduitCacheKeys.Produit(7), out _));
        Assert.NotEqual(cleListeAvant, ProduitCacheKeys.Liste(_cache, null));
    }

    [Fact]
    public async Task Reapprovisionner_ConflitDeConcurrence_NInvalidePasLeCache()
    {
        ProduitExiste(CreerProduit());
        _stockRepo
            .Setup(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());
        _cache.Set(ProduitCacheKeys.Produit(7), "valeur en cache");
        var service = CreerService();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            service.ReapprovisionnerStockAsync(7, CreerDto(), CancellationToken.None));

        Assert.True(_cache.TryGetValue(ProduitCacheKeys.Produit(7), out _));
    }

    // ---------- RowVersion invalide ----------

    [Theory]
    [InlineData("pas-du-base64!")]
    [InlineData("abc")]
    public async Task Reapprovisionner_RowVersionInvalide_Leve400SansModifierLeStock(string rowVersion)
    {
        var produit = CreerProduit(stock: 10);
        ProduitExiste(produit);
        var dto = new ReapprovisionnerStockDto { Quantite = 5, RowVersion = rowVersion };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ReapprovisionnerStockAsync(7, dto, CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Equal(10, produit.Stock);
        _stockRepo.Verify(r => r.SaveChangeAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reapprovisionner_RowVersionAbsente_Leve428()
    {
        ProduitExiste(CreerProduit());
        var dto = new ReapprovisionnerStockDto { Quantite = 5, RowVersion = "" };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            CreerService().ReapprovisionnerStockAsync(7, dto, CancellationToken.None));

        Assert.Equal(StatusCodes.Status428PreconditionRequired, ex.StatusCode);
    }
}
