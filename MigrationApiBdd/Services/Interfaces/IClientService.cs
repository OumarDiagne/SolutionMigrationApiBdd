using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;

namespace MigrationApiBdd.Services.Interfaces
{
    public interface IClientService
    {
        Task<List<ClientDto>> GetAllClientsAsync();
        Task<ClientDto?> GetClientByIdAsync(int id);
        Task<ClientDto?> GetMyClientAsync(CancellationToken cancellationToken);
        Task<ClientDto> CreateClientAsync(CreateClientDto createClientDto,CancellationToken cancellationToken);
        Task<ClientDto> UpdateClientAsync(int id, UpdateClientDto updateClientDto, CancellationToken cancellationToken);
        Task DeleteClientByIdAsync(int id, string? rowVersion, CancellationToken cancellationToken);
        Task<List<ClientDto>> GetCommandesClientByIdAsync(int id, CancellationToken cancellationToken);
    }
}
