using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models.Identity;

namespace MigrationApiBdd.Models
{
    /// <summary>
    /// Represents a client in the system, containing personal information and a list of associated orders.
    /// </summary>
    public class Clients
    {
        public int ClientId { get; set; }
        public string Nom { get; set; } = null!;    
        public string Prenom { get; set; }=null!;
        public bool IsActive { get; private set; } = true;
        public string? ApplicationUserId { get; set; }
        public ApplicationUser? ApplicationUser { get; set; }
        public DateTime? DeactivatedAtUtc { get; private set; }
        public ICollection<Commandes> Commandes { get; set; } = new HashSet<Commandes>();
        // Propriété de persistance EF Core / SQL Server.
        public byte[] RowVersion { get; set; } = [];


        public void Deactivate()
        {
            if (!IsActive)
                return;

            IsActive = false;
            DeactivatedAtUtc = DateTime.UtcNow;
        }

        public void Reactivate()
        {
            if (IsActive)
                return;

            IsActive = true;
            DeactivatedAtUtc = null;
        }

        internal void UpdateInformations(UpdateClientDto updateClientDto)
        {
            Nom = updateClientDto.Nom.Trim();
            Prenom = updateClientDto.Prenom.Trim();
        }
    }
}
