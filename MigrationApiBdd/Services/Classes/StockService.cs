using Azure;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MigrationApiBdd.DAL;
using MigrationApiBdd.Dtos;
using Microsoft.Extensions.Caching.Memory;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Helpers;
using MigrationApiBdd.Models;
using MigrationApiBdd.Services.Auth;
using MigrationApiBdd.Services.Interfaces;
using System.Text.Json;

namespace MigrationApiBdd.Services.Classes
{
    public class StockService : IStockService
    {
        private readonly ILogger<StockService> _logger;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly IProduitRepository _produitRepository;
        private readonly IStockMouvementRepository _stockMouvementRepository;
        private readonly IMemoryCache _cache;

        public StockService(ILogger<StockService> logger, IAuditLogRepository auditLogRepository, ICurrentUserService currentUserService, IProduitRepository produitRepository, IStockMouvementRepository stockMouvementRepository, IMemoryCache cache)
        {
            _logger = logger;
            _auditLogRepository = auditLogRepository;
            _currentUserService = currentUserService;
            _produitRepository = produitRepository;
            _stockMouvementRepository = stockMouvementRepository;
            _cache = cache;
        }

        public async Task<ProduitDto> ReapprovisionnerStockAsync(int id, ReapprovisionnerStockDto reapprovisionnerStockDto, CancellationToken cancellationToken)
        {
            OperationProduit operation = OperationProduit.Reapprovisionnement;
            string correlationId = Guid.NewGuid().ToString("N");
            string currentUser = _currentUserService.UserName ?? "Admin";
            var stockMouvement = new StockMouvement();
            if (reapprovisionnerStockDto.Quantite <= 0)
            {
                throw new BusinessRuleException(
                    "La quantité de réapprovisionnement doit être supérieure à zéro.");
            }

            // Décodage avant toute modification de l'entité : une RowVersion invalide donne une 400.
            var rowVersionClient = RowVersionHelper.Decoder(reapprovisionnerStockDto.RowVersion);

            var produit = await _produitRepository.GetProduitByIdAsync(
                id,
                cancellationToken);

            if (produit is null)
            {
                throw new BusinessRuleException(
                    "Le produit à réapprovisionner est introuvable.");
            }

            var ancienProduit = new Produits
            {
                ProduitId = produit.ProduitId,
                NomProduit = produit.NomProduit,
                Description = produit.Description,
                PrixUnitaireTTC = produit.PrixUnitaireTTC,
                Stock = produit.Stock,
                RowVersion = produit.RowVersion
            };

            var stockAvant = produit.Stock;
            produit.Stock += reapprovisionnerStockDto.Quantite;


            _stockMouvementRepository.SetOriginalRowVersion( produit, rowVersionClient);

            stockMouvement = new StockMouvement
            {
                SourceOperation = operation.ToString(),
                ProduitId = produit.ProduitId,
                TypeMouvement = TypeMouvementStock.Entree,
                Quantite = reapprovisionnerStockDto.Quantite,
                StockAvant = stockAvant,
                StockApres = produit.Stock,
                Motif = reapprovisionnerStockDto.Motif ?? "Réapprovisionnement manuel.",
                CreatedAtUtc = DateTime.UtcNow
            };
            _stockMouvementRepository.Add(stockMouvement);
            await SetLogReportProduitAsync(ancienProduit, produit, operation, currentUser, correlationId, cancellationToken);

            try
            {
                await _stockMouvementRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "Le produit a été modifié par une autre opération. " +
                    "Rechargez les données avant de réessayer.",
                    StatusCodes.Status409Conflict);
            }

            ProduitCacheKeys.Invalider(_cache, produit.ProduitId);
            return produit.Adapt<ProduitDto>();
        }

        private Task SetLogReportProduitAsync(Produits ancienProduit, Produits produitTracke, OperationProduit operation, string currentUser, string correlationId, CancellationToken cancellationToken)
        {
            var auditLog = new AuditLog
            {
                EntityName = nameof(Produits),

                EntityId = produitTracke?.ProduitId > 0 ?
                          produitTracke.ProduitId.ToString() : ancienProduit?.ProduitId > 0 ?
                                                          ancienProduit.ProduitId.ToString() : correlationId,

                ActionType = operation switch
                {
                    OperationProduit.Creation => "INSERT",
                    OperationProduit.Modification => "UPDATE",
                    OperationProduit.Suppression => "DELETE",
                    OperationProduit.Reapprovisionnement => "REAPPROVISIONNEMENT",
                    _ => throw new BusinessRuleException("Action non prise en charge.")
                },

                OldValue = ancienProduit is null
                   ? null
                   : ConstruireValeurAuditProduit(ancienProduit),

                NewValue = produitTracke is null
                   ? null
                   : ConstruireValeurAuditProduit(produitTracke),

                ChangedBy = currentUser,
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                Reason = $"{operation} de produit"
            };

            _auditLogRepository.AddRange([auditLog]);
            return Task.CompletedTask;

        }

        private string ConstruireValeurAuditProduit(Produits ancienProduit)
        {
            var valeurAudit = new
            {
                ancienProduit.ProduitId,
                ancienProduit.NomProduit,
                ancienProduit.Description,
                ancienProduit.PrixUnitaireTTC,
                ancienProduit.RowVersion,
                ancienProduit.Stock
            };

            return JsonSerializer.Serialize(valeurAudit);
        }
    }
}
