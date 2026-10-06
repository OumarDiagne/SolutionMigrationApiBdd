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
    }
}
