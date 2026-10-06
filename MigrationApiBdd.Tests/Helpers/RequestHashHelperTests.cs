using MigrationApiBdd.Helpers;
using Xunit;

namespace MigrationApiBdd.Tests.Helpers;

public class RequestHashHelperTests
{
    private sealed record Demande(int ProduitId, int Quantite);

    [Fact]
    public void Compute_MemeContenu_DonneLeMemeHash()
    {
        var h1 = RequestHashHelper.Compute(new Demande(1, 5));
        var h2 = RequestHashHelper.Compute(new Demande(1, 5));

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void Compute_ContenuDifferent_DonneUnHashDifferent()
    {
        var h1 = RequestHashHelper.Compute(new Demande(1, 5));
        var h2 = RequestHashHelper.Compute(new Demande(1, 6));

        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Compute_RetourneUnSha256HexadecimalEnMinuscules()
    {
        var hash = RequestHashHelper.Compute(new Demande(1, 5));

        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
