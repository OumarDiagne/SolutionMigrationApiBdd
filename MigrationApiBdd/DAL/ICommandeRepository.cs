using Microsoft.EntityFrameworkCore.Storage;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;

namespace MigrationApiBdd.DAL
{
    public interface ICommandeRepository
    {
        Task<Commandes> CreateCommandeAsync(Commandes commande, CancellationToken cancellationToken);
        Task SaveChangesAsync( CancellationToken cancellationToken);
        Task<List<Commandes>> GetAllCommandesAsync(CancellationToken cancellationToken);
        Task<Commandes?> GetCommandeByIdAsync(int id, CancellationToken cancellationToken);
        Task SaveChangeAsync(CancellationToken cancellationToken);
        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
        void SetOriginalRowVersion(Commandes commande, byte[] rowVersion);
        Task<List<Commandes>> GetByOwnerAsync( string userId, CancellationToken cancellationToken = default);
    }
}
