using MigrationApiBdd.Dtos;

namespace MigrationApiBdd.Services.Interfaces
{
    public interface IStockService
    {
        Task<ProduitDto> ReapprovisionnerStockAsync(int id, ReapprovisionnerStockDto reapprovisionnerStockDto, CancellationToken cancellationToken);
    }
}
