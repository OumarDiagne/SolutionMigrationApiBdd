using Microsoft.Extensions.Caching.Memory;

namespace MigrationApiBdd.Helpers
{
    /// <summary>
    /// Clés et invalidation du cache des produits.
    /// Les listes (une entrée par filtre nomProduit) sont versionnées : incrémenter
    /// la version rend obsolètes d'un coup toutes les listes en cache, quel que soit le filtre.
    /// À appeler par tout service qui modifie un produit ou son stock
    /// (ProduitService, CommandeService, StockService).
    /// </summary>
    public static class ProduitCacheKeys
    {
        private const string VersionListeKey = "produits:list:version";

        public static readonly TimeSpan Duree = TimeSpan.FromMinutes(5);

        public static string Produit(int produitId) => $"produit:{produitId}";

        public static string Liste(IMemoryCache cache, string? nomProduit)
        {
            long version = cache.TryGetValue(VersionListeKey, out long v) ? v : 0L;
            string filtre = string.IsNullOrWhiteSpace(nomProduit)
                ? "*"
                : nomProduit.Trim().ToLowerInvariant();

            return $"produits:list:v{version}:{filtre}";
        }

        public static void Invalider(IMemoryCache cache, IEnumerable<int> produitIds)
        {
            foreach (int produitId in produitIds.Distinct())
            {
                cache.Remove(Produit(produitId));
            }

            long version = cache.TryGetValue(VersionListeKey, out long v) ? v : 0L;
            cache.Set(VersionListeKey, version + 1, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.NeverRemove
            });
        }

        public static void Invalider(IMemoryCache cache, int produitId) => Invalider(cache, [produitId]);
    }
}
