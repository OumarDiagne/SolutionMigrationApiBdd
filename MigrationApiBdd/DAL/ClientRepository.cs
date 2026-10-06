using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.DAL
{
    public class ClientRepository : IClientRepository
    {
        private readonly MigApiContext _migApiContext;

        public ClientRepository( MigApiContext migApiContext )
        {
            _migApiContext = migApiContext;
        }

        public async Task<Clients> CreateClientAsync(Clients client,CancellationToken cancellationToken)
        {
            await _migApiContext.Clients.AddAsync(client,cancellationToken);
            await _migApiContext.SaveChangesAsync(cancellationToken);
            return client;

        }

        public async Task SaveChangeAsync(CancellationToken cancellationToken)
        {
            await _migApiContext.SaveChangesAsync(cancellationToken);
        
        }

        public async Task<List<Clients>> GetAllClientsAsync()
        {
            return await  _migApiContext.Clients.AsNoTracking().ToListAsync();
        }

        public async Task<List<Clients>> GetAllClientsWithCommandesAsync()
        {
            return await _migApiContext.Clients.AsNoTracking().Include(x=>x.Commandes).ToListAsync();
        }

        public async Task<Clients?> GetClientByIdAsync(int id, CancellationToken cancellationToken)
        {
         return  await _migApiContext.Clients.FirstOrDefaultAsync(c => c.ClientId == id, cancellationToken);
        }

        public async Task<Clients?> GetClientByUserIdAsync(string applicationUserId, CancellationToken cancellationToken)
        {
            return await _migApiContext.Clients.FirstOrDefaultAsync(c => c.ApplicationUserId == applicationUserId, cancellationToken);
        }

        public async Task<List<Clients>> GetClientsWithCommandesAsync()
        {
          return await _migApiContext.Clients.AsNoTracking().Include(c => c.Commandes).ToListAsync();
            
        }

        public async Task<List<Clients>> GetCommandesByClientAsync(int id, CancellationToken cancellationToken)
        {
            return await _migApiContext.Clients.Include(c => c.Commandes).Where(c => c.ClientId == id).ToListAsync(cancellationToken);
        }

        public async Task<Clients> UpdateClientAsync(Clients clientToUpdate)
        {
           var clientUpdated=  _migApiContext.Clients.Update(clientToUpdate).Entity;
            await _migApiContext.SaveChangesAsync();
            return clientUpdated;
        }


        public void SetOriginalRowVersion(Clients clientTracke, byte[] rowVersion)
        {
            
            _migApiContext.Entry(clientTracke).Property(c => c.RowVersion).OriginalValue = rowVersion;
        }
    }
}
