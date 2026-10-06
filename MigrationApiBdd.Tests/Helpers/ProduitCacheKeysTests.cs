using Microsoft.Extensions.Caching.Memory;
using MigrationApiBdd.Helpers;
using Xunit;

namespace MigrationApiBdd.Tests.Helpers;

public class ProduitCacheKeysTests
{
    private static MemoryCache NouveauCache() => new(new MemoryCacheOptions());

    [Fact]
    public void Produit_ConstruitLaCleAttendue()
    {
        Assert.Equal("produit:42", ProduitCacheKeys.Produit(42));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Liste_SansFiltre_UtiliseLEtoile(string? filtre)
    {
        using var cache = NouveauCache();

        Assert.Equal("produits:list:v0:*", ProduitCacheKeys.Liste(cache, filtre));
    }

    [Fact]
    public void Liste_NormaliseLeFiltre()
    {
        using var cache = NouveauCache();

        Assert.Equal("produits:list:v0:clavier", ProduitCacheKeys.Liste(cache, "  ClaViEr "));
    }

    [Fact]
    public void Invalider_SupprimeLesProduitsEtIncrementeLaVersionDesListes()
    {
        using var cache = NouveauCache();
        cache.Set(ProduitCacheKeys.Produit(1), "p1");
        cache.Set(ProduitCacheKeys.Produit(2), "p2");
        cache.Set(ProduitCacheKeys.Produit(3), "p3");
        var cleListeAvant = ProduitCacheKeys.Liste(cache, null);

        ProduitCacheKeys.Invalider(cache, [1, 2]);

        Assert.False(cache.TryGetValue(ProduitCacheKeys.Produit(1), out _));
        Assert.False(cache.TryGetValue(ProduitCacheKeys.Produit(2), out _));
        Assert.True(cache.TryGetValue(ProduitCacheKeys.Produit(3), out _));
        Assert.NotEqual(cleListeAvant, ProduitCacheKeys.Liste(cache, null));
        Assert.Equal("produits:list:v1:*", ProduitCacheKeys.Liste(cache, null));
    }

    [Fact]
    public void Invalider_PlusieursFois_IncrementeLaVersionACHaqueAppel()
    {
        using var cache = NouveauCache();

        ProduitCacheKeys.Invalider(cache, 1);
        ProduitCacheKeys.Invalider(cache, 1);

        Assert.Equal("produits:list:v2:*", ProduitCacheKeys.Liste(cache, null));
    }
}
