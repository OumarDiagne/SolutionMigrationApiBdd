using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.DAL
{
    public class StockMouvementRepository : IStockMouvementRepository
    {

        private readonly MigApiContext _migApiDbContext;
        public StockMouvementRepository(MigApiContext migApiDbContext)
        {
            _migApiDbContext = migApiDbContext;
        }

        public void Add(StockMouvement stockMouvement)
        {
            _migApiDbContext.StockMouvements.Add(stockMouvement);
        }

        public void AddRange(List<StockMouvement> stockMouvements)
        {
            _migApiDbContext.StockMouvements.AddRange(stockMouvements);
        }

        public async Task<StockMouvement?> GetStockMouvementByIdAsync(int stockMouvementId, CancellationToken cancellationToken)
        {
            return await _migApiDbContext.StockMouvements.FirstOrDefaultAsync(x => x.StockMouvementId == stockMouvementId, cancellationToken);
        }

        public Task SaveChangeAsync(CancellationToken cancellationToken)
        {
           return _migApiDbContext.SaveChangesAsync(cancellationToken);
        }

        public void SetOriginalRowVersion(Produits produit, byte[] bytes)
        {
           _migApiDbContext.Entry(produit).Property(p => p.RowVersion).OriginalValue = bytes;
        }
    }
}
