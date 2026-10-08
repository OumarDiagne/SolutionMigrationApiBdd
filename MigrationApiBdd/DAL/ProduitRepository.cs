using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.DAL
{
    public class ProduitRepository : IProduitRepository
    {
        private readonly MigApiContext _migApiContext;

        public ProduitRepository(MigApiContext migApiContext)
        {
            _migApiContext = migApiContext;
        }

        public async Task<List<Produits>> GetAllProduitsAsync(string? nomProduit,CancellationToken cancellationToken)
        {
            var query = _migApiContext.Produits.AsNoTracking();

            if (!string.IsNullOrEmpty(nomProduit))
            {
                query = query.Where(p => p.NomProduit.Contains(nomProduit));
            }

            return await query.ToListAsync(cancellationToken);
        }

        public async Task<Produits?> GetProduitByIdAsync(int id,CancellationToken cancellationToken)
        {
            return await _migApiContext.Produits.FindAsync([id], cancellationToken);
        }

        public async Task<Produits> UpdateProduitAsync(Produits updateproduit,CancellationToken cancellationToken)
        {
            await _migApiContext.SaveChangesAsync(cancellationToken);
            return updateproduit;
        }

        public async Task<Produits> CreateProduitAsync(Produits createProduit,CancellationToken cancellationToken)
        {
            await  _migApiContext.Produits.AddAsync(createProduit,cancellationToken);
            await _migApiContext.SaveChangesAsync(cancellationToken);
            return createProduit;
        }   
        

        public async Task<int> DeleteProduitByIdAsync(int id,CancellationToken cancellationToken)
        { 
            

          return  await _migApiContext.SaveChangesAsync(cancellationToken);
           
        }

        public async Task<ICollection<Produits>> GetAllProduitsByIdsAsync(List<int> produitIds, CancellationToken cancellationToken)
        {
            if (produitIds.Count == 0)
                return await Task.FromResult(new List<Produits>());

            return await _migApiContext.Produits
                .Where(p => produitIds.Contains(p.ProduitId))
                .ToListAsync(cancellationToken);
        }

        public async Task  SaveChangeAsync(CancellationToken cancellationToken)
        {
            await _migApiContext.SaveChangesAsync(cancellationToken);
        }

        public void SetOriginalRowVersion(Produits produitTracke, byte[] rowVersion)
        {
            _migApiContext.Entry(produitTracke).Property(c => c.RowVersion).OriginalValue = rowVersion;
        }

        /// <summary>
        /// Applique une variation de stock par un UPDATE atomique (variation négative = sortie).
        /// Le contrôle « stock suffisant » et la modification sont faits dans la même instruction SQL :
        /// le stock ne peut jamais devenir négatif, même avec des commandes simultanées.
        /// Renvoie false si le stock disponible est insuffisant (aucune ligne modifiée).
        /// </summary>
        public async Task<bool> TryAppliquerVariationStockAsync(int produitId, int variation, CancellationToken cancellationToken)
        {
            var lignesModifiees = await _migApiContext.Produits
                .Where(p => p.ProduitId == produitId && p.Stock + variation >= 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Stock, p => p.Stock + variation), cancellationToken);

            return lignesModifiees == 1;
        }

        /// <summary>
        /// Relit le produit en base (stock et RowVersion) après un UPDATE atomique,
        /// car ExecuteUpdateAsync ne met pas à jour les entités suivies en mémoire.
        /// </summary>
        public async Task RechargerAsync(Produits produit, CancellationToken cancellationToken)
        {
            await _migApiContext.Entry(produit).ReloadAsync(cancellationToken);
        }
    }
}
