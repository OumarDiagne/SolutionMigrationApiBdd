using MigrationApiBdd.Models;

namespace MigrationApiBdd.DAL
{
    public interface IStockMouvementRepository
    {
        void AddRange(List<StockMouvement> stockMouvements);
        void Add(StockMouvement stockMouvements);
        Task<StockMouvement?> GetStockMouvementByIdAsync(int stockMouvementId, CancellationToken cancellationToken);
        void SetOriginalRowVersion(Produits produit, byte[] bytes);
        Task SaveChangeAsync(CancellationToken cancellationToken);
    }
}
