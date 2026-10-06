using Mapster;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json.Serialization;

namespace MigrationApiBdd.Dtos
{
    /// <summary>
    /// Represents a Data Transfer Object (DTO) for a client.
    /// </summary>
    public class ClientDto
    {
        public int ClientId { get; set; }
        public string Nom { get; set; } = null!;
        public string Prenom { get; set; } = null!;
        public bool IsActive { get; private set; } = true;
        public DateTime? DeactivatedAtUtc { get; private set; }
        public List<CommandeDto> Commandes { get; set; } = [];
        public string RowVersion { get; init; } = string.Empty;
    }
}
