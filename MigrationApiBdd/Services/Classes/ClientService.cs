using Mapster;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Helpers;
using MigrationApiBdd.DAL;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Models;
using MigrationApiBdd.Services.Auth;
using MigrationApiBdd.Services.Interfaces;
using System.Text.Json;
using System.Threading.Tasks;

namespace MigrationApiBdd.Services.Classes
{
    public class ClientService :IClientService
    {
        private readonly IClientRepository _clientRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly ICurrentUserService _currentUserService;

        public ClientService( IClientRepository clientRepository, IAuditLogRepository auditLogRepository, ICurrentUserService currentUserService )
        {
            _clientRepository = clientRepository;
            _auditLogRepository = auditLogRepository;
            _currentUserService = currentUserService;
        }

        /// <summary>
        /// Create a new client in the database
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public async Task<ClientDto> CreateClientAsync(CreateClientDto createClientDto,CancellationToken cancellationToken)
        {
            OperationClient operation = OperationClient.Creation;
            string correlationId = Guid.NewGuid().ToString("N");
            string currentUser = _currentUserService.UserName ?? "Anonymous";
            var client = createClientDto.Adapt<Clients>();

            await SetLogReportClientAsync(null,client, operation, currentUser, correlationId,cancellationToken);
            await _clientRepository.CreateClientAsync(client,cancellationToken);
            var clientDto= client.Adapt<ClientDto>();
            return clientDto;
        }


        public async Task DeleteClientByIdAsync(int id, string? rowVersion, CancellationToken cancellationToken)
        {
            const OperationClient operation = OperationClient.Suppression;

            string correlationId = Guid.NewGuid().ToString("N");
            string currentUser = _currentUserService.UserName ?? "Anonymous";

            var clientTracke = await _clientRepository.GetClientByIdAsync( id, cancellationToken);

            // Client inexistant ou appartenant à un autre utilisateur : même réponse (404),
            // on ne révèle pas l'existence des clients des autres.
            if (clientTracke is null || !PeutAccederClient(clientTracke))
            {
                throw new BusinessRuleException(
                    "Le client à supprimer n'a pas été trouvé.", StatusCodes.Status404NotFound);
            }

            if (!clientTracke.IsActive)
            {
                throw new BusinessRuleException(
                    "Le client est déjà désactivé.");
            }

            // Snapshot indépendant : aucune référence commune avec clientTracke.
            var clientAvantDesactivation = clientTracke.Adapt<Clients>();

            // Modification de l'entité réellement suivie par EF Core.
            clientTracke.Deactivate(); // Mark the client as inactive instead of deleting it
            if (string.IsNullOrEmpty(rowVersion))
            {
                throw new BusinessRuleException(
                    "La version de ligne (RowVersion) est requise pour l'archivage du client.");
            }
            _clientRepository.SetOriginalRowVersion(clientTracke,RowVersionHelper.Decoder(rowVersion));
            await SetLogReportClientAsync(clientAvantDesactivation, clientTracke, operation, currentUser,  correlationId, cancellationToken);

            try
            {
                await _clientRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "Le client a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.",
                    StatusCodes.Status409Conflict);
            }
        }

        /// <summary>
        /// Get all clients from the database
        /// </summary>
        /// <returns></returns>
        public async Task<List<ClientDto>> GetAllClientsAsync()
        {
            var clients = await _clientRepository.GetAllClientsAsync();
            return clients.Adapt<List<ClientDto>>();
        }

        /// <summary>
        /// Get a client by id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<ClientDto?> GetClientByIdAsync(int id)
        {
            var client = await _clientRepository.GetClientByIdAsync(id, CancellationToken.None);

            // Inexistant ou appartenant à un autre utilisateur : même réponse (404 via le contrôleur).
            if (client is null || !PeutAccederClient(client))
                return null;

            return client.Adapt<ClientDto>();
        }

        /// <summary>
        /// Client métier relié au compte de l'utilisateur connecté (null si aucun, par exemple un compte admin).
        /// </summary>
        public async Task<ClientDto?> GetMyClientAsync(CancellationToken cancellationToken)
        {
            var userId = _currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.", StatusCodes.Status401Unauthorized);
            }

            var client = await _clientRepository.GetClientByUserIdAsync(userId, cancellationToken);
            return client?.Adapt<ClientDto>();
        }

        public async Task<List<ClientDto>> GetClientsWithCommandes()
        {
            var clients = await _clientRepository.GetClientsWithCommandesAsync();
            return clients.Adapt<List<ClientDto>>();
        }

        public async Task<List<ClientDto>> GetCommandesClientByIdAsync(int id, CancellationToken cancellationToken)
        {
           List<Clients> res = await _clientRepository.GetCommandesByClientAsync(id, cancellationToken);
           res = res.Where(PeutAccederClient).ToList();
           return res.Adapt<List<ClientDto>>();
        }


        public async Task<ClientDto> UpdateClientAsync(int id,UpdateClientDto updateClientDto, CancellationToken cancellationToken)
        {
            OperationClient operation = OperationClient.Modification;
            string correlationId = Guid.NewGuid().ToString("N");
            string currentUser = _currentUserService.UserName ?? "Anonymous";

            var clientTracke = await _clientRepository.GetClientByIdAsync(id, cancellationToken);


            // Client inexistant ou appartenant à un autre utilisateur : même réponse (404).
            if (clientTracke is null || !PeutAccederClient(clientTracke))
                throw new BusinessRuleException(
                    "Le client à modifier n'a pas été trouvé.", StatusCodes.Status404NotFound);

            if (!clientTracke.IsActive)
            {
                throw new BusinessRuleException(
                    "Un client désactivé ne peut pas être modifié.");
            }

            // Snapshot indépendant pour l'audit : état avant modification. en recreant une nouvelle entité Clients à partir de l'entité suivie par EF Core.
            var oldClientSnapshot = clientTracke.Adapt<Clients>();

            // La modification réelle porte sur l'instance trackée.
            // Les private set restent encapsulés dans l'entité.
            clientTracke.UpdateInformations(updateClientDto);
            

            _clientRepository.SetOriginalRowVersion(clientTracke,RowVersionHelper.Decoder(updateClientDto.RowVersion));

            await SetLogReportClientAsync(oldClientSnapshot, clientTracke, operation, currentUser, correlationId, cancellationToken);
            try
            {
                await _clientRepository.SaveChangeAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessRuleException(
                    "Le client a été modifié par un autre utilisateur. Rechargez les données avant de réessayer.",
                     StatusCodes.Status409Conflict);
            }
            return clientTracke.Adapt<ClientDto>();
        }

     

        private Task SetLogReportClientAsync( Clients? oldClient, Clients? newClient, OperationClient operation, string currentUser, string correlationId, CancellationToken cancellationToken)
        {
            var auditLog = new AuditLog
            {
                EntityName = nameof(Clients),

                EntityId = newClient?.ClientId > 0 ? 
                           newClient.ClientId.ToString() : oldClient?.ClientId > 0 ? 
                                                           oldClient.ClientId.ToString() : correlationId,

                ActionType = operation switch
                {
                    OperationClient.Creation => "INSERT",
                    OperationClient.Modification => "UPDATE",
                    OperationClient.Suppression => "DELETE",
                    _ => throw new BusinessRuleException("Action non prise en charge.")
                },

                OldValue = oldClient is null
                    ? null
                    : ConstruireValeurAuditClient(oldClient),

                NewValue = newClient is null
                    ? null
                    : ConstruireValeurAuditClient(newClient),

                ChangedBy = currentUser,
                ChangedAtUtc = DateTime.UtcNow,
                CorrelationId = correlationId,
                Reason = $"{operation} de client"
            };

            _auditLogRepository.AddRange([auditLog]);

            return Task.CompletedTask;
        }

        private string ConstruireValeurAuditClient(Clients client)
        {
            var valeurAudit = new
            {
                client.ClientId,
                client.Nom,
                client.Prenom,
                client.RowVersion,
                client.IsActive,
                client.DeactivatedAtUtc,
                Commandes= client.Commandes.OrderBy(c => c.CommandeId).Select(c => new
                {
                    c.CommandeId,
                    c.DateCommande,
                    c.FacturePath,
                    c.TotalCommandeTTC
                })
            };

            return JsonSerializer.Serialize(valeurAudit);
  
        }

        /// <summary>
        /// Admin : accès à tous les clients. Utilisateur : uniquement le client relié à son compte
        /// (Clients.ApplicationUserId == identifiant de l'utilisateur connecté).
        /// Un client non relié à un compte n'est accessible qu'à l'admin.
        /// </summary>
        private bool PeutAccederClient(Clients client)
        {
            if (_currentUserService.IsAdmin)
                return true;

            var userId = _currentUserService.UserId;

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new BusinessRuleException(
                    "Utilisateur authentifié introuvable.", StatusCodes.Status401Unauthorized);
            }

            return client.ApplicationUserId == userId;
        }

        private enum OperationClient
        {
            Creation,
            Modification,
            Suppression
        }
    }
}
