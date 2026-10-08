using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;

namespace MigrationApiBdd.DAL
{
    public interface IProduitRepository
    {
        Task<Produits> CreateProduitAsync(Produits createProduit,CancellationToken cancellationToken);
        Task<int> DeleteProduitByIdAsync(int id,CancellationToken cancellationToken);
        Task<List<Produits>> GetAllProduitsAsync(string? nomProduit,CancellationToken cancellationToken);
        Task<ICollection<Produits>> GetAllProduitsByIdsAsync(List<int> produitIds, CancellationToken cancellationToken);
        Task<Produits?> GetProduitByIdAsync(int id,CancellationToken cancellationToken);
        Task SaveChangeAsync(CancellationToken cancellationToken);
        void SetOriginalRowVersion(Produits produitTracke, byte[] rowVersion);
        Task<bool> TryAppliquerVariationStockAsync(int produitId, int variation, CancellationToken cancellationToken);
        Task RechargerAsync(Produits produit, CancellationToken cancellationToken);
        Task<Produits> UpdateProduitAsync(Produits updateproduit,CancellationToken cancellationToken);
    }
}
