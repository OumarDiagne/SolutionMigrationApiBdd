using Azure.Core;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;
using System.ComponentModel.Design;

namespace MigrationApiBdd.DAL
{
    public class CommandeRepository : ICommandeRepository
    {
        private readonly MigApiContext _migApiContext;

        public CommandeRepository(MigApiContext migApiContext)
        {
            _migApiContext = migApiContext;
        }

        public async Task<Commandes> CreateCommandeAsync(Commandes commande, CancellationToken cancellationToken)
        {

            // Création de la commande + lignes + mouvements de stock.
            // Une seule transaction et un seul SaveChangesAsync à la fin.
            await _migApiContext.Commandes.AddAsync(commande, cancellationToken);
            await _migApiContext.SaveChangesAsync(cancellationToken);
            return commande;

        }

        public async Task SaveChangesAsync( CancellationToken cancellationToken)
        {
            await _migApiContext.SaveChangesAsync();
            // return await _migApiContext.Commandes.Where(c => c.CommandeId == id).ExecuteDeleteAsync(cancellationToken);
        }

        public async Task<List<Commandes>> GetAllCommandesAsync(CancellationToken cancellationToken)
        {
           return await _migApiContext.Commandes.AsNoTracking().ToListAsync(cancellationToken);
        }

        public async Task<Commandes?> GetCommandeByIdAsync(int id, CancellationToken cancellationToken)
        {
            return await _migApiContext.Commandes.Include(c => c.LignesCommande).FirstOrDefaultAsync(c => c.CommandeId == id, cancellationToken);
        }

        public async Task SaveChangeAsync(CancellationToken cancellationToken)
        {
           await _migApiContext.SaveChangesAsync(cancellationToken);
        }

        public void SetOriginalRowVersion(Commandes commande, byte[] rowVersion)
        {
            _migApiContext.Entry(commande).Property(c => c.RowVersion).OriginalValue = rowVersion;
        }

        public async Task<List<Commandes>> GetByOwnerAsync(string userId, CancellationToken cancellationToken = default)
        {
            return await _migApiContext.Commandes.AsNoTracking().Where(c => c.CreatedByUserId == userId).ToListAsync(cancellationToken);
        }

    }
}
