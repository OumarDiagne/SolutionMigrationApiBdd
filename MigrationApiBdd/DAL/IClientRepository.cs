using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;

namespace MigrationApiBdd.DAL
{
    public interface IClientRepository
    {
        Task<Clients> CreateClientAsync(Clients client,CancellationToken cancellationToken);
        Task SaveChangeAsync(CancellationToken cancellationToken);
        Task<List<Clients>> GetAllClientsAsync();
        Task<Clients?> GetClientByIdAsync(int id, CancellationToken cancellationToken);
        Task<Clients?> GetClientByUserIdAsync(string applicationUserId, CancellationToken cancellationToken);
        Task<List<Clients>> GetClientsWithCommandesAsync();
        Task<List<Clients>> GetCommandesByClientAsync(int id, CancellationToken cancellationToken);
        Task<Clients> UpdateClientAsync(Clients clientToUpdate);
        void SetOriginalRowVersion(Clients clientTracke, byte[] rowVersion);
    }
}
