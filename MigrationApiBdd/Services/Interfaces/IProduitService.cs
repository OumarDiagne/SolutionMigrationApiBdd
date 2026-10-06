using MigrationApiBdd.Dtos;

namespace MigrationApiBdd.Services.Interfaces
{
    public interface IProduitService
    {
        Task<ProduitDto> CreateProduitAsync(CreateProduitDto createProduitDto, string? idempotencyKey, CancellationToken cancellationToken);
        Task ArchiveProduitByIdAsync(int id, string? rowVersion, CancellationToken cancellationToken);
        Task<ICollection<ProduitDto>> GetAllProduitsAsync(string? nomProduit, CancellationToken cancellationToken);
        Task<ProduitDto?> GetProduitByIdAsync(int id, CancellationToken cancellationToken);
        Task<ProduitDto> UpdateProduitAsync(int id, UpdateProduitDto updateProduitDto, CancellationToken cancellationToken);
    }
}
