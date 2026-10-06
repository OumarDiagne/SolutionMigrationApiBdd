using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MigrationApiBdd.DAL;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Helpers;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;
using MigrationApiBdd.Services.Auth;
using MigrationApiBdd.Services.Interfaces;
using System.Text.Json;

namespace MigrationApiBdd.Services.Classes
{
    public class ProduitService : IProduitService
    {
        private const string ScopeCreation = "POST:/api/produit";

        private readonly IProduitRepository _produitRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IStockMouvementRepository _stockMouvementRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly MigApiContext _dbContext;
        private readonly IMemoryCache _cache;

        public ProduitService(IProduitRepository produitRepository,
                              IAuditLogRepository auditLogRepository,
                              IStockMouvementRepository stockMouvementRepository,
                              ICurrentUserService currentUserService,
                              MigApiContext dbContext,
                              IMemoryCache cache)
        {
            _produitRepository = produitRepository;
            _auditLogRepository = auditLogRepository;
            _stockMouvementRepository = stockMouvementRepository;
            _currentUserService = currentUserService;
            _dbContext = dbContext;
            _cache = cache;
        }

        #region Méthodes privées

        /// <summary>Les écritures sur le catalogue sont réservées aux administrateurs.</summary>
        private void EnsureIsAdmin()
        {
            if (!_currentUserService.IsAuthenticated)
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.",
                    StatusCodes.Status401Unauthorized);
            }

            if (!_currentUserService.IsAdmin)
            {
                throw new BusinessRuleException(
                    "Seul un administrateur peut modifier le catalogue produits.",
                    StatusCodes.Status403Forbidden);
            }
        }

        private string ObtenirUtilisateurCourant()
        {
            string? currentUser = _currentUserService.UserName ?? _currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(currentUser))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.",
                    StatusCodes.Status401Unauthorized);
            }

            return currentUser;
        }

        private static string NormaliserCleIdempotence(string? idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new BusinessRuleException("Le header Idempotency-Key est obligatoire.");
            }

            idempotencyKey = idempotencyKey.Trim();

            if (idempotencyKey.Length > 128)
            {
                throw new BusinessRuleException(
                    "Le header Idempotency-Key ne doit pas dépasser 128 caractères.");
            }

            return idempotencyKey;
        }

        /// <summary>
        /// Même clé + même contenu + traitement terminé : on rejoue la réponse enregistrée.
        /// Sinon : contenu différent (422) ou requête encore en cours (409).
        /// </summary>
        private static ProduitDto RejouerReponseIdempotente(IdempotencyRecord existingRecord, string requestHash)
        {
            if (existingRecord.RequestHash != requestHash)
            {
                throw new BusinessRuleException(
                    "Cette clé d'idempotence a déjà été utilisée avec un contenu différent.",
                    StatusCodes.Status422UnprocessableEntity);
            }

            if (existingRecord.Status == "Completed" &&
                !string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
            {
                return JsonSerializer.Deserialize<ProduitDto>(existingRecord.ResponseBody)!;
            }

            throw new BusinessRuleException(
                "Une requête avec cette clé d'idempotence est déjà en cours de traitement.",
                StatusCodes.Status409Conflict);
        }

        private static void ValiderDonneesProduit(string? nomProduit, string? description, decimal prixUnitaireTTC, int stock)
        {
            if (string.IsNullOrWhiteSpace(nomProduit))
                throw new BusinessRuleException("Le nom du produit est obligatoire.");

            if (nomProduit.Trim().Length > 50)
                throw new BusinessRuleException("Le nom du produit ne doit pas dépasser 50 caractères.");

            if (description?.Length > 200)
                throw new BusinessRuleException("La description ne doit pas dépasser 200 caractères.");

            if (prixUnitaireTTC <= 0)
                throw new BusinessRuleException("Le prix unitaire TTC doit être supérieur à zéro.");

            if (stock < 0)
                throw new BusinessRuleException("Le stock ne peut pas être négatif.");
        }

        /// <summary>
        /// Convertit la RowVersion reçue (corps ou header If-Match) en byte[].
        /// Accepte la forme ETag entre guillemets : "AAAAAAAAB9E=".
        /// </summary>
        private static byte[] ConvertirRowVersion(string? rowVersion)
        {
            if (string.IsNullOrWhiteSpace(rowVersion))
            {
                throw new BusinessRuleException(
                    "La version de ligne (RowVersion) est requise.",
                    StatusCodes.Status428PreconditionRequired);
            }

            try
            {
                return Convert.FromBase64String(rowVersion.Trim().Trim('"'));
            }
            catch (FormatException)
            {
                throw new BusinessRuleException("La version de ligne (RowVersion) est invalide.");
            }
        }

        /// <summary>
        /// Contrôle anticipé : si la version connue du client n'est plus la version en base,
        /// inutile d'aller plus loin. EF Core refait le contrôle au SaveChanges (fenêtre de course).
        /// </summary>
        private static void VerifierVersionProduit(Produits produit, byte[] rowVersionClient)
        {
            if (!produit.RowVersion.SequenceEqual(rowVersionClient))
            {
                throw new BusinessRuleException(
                    "Le produit a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.",
                    StatusCodes.Status409Conflict);
            }
        }

        /// <summary>Snapshot indépendant : aucune référence commune avec l'entité suivie par EF Core.</summary>
        private static Produits CopierProduit(Produits produit)
        {
            return new Produits
            {
                ProduitId = produit.ProduitId,
                NomProduit = produit.NomProduit,
                Description = produit.Description,
                PrixUnitaireTTC = produit.PrixUnitaireTTC,
                Stock = produit.Stock,
                EstDisponible = produit.EstDisponible,
                RowVersion = produit.RowVersion.ToArray()
            };
        }

        private static string ConstruireValeurAuditProduit(Produits produit)
        {
            var valeurAudit = new
            {
                produit.ProduitId,
                produit.NomProduit,
                produit.Description,
                produit.PrixUnitaireTTC,
                produit.Stock,
                produit.EstDisponible,
                produit.RowVersion
            };

            return JsonSerializer.Serialize(valeurAudit);
        }

        private void AjouterAuditLog(Produits? ancienProduit, Produits produit, string actionType, OperationProduit operation, string currentUser, string correlationId)
        {
            var auditLog = new AuditLog
            {
                EntityName = nameof(Produits),
                EntityId = produit.ProduitId.ToString(),
                ActionType = actionType,
                OldValue = ancienProduit is null ? null : ConstruireValeurAuditProduit(ancienProduit),
                NewValue = ConstruireValeurAuditProduit(produit),
                ChangedBy = currentUser,
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                Reason = $"{operation} de produit"
            };

            _auditLogRepository.AddRange([auditLog]);
        }

        /// <summary>Toute variation de stock laisse une trace dans StockMouvements.</summary>
        private void AjouterMouvementStock(Produits produit, int stockAvant, int stockApres, TypeMouvementStock typeMouvement, OperationProduit operation, string motif, string correlationId)
        {
            if (stockAvant == stockApres)
            {
                return;
            }

            _stockMouvementRepository.Add(new StockMouvement
            {
                ProduitId = produit.ProduitId,
                Produit = produit,
                TypeMouvement = typeMouvement,
                Quantite = Math.Abs(stockApres - stockAvant),
                StockAvant = stockAvant,
                StockApres = stockApres,
                SourceOperation = $"{operation}Produit",
                Motif = motif,
                OperationId = correlationId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        #endregion

        #region Get All Produits
        public async Task<ICollection<ProduitDto>> GetAllProduitsAsync(string? nomProduit, CancellationToken cancellationToken)
        {
            string cacheKey = ProduitCacheKeys.Liste(_cache, nomProduit);

            var produits = await _cache.GetOrCreateAsync(cacheKey, async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = ProduitCacheKeys.Duree;

                List<Produits> entites = await _produitRepository.GetAllProduitsAsync(nomProduit?.Trim(), cancellationToken);
                return entites.Adapt<List<ProduitDto>>();
            });

            return produits ?? [];
        }
        #endregion

        #region Get Produit by Id
        public async Task<ProduitDto?> GetProduitByIdAsync(int id, CancellationToken cancellationToken)
        {
            string cacheKey = ProduitCacheKeys.Produit(id);

            if (_cache.TryGetValue(cacheKey, out ProduitDto? produitEnCache))
            {
                return produitEnCache;
            }

            Produits? produit = await _produitRepository.GetProduitByIdAsync(id, cancellationToken);
            if (produit is null)
            {
                return null;
            }

            ProduitDto produitDto = produit.Adapt<ProduitDto>();
            _cache.Set(cacheKey, produitDto, ProduitCacheKeys.Duree);

            return produitDto;
        }
        #endregion

        #region Création Produit
        public async Task<ProduitDto> CreateProduitAsync(CreateProduitDto createProduitDto, string? idempotencyKey, CancellationToken cancellationToken)
        {
            const OperationProduit operation = OperationProduit.Creation;

            EnsureIsAdmin();
            string currentUser = ObtenirUtilisateurCourant();
            idempotencyKey = NormaliserCleIdempotence(idempotencyKey);
            ValiderDonneesProduit(createProduitDto.NomProduit, createProduitDto.Description,
                                  createProduitDto.PrixUnitaireTTC, createProduitDto.Stock);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                string requestHash = RequestHashHelper.Compute(createProduitDto);

                var existingRecord = await _dbContext.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Scope == ScopeCreation && x.IdempotencyKey == idempotencyKey, cancellationToken);

                if (existingRecord is not null)
                {
                    // Pas de commit : la transaction (lecture seule ici) est annulée au Dispose.
                    return RejouerReponseIdempotente(existingRecord, requestHash);
                }

                var now = DateTime.UtcNow;
                var record = new IdempotencyRecord
                {
                    Scope = ScopeCreation,
                    IdempotencyKey = idempotencyKey,
                    RequestHash = requestHash,
                    Status = "Processing",
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddHours(24)
                };
                _dbContext.IdempotencyRecords.Add(record);

                try
                {
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Index unique (Scope, IdempotencyKey) : une requête concurrente avec la même clé vient d'être enregistrée.
                    throw new BusinessRuleException(
                        "Une requête avec cette clé d'idempotence est déjà en cours de traitement.",
                        StatusCodes.Status409Conflict);
                }

                string correlationId = Guid.NewGuid().ToString("N");

                Produits produitACreer = createProduitDto.Adapt<Produits>();
                produitACreer.NomProduit = produitACreer.NomProduit.Trim();

                // 1er SaveChanges : le ProduitId et la RowVersion sont générés par SQL Server.
                Produits createdProduit = await _produitRepository.CreateProduitAsync(produitACreer, cancellationToken);

                // Traces avec le vrai ProduitId (et non le correlationId comme EntityId).
                AjouterMouvementStock(createdProduit, 0, createdProduit.Stock, TypeMouvementStock.Entree,
                                      operation, "Stock initial à la création du produit.", correlationId);
                AjouterAuditLog(null, createdProduit, "INSERT", operation, currentUser, correlationId);
                await _dbContext.SaveChangesAsync(cancellationToken);

                ProduitDto produitDto = createdProduit.Adapt<ProduitDto>();

                record.Status = "Completed";
                record.ResponseStatusCode = StatusCodes.Status201Created;
                record.ResponseBody = JsonSerializer.Serialize(produitDto);
                record.CompletedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                ProduitCacheKeys.Invalider(_cache, createdProduit.ProduitId);
                return produitDto;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        #endregion

        #region Update Produit
        public async Task<ProduitDto> UpdateProduitAsync(int id, UpdateProduitDto updateProduitDto, CancellationToken cancellationToken)
        {
            const OperationProduit operation = OperationProduit.Modification;

            EnsureIsAdmin();
            string currentUser = ObtenirUtilisateurCourant();
            string correlationId = Guid.NewGuid().ToString("N");

            ValiderDonneesProduit(updateProduitDto.NomProduit, updateProduitDto.Description,
                                  updateProduitDto.PrixUnitaireTTC, updateProduitDto.Stock);
            byte[] rowVersion = ConvertirRowVersion(updateProduitDto.RowVersion);

            Produits produitTracke = await _produitRepository.GetProduitByIdAsync(id, cancellationToken)
                ?? throw new BusinessRuleException(
                    "Le produit à modifier n'a pas été trouvé.",
                    StatusCodes.Status404NotFound);

            VerifierVersionProduit(produitTracke, rowVersion);

            Produits ancienProduit = CopierProduit(produitTracke);

            produitTracke.NomProduit = updateProduitDto.NomProduit.Trim();
            produitTracke.Description = updateProduitDto.Description;
            produitTracke.PrixUnitaireTTC = updateProduitDto.PrixUnitaireTTC;
            produitTracke.EstDisponible = updateProduitDto.EstDisponible;
            produitTracke.Stock = updateProduitDto.Stock;

            // Un changement de stock via PUT est un ajustement manuel : il est tracé comme tel.
            AjouterMouvementStock(produitTracke, ancienProduit.Stock, produitTracke.Stock, TypeMouvementStock.Ajustement,
                                  operation, "Ajustement manuel du stock lors de la modification du produit.", correlationId);

            _produitRepository.SetOriginalRowVersion(produitTracke, rowVersion);
            AjouterAuditLog(ancienProduit, produitTracke, "UPDATE", operation, currentUser, correlationId);

            try
            {
                await _produitRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "Le produit a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.",
                    StatusCodes.Status409Conflict);
            }

            ProduitCacheKeys.Invalider(_cache, produitTracke.ProduitId);
            return produitTracke.Adapt<ProduitDto>();
        }
        #endregion

        #region Archive Produit
        public async Task ArchiveProduitByIdAsync(int id, string? rowVersion, CancellationToken cancellationToken)
        {
            const OperationProduit operation = OperationProduit.Suppression;

            EnsureIsAdmin();
            string currentUser = ObtenirUtilisateurCourant();
            string correlationId = Guid.NewGuid().ToString("N");
            byte[] rowVersionClient = ConvertirRowVersion(rowVersion);

            Produits produitTracke = await _produitRepository.GetProduitByIdAsync(id, cancellationToken)
                ?? throw new BusinessRuleException(
                    "Le produit à archiver n'a pas été trouvé.",
                    StatusCodes.Status404NotFound);

            if (!produitTracke.EstDisponible)
            {
                throw new BusinessRuleException("Le produit est déjà archivé.");
            }

            VerifierVersionProduit(produitTracke, rowVersionClient);

            Produits produitAvantArchivage = CopierProduit(produitTracke);

            // Archivage logique : le produit reste référencé par les lignes de commande existantes.
            produitTracke.EstDisponible = false;

            _produitRepository.SetOriginalRowVersion(produitTracke, rowVersionClient);
            AjouterAuditLog(produitAvantArchivage, produitTracke, "ARCHIVE", operation, currentUser, correlationId);

            try
            {
                await _produitRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "Le produit a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.",
                    StatusCodes.Status409Conflict);
            }

            ProduitCacheKeys.Invalider(_cache, produitTracke.ProduitId);
        }
        #endregion
    }
}
