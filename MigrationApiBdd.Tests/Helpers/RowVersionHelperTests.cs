using Microsoft.AspNetCore.Http;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Helpers;
using Xunit;

namespace MigrationApiBdd.Tests.Helpers;

public class RowVersionHelperTests
{
    private static readonly byte[] Octets = [0, 0, 0, 0, 0, 0, 0x1F, 0xA5];

    [Fact]
    public void Decoder_Base64Valide_RetourneLesOctets()
    {
        var resultat = RowVersionHelper.Decoder(Convert.ToBase64String(Octets));

        Assert.Equal(Octets, resultat);
    }

    [Fact]
    public void Decoder_FormeETagEntreGuillemets_EstAcceptee()
    {
        var etag = $"\"{Convert.ToBase64String(Octets)}\"";

        Assert.Equal(Octets, RowVersionHelper.Decoder(etag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Decoder_Absente_Leve428(string? valeur)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => RowVersionHelper.Decoder(valeur));

        Assert.Equal(StatusCodes.Status428PreconditionRequired, ex.StatusCode);
    }

    [Theory]
    [InlineData("pas-du-base64!")]
    [InlineData("abc")]
    public void Decoder_Invalide_Leve400AuLieuDUneFormatException(string valeur)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => RowVersionHelper.Decoder(valeur));

        Assert.Equal(StatusCodes.Status400BadRequest, ex.StatusCode);
        Assert.Contains("invalide", ex.Message);
    }
}
