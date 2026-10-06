using FluentValidation;
using Mapster;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MigrationApiBdd.DAL;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Helpers;
using MigrationApiBdd.Migrations;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;
using MigrationApiBdd.Services.Auth;
using System.ComponentModel.Design;
using System.Text.Json;
using OperationLog = MigrationApiBdd.Models.OperationLog;

namespace MigrationApiBdd.Services.Classes
{
    public class CommandeService : Interfaces.ICommandeService
    {
        private readonly ICommandeRepository _commandeRepository;
        private readonly IClientRepository _clientRepository;
        private readonly IProduitRepository _produitRepository;
        private readonly IOperationLogRepository _operationLogRepository;
        private readonly IValidator<CreateCommandeDto> _createCommandeValidator;
        private readonly IStockMouvementRepository _stockMouvementRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly ICurrentUserService _currentUserService;
        private readonly MigApiContext _dbContext;
        private readonly IMemoryCache _cache;

        public CommandeService(ICommandeRepository commandeRepository,
                               IClientRepository clientRepository,
                               IProduitRepository produitRepository,
                               IOperationLogRepository operationLogRepository,
                               IValidator<CreateCommandeDto> createCommandeValidator,
                               IStockMouvementRepository stockMouvementRepository,
                               IAuditLogRepository auditLogRepository,
                               MigApiContext dbContext,
                               ICurrentUserService currentUserService,
                               IMemoryCache cache)
        {
            this._commandeRepository = commandeRepository;
            this._operationLogRepository = operationLogRepository;
            this._produitRepository = produitRepository;
            this._clientRepository = clientRepository;
            this._stockMouvementRepository = stockMouvementRepository;
            this._createCommandeValidator = createCommandeValidator;
            this._auditLogRepository = auditLogRepository;
            this._currentUserService = currentUserService;
            this._dbContext = dbContext;
            this._cache = cache;
        }

 

        #region Methode privées
        private async Task SetLogReportCommandAsync(Dictionary<int, Produits> produitsParId, Commandes? ancienneCommande, Commandes commande, OperationCommande operation, string currentUser, string correlationId, CancellationToken cancellationToken, IReadOnlyDictionary<int, int>? stocksAvant = null)
        {
            List<AuditLog> auditLogs = new List<AuditLog>();
            List<OperationLog> operationLogs = new List<OperationLog>();
            string oldValue = string.Empty;
            List<StockMouvement> stockMouvements = (operation == OperationCommande.Modification && stocksAvant is not null)
                ? ConstruireMouvementsModification(produitsParId, stocksAvant, commande, correlationId)
                : commande.LignesCommande.Select(ligne => new StockMouvement
            {
                ProduitId = ligne.ProduitId,
                OperationId = correlationId,
                SourceOperation = $"{operation}Commande",
                TypeMouvement = TypeMouvementStock.Sortie,
                Motif = $"Sortie de stock pour la commande du client {commande.ClientId}.",
                Quantite = ligne.Quantite,
                StockAvant = produitsParId[ligne.ProduitId].Stock + ligne.Quantite,
                StockApres = produitsParId[ligne.ProduitId].Stock,
                CreatedAtUtc = DateTime.UtcNow,
            }).ToList();

            _stockMouvementRepository.AddRange(stockMouvements);

            if (operation == OperationCommande.Modification)
            {
                if (ancienneCommande != null)
                    oldValue = ConstruireValeurAuditCommande(ancienneCommande);
            }

            AuditLog auditLog = new AuditLog
            {
                EntityName = nameof(Commandes),
                EntityId = operation == OperationCommande.Modification ? commande.CommandeId.ToString() : correlationId,
                ActionType = operation == OperationCommande.Modification ? "UPDATE" : "INSERT",
                OldValue = operation == OperationCommande.Modification ? oldValue : null,
                NewValue = ConstruireValeurAuditCommande(commande),
                ChangedBy = currentUser,
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                Reason = $"{operation} de commande"
            };
            auditLogs.Add(auditLog);
            _auditLogRepository.AddRange(auditLogs);
        }
       
        private async Task SetLogArchiveReportCommandAsync(Commandes commandeAvantDesactivation, Commandes commandeTracke, OperationCommande operation, string currentUser, string correlationId, CancellationToken cancellationToken)
        {
            List<AuditLog> auditLogs = new List<AuditLog>();
            string oldValue = ConstruireValeurAuditCommande(commandeAvantDesactivation);
            AuditLog auditLog = new AuditLog
            {
                EntityName = nameof(Commandes),
                EntityId = commandeTracke.CommandeId.ToString(),
                ActionType = "ARCHIVE",
                OldValue = oldValue,
                NewValue = ConstruireValeurAuditCommande(commandeTracke),
                ChangedBy = currentUser,
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                Reason = $"{operation} de commande"
            };
            auditLogs.Add(auditLog);
            _auditLogRepository.AddRange(auditLogs);
        }

        /// <summary>
        /// Mouvements d'une modification de commande : un mouvement par produit dont le stock a réellement
        /// changé (variation nette), y compris les produits retirés de la commande. StockAvant / StockApres
        /// reflètent le stock réel en base, avant la restitution des anciennes lignes.
        /// </summary>
        private static List<StockMouvement> ConstruireMouvementsModification(
            IReadOnlyDictionary<int, Produits> produitsParId,
            IReadOnlyDictionary<int, int> stocksAvant,
            Commandes commande,
            string correlationId)
        {
            return produitsParId.Values
                .Where(produit => stocksAvant.TryGetValue(produit.ProduitId, out var stockInitial) && produit.Stock != stockInitial)
                .Select(produit =>
                {
                    int stockInitial = stocksAvant[produit.ProduitId];
                    int variation = produit.Stock - stockInitial;

                    return new StockMouvement
                    {
                        ProduitId = produit.ProduitId,
                        OperationId = correlationId,
                        SourceOperation = $"{OperationCommande.Modification}Commande",
                        TypeMouvement = variation < 0 ? TypeMouvementStock.Sortie : TypeMouvementStock.Entree,
                        Motif = $"Ajustement du stock suite à la modification de la commande {commande.CommandeId}.",
                        Quantite = Math.Abs(variation),
                        StockAvant = stockInitial,
                        StockApres = produit.Stock,
                        CreatedAtUtc = DateTime.UtcNow
                    };
                })
                .ToList();
        }

        private static void ResetLignesDeCommande(Commandes commande, Dictionary<int, Produits> produitsParId)
        {
            if (commande.LignesCommande != null && commande.LignesCommande.Count > 0)
            {
                foreach (var ligne in commande.LignesCommande)
                {
                    var produit = produitsParId[ligne.ProduitId];
                    produit.Stock += ligne.Quantite;
                }
                commande.LignesCommande.Clear();
            }
        }

        private async Task<Dictionary<int, Produits>> GetProduitsParIdsAsync(string correlationId,
            List<int> produitIds,
            OperationCommande operation,
            CancellationToken cancellationToken)
        {
            var operationLogs = new List<OperationLog>();

            ICollection<Produits> produits = await _produitRepository.GetAllProduitsByIdsAsync(produitIds, cancellationToken);

            var produisParId = produits.ToDictionary(p => p.ProduitId, p => p);

            var produitsManquants = produitIds
                .Where(id => !produisParId.ContainsKey(id))
                .ToList();


            if (produitsManquants.Count == 0)
            {
                return produisParId;
            }
            operationLogs.Add(new OperationLog
            {
                OperationName = $"{operation}Commande",
                Level = "Warning",
                Message = $"{operation} de commande refusée : produits introuvables [{string.Join(", ", produitsManquants)}].",
                Exception = null,
                ExecutedAtUtc = DateTime.UtcNow,
                DurationMs = 0,
                CorrelationId = correlationId
            });
            _operationLogRepository.AddRange(
            operationLogs);

            throw new BusinessRuleException(
                $"{operation} de commande refusée : un ou plusieurs produits demandés n'existent pas.");
        }

        private static ResultatPreparationCommande PreparerCommande(IReadOnlyDictionary<int, Produits> produitsParId, Dictionary<int, int> produitsIdsDico)
        {
            var lignesFinalisees = new List<LignesCommande>();
            decimal totalCommandeTTC = 0m;

            foreach (var identifiantProduit in produitsIdsDico.Keys)
            {
                var produit = ObtenirProduit(identifiantProduit, produitsParId);

                ValiderDisponibilite(produit, produitsIdsDico[identifiantProduit]);

                var ligneFinalisee = ConstruireLigneCommande(produit, produitsIdsDico[identifiantProduit]);

                ReserverStock(produit, produitsIdsDico[identifiantProduit]);

                lignesFinalisees.Add(ligneFinalisee);
                totalCommandeTTC += ligneFinalisee.PrixUnitaireTTC * ligneFinalisee.Quantite;
            }

            return new ResultatPreparationCommande(lignesFinalisees, totalCommandeTTC);
        }

        private static Produits ObtenirProduit(
            int produitId,
            IReadOnlyDictionary<int, Produits> produitsParId)
        {
            if (!produitsParId.TryGetValue(produitId, out var produit))
            {
                throw new BusinessRuleException(
                    $"Le produit avec l'identifiant {produitId} est introuvable.");
            }

            return produit;
        }

        private static void ValiderDisponibilite(Produits produit, int quantiteDemandee)
        {
            if (!produit.EstDisponible)
            {
                throw new BusinessRuleException(
                    $"Le produit {produit.NomProduit} n'est pas disponible.");
            }

            if (quantiteDemandee <= 0)
            {
                throw new BusinessRuleException(
                    "La quantité commandée doit être supérieure à zéro.");
            }

            if (produit.Stock < quantiteDemandee)
            {
                throw new BusinessRuleException(
                    $"Stock insuffisant pour le produit : {produit.NomProduit}. " +
                    $"Demandé : {quantiteDemandee}, disponible : {produit.Stock}.");
            }
        }

        private static LignesCommande ConstruireLigneCommande(  Produits produit, int quantite)
        {
            return new LignesCommande
            {
                ProduitId = produit.ProduitId,
                Quantite = quantite,
                PrixUnitaireTTC = produit.PrixUnitaireTTC
            };
        }

        private static void ReserverStock(Produits produit, int quantite)
        {
            produit.Stock -= quantite;
        }

        private static string ConstruireValeurAuditCommande(Commandes commande)
        {
            var valeurAudit = new
            {
                commande.CommandeId,
                commande.ClientId,
                Statut = commande.Statut.ToString(),
                commande.TotalCommandeTTC,
                commande.DateCommande,
                commande.FacturePath,
                commande.RowVersion,
                Lignes = commande.LignesCommande
                    .OrderBy(ligne => ligne.ProduitId)
                    .Select(ligne => new
                    {
                        ligne.ProduitId,
                        ligne.Quantite,
                        ligne.PrixUnitaireTTC
                    })
            };

            return JsonSerializer.Serialize(valeurAudit);
        }

        private const string MessageCommandeIntrouvable = "Commande introuvable.";

        /// <summary>
        /// Admin : accès à tout. Utilisateur : uniquement ses propres commandes.
        /// </summary>
        private bool PeutAccederCommande(string? createdByUserId)
        {
            if (_currentUserService.IsAdmin)
            {
                return true;
            }

            var userId = _currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.", StatusCodes.Status401Unauthorized);
            }

            return createdByUserId == userId;
        }

        /// <summary>
        /// Une commande d'un autre utilisateur est traitée comme inexistante (404, même message) :
        /// on ne révèle pas son existence (protection contre l'énumération / IDOR).
        /// </summary>
        private void EnsureCanAccessCommande(Commandes commande)
        {
            if (!PeutAccederCommande(commande.CreatedByUserId))
            {
                throw new BusinessRuleException(MessageCommandeIntrouvable, StatusCodes.Status404NotFound);
            }
        }

        #endregion

        #region Get All Commandes
        public async Task<IEnumerable<CommandeDto>> GetAllCommandesAsync(CancellationToken cancellationToken)
        {
            var userId = _currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.", StatusCodes.Status401Unauthorized);
            }

            // Pas de cache ici : la liste est relue en base à chaque appel, filtrée par propriétaire
            // pour un utilisateur standard (le cache précédent était calculé puis ignoré).
            List<Commandes>? commandes = _currentUserService.IsAdmin
                ? await _commandeRepository.GetAllCommandesAsync(cancellationToken)
                : await _commandeRepository.GetByOwnerAsync(userId, cancellationToken);

            if (commandes == null)
                return Enumerable.Empty<CommandeDto>();

            return commandes.Adapt<List<CommandeDto>>();
        }
        #endregion

        #region Get Commande by Id
        public async Task<CommandeDto?> GetCommandeByIdAsync(int commandeId, CancellationToken cancellationToken)
        {
            var cacheKey = $"commande:{commandeId}";

            // Le DTO ne contient pas le propriétaire : on le garde à côté dans le cache
            // pour refaire le contrôle d'accès même quand la commande vient du cache.
            if (_cache.TryGetValue(cacheKey, out CommandeEnCache? commandeEnCache) && commandeEnCache is not null)
            {
                return PeutAccederCommande(commandeEnCache.CreatedByUserId) ? commandeEnCache.Commande : null;
            }

            Commandes? commande = await _commandeRepository.GetCommandeByIdAsync(commandeId, cancellationToken);

            // Inexistante ou appartenant à un autre utilisateur : même réponse (404 via le contrôleur).
            if (commande is null || !PeutAccederCommande(commande.CreatedByUserId))
            {
                return null;
            }

            CommandeDto commandeDto = commande.Adapt<CommandeDto>();
            _cache.Set(cacheKey, new CommandeEnCache(commande.CreatedByUserId, commandeDto), new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            return commandeDto;
        }
        #endregion

        #region creation Commande

        public async Task<CommandeDto> CreateCommandeAsync(CreateCommandeDto createCommandeDto, string? idempotencyKey, CancellationToken cancellationToken)
        {
            var operationLogs = new List<OperationLog>();
            string? currentUser = _currentUserService.UserId;
            OperationCommande operation = OperationCommande.Creation;

            if (string.IsNullOrWhiteSpace(currentUser))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.", StatusCodes.Status401Unauthorized);
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new BusinessRuleException(

                   "Le header Idempotency-Key est obligatoire."
                );
            }

            idempotencyKey = idempotencyKey.Trim();

            if (idempotencyKey.Length > 128)
            {
                throw new BusinessRuleException(
                    "Le header Idempotency-Key ne doit pas dépasser 128 caractères."
                );
            }

            // Déclarer la variable en dehors du try
            Commandes createdCommande = null!;

            await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);


            try
            {
                var scope = "POST:/api/commandes";
                var requestHash = RequestHashHelper.Compute(createCommandeDto);
                var existingRecord = await _dbContext.IdempotencyRecords
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Scope == scope && x.IdempotencyKey == idempotencyKey, cancellationToken);

                if (existingRecord is not null)
                {
                    if (existingRecord.RequestHash != requestHash)
                    {
                        throw new BusinessRuleException(
                            "Cette clé d'idempotence a déjà été utilisée avec un contenu différent.", StatusCodes.Status422UnprocessableEntity);
                    }

                    if (existingRecord.Status == "Completed" &&
                        !string.IsNullOrWhiteSpace(existingRecord.ResponseBody))
                    {
                        var commandeExistante = JsonSerializer.Deserialize<CommandeDto>(
                            existingRecord.ResponseBody);

                        return commandeExistante!;
                    }

                    throw new BusinessRuleException(
                        "Une requête avec cette clé d'idempotence est déjà en cours de traitement.", StatusCodes.Status409Conflict);
                }

                var now = DateTime.UtcNow;



                var record = new IdempotencyRecord
                {
                    Scope = scope,
                    IdempotencyKey = idempotencyKey,
                    RequestHash = requestHash,
                    Status = "Processing",
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddHours(24)
                };

                _dbContext.IdempotencyRecords.Add(record);
                await _dbContext.SaveChangesAsync(cancellationToken);

            
                Clients? client;

                if (_currentUserService.IsAdmin)
                {
                    // L'administrateur peut passer commande pour n'importe quel client.
                    client = await _clientRepository.GetClientByIdAsync(createCommandeDto.ClientId, cancellationToken);
                }
                else
                {
                    // Un utilisateur ne commande que pour le client relié à son compte (relation 0..1).
                    client = await _clientRepository.GetClientByUserIdAsync(currentUser, cancellationToken);

                    if (client is null)
                        throw new BusinessRuleException(
                            "Aucun client n'est relié à votre compte utilisateur.", StatusCodes.Status403Forbidden);

                    if (createCommandeDto.ClientId != client.ClientId)
                        throw new BusinessRuleException(
                            "Vous ne pouvez passer commande que pour le client relié à votre compte.", StatusCodes.Status403Forbidden);
                }

                if (client == null)
                    throw new BusinessRuleException("Client introuvable.");

                if (!client.IsActive)
                    throw new BusinessRuleException("Le client est désactivé : impossible de passer commande.");

                string correlationId = Guid.NewGuid().ToString("N");
                var validation = await _createCommandeValidator.ValidateAsync(createCommandeDto, cancellationToken);

                if (!validation.IsValid)
                {
                    throw new FluentValidation.ValidationException(validation.Errors);
                }
                var produitIds = createCommandeDto.LignesCommande.Select(l => l.ProduitId).Distinct().ToList();

                Dictionary<int, int> produitsIdsDico = createCommandeDto.LignesCommande
                    .GroupBy(l => l.ProduitId)
                    .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantite));

                Dictionary<int, Produits> produitsParId = await GetProduitsParIdsAsync(correlationId, produitIds, operation, cancellationToken);

                var resultatPreparation = CommandeService.PreparerCommande(produitsParId, produitsIdsDico);
                ICollection<LignesCommande> lignesCommande = resultatPreparation.LignesCommande.ToList();

                Commandes commande = new Commandes
                {
                    ClientId = createCommandeDto.ClientId,
                    CreatedByUserId = currentUser,
                    DateCommande = DateTime.UtcNow,
                    TotalCommandeTTC = resultatPreparation.TotalCommandeTTC,
                    Statut = StatutCommande.EnCours,
                    LignesCommande = lignesCommande,
                };

                await SetLogReportCommandAsync(produitsParId, null, commande, operation, currentUser, correlationId, cancellationToken);

                // assignation dans la variable déclarée en dehors du try
                createdCommande = await _commandeRepository.CreateCommandeAsync(commande, cancellationToken);
                record.Status = "Completed";
                record.ResponseStatusCode = StatusCodes.Status201Created;
                record.ResponseBody = JsonSerializer.Serialize(createdCommande.Adapt<CommandeDto>());
                record.CompletedAtUtc = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);

                // Commit transaction si tout est OK
                await transaction.CommitAsync(cancellationToken);
                ProduitCacheKeys.Invalider(_cache, produitIds);
                return createdCommande.Adapt<CommandeDto>();
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }


        }
        #endregion

        #region Update Commande
        public async Task<CommandeDto> UpdateCommandeAsync(int id, UpdateCommandeDto updateCommandeDto, CancellationToken cancellationToken)
        {
            OperationCommande operation = OperationCommande.Modification;
            var operationLogs = new List<OperationLog>();
            string? currentUser = _currentUserService.UserId;
            string correlationId = Guid.NewGuid().ToString("N");


            if (string.IsNullOrWhiteSpace(currentUser))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.", StatusCodes.Status401Unauthorized);
            }



            var commande = await _commandeRepository.GetCommandeByIdAsync(id, cancellationToken);

            // Vérification des règles métier de la commande à modifier  avant la mise à jour si elligible.

            if (commande is null)
                throw new BusinessRuleException(MessageCommandeIntrouvable, StatusCodes.Status404NotFound);

            // Contrôle d'accès AVANT les règles métier : sinon le message de statut révélerait
            // l'existence d'une commande appartenant à un autre utilisateur.
            EnsureCanAccessCommande(commande);

            if (commande.Statut != StatutCommande.EnCours)
                throw new BusinessRuleException("Impossible de modifier une commande annulée ou acquittée.");

            // Décodage avant toute modification du stock : une RowVersion invalide donne une 400.
            var rowVersionClient = RowVersionHelper.Decoder(updateCommandeDto.RowVersion);
            var ancienneCommande = new Commandes
            {
                CommandeId = commande.CommandeId,
                ClientId = commande.ClientId,
                DateCommande = commande.DateCommande,
                TotalCommandeTTC = commande.TotalCommandeTTC,
                Statut = commande.Statut,
                FacturePath = commande.FacturePath,
                RowVersion = commande.RowVersion,
                LignesCommande = commande.LignesCommande.Select(l => new LignesCommande
                {
                    LigneCommandeId = l.LigneCommandeId,
                    CommandeId = l.CommandeId,
                    ProduitId = l.ProduitId,
                    Quantite = l.Quantite,
                    PrixUnitaireTTC = l.PrixUnitaireTTC
                }).ToList()
            };
            var anciensProduitIds = commande.LignesCommande
                  .Select(ligne => ligne.ProduitId);

            var nouveauxProduitIds = updateCommandeDto.LignesCommande
                .Select(ligne => ligne.ProduitId);

            var produitIds = anciensProduitIds
                .Concat(nouveauxProduitIds)
                .Distinct()
                .ToList();

            // Vérification si les lignes modifiées sont valides.
            var produitsParId = await GetProduitsParIdsAsync(correlationId, produitIds, operation, cancellationToken);

            // Stock réel avant restitution des anciennes lignes (sert à journaliser les mouvements).
            var stocksAvant = produitsParId.ToDictionary(p => p.Key, p => p.Value.Stock);

            ResetLignesDeCommande(commande, produitsParId);

            var nouvellesLignes = updateCommandeDto.LignesCommande
            .Select(ligne => new LignesCommande
            {
                ProduitId = ligne.ProduitId,
                Quantite = ligne.Quantite
            })
            .ToList();

            var produitsIdsDico = updateCommandeDto.LignesCommande
                .GroupBy(l => l.ProduitId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantite));
            var resultatPreparation = CommandeService.PreparerCommande(produitsParId, produitsIdsDico);

            commande.TotalCommandeTTC = resultatPreparation.TotalCommandeTTC;

            foreach (var ligneFinalisee in resultatPreparation.LignesCommande)
            {
                commande.LignesCommande.Add(ligneFinalisee);
            }
            _commandeRepository.SetOriginalRowVersion(commande, rowVersionClient);

            // AuditLog / OperationLog si nécessaire.
            await SetLogReportCommandAsync(produitsParId, ancienneCommande, commande, operation, currentUser, correlationId, cancellationToken, stocksAvant);

            try
            {
                await _commandeRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "La commande a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.", StatusCodes.Status409Conflict);
            }

            _cache.Remove($"commande:{commande.CommandeId}");
            ProduitCacheKeys.Invalider(_cache, produitIds);
            return commande.Adapt<CommandeDto>();
        }
        #endregion

        #region Archive Commande
        public async Task ArchiveCommandeByIdAsync(int id,string? rowVersion, CancellationToken cancellationToken)
        {
            const OperationCommande operation = OperationCommande.Suppression;

            string correlationId = Guid.NewGuid().ToString("N");
            string currentUser = _currentUserService.UserName ?? "Anonymous";

            var commandeTracke = await _commandeRepository.GetCommandeByIdAsync(id, cancellationToken);

            if (commandeTracke is null)
            {
                throw new BusinessRuleException(
                    MessageCommandeIntrouvable, StatusCodes.Status404NotFound);
            }

            EnsureCanAccessCommande(commandeTracke);
            if (commandeTracke.Statut == StatutCommande.Archivee)
            {
                throw new BusinessRuleException(
                    "Une operation de suppression sur cette commande  ne peut pas être effectuée.");
            }

            // Snapshot indépendant : aucune référence commune avec clientTracke.
            var commandeAvantDesactivation = new Commandes
            {
                CommandeId = commandeTracke.CommandeId,
                ClientId = commandeTracke.ClientId,
                Statut = commandeTracke.Statut,
                TotalCommandeTTC = commandeTracke.TotalCommandeTTC,
                DateCommande = commandeTracke.DateCommande,
                FacturePath = commandeTracke.FacturePath,
                RowVersion = commandeTracke.RowVersion,
                LignesCommande = commandeTracke.LignesCommande.Select(l => new LignesCommande
                {
                    LigneCommandeId = l.LigneCommandeId,
                    CommandeId = l.CommandeId,
                    ProduitId = l.ProduitId,
                    Quantite = l.Quantite,
                    PrixUnitaireTTC = l.PrixUnitaireTTC
                }).ToList()
                // Copier d'autres propriétés pertinentes
            };

            // Modification de l'entité réellement suivie par EF Core.
            commandeTracke.Statut = StatutCommande.Archivee; // Mark the commmande as archived instead of deleting it
            if(string.IsNullOrEmpty(rowVersion))
            {
                throw new BusinessRuleException(
                    "La version de ligne (RowVersion) est requise pour l'archivage de la commande.", StatusCodes.Status428PreconditionRequired);
            }
            _commandeRepository.SetOriginalRowVersion(commandeTracke, RowVersionHelper.Decoder(rowVersion));

            await SetLogArchiveReportCommandAsync(commandeAvantDesactivation, commandeTracke, operation, currentUser, correlationId, cancellationToken);

            try
            {
                await _commandeRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "La commande a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.", StatusCodes.Status409Conflict);
            }
            _cache.Remove($"commande:{commandeTracke.CommandeId}");
        }
        #endregion

        #region Objets internes
        private sealed record CommandeEnCache(string? CreatedByUserId, CommandeDto Commande);

        public sealed record ResultatPreparationCommande( IReadOnlyCollection<LignesCommande> LignesCommande, decimal TotalCommandeTTC);

        private enum OperationCommande
        {
            Creation,
            Modification,
            Suppression
        }
        #endregion
    }
}
