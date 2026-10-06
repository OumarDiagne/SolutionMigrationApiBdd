using Microsoft.AspNetCore.Mvc;
using MigrationApiBdd.Dtos;

namespace MigrationApiBdd.Services.Interfaces
{
    public interface ICommandeService
    {
        Task<CommandeDto> CreateCommandeAsync(CreateCommandeDto commande, string? idempotencyKey, CancellationToken cancellationToken);
        Task<IEnumerable<CommandeDto>> GetAllCommandesAsync(CancellationToken cancellationToken);
        Task<CommandeDto?> GetCommandeByIdAsync(int id, CancellationToken cancellationToken);
        Task<CommandeDto> UpdateCommandeAsync(int id, UpdateCommandeDto commande, CancellationToken cancellationToken);
        Task ArchiveCommandeByIdAsync(int id, string? rowVersion, CancellationToken cancellationToken);
    }
}
