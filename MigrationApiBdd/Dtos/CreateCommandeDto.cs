using MigrationApiBdd.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MigrationApiBdd.Dtos
{
    /// <summary>
    /// DTO for creating a new Commande (Order).
    /// </summary>
    public class CreateCommandeDto
    {
        [JsonRequired]
        [Range(1, int.MaxValue, ErrorMessage = "L'Id du client doit être supérieur à 0.")]
        public int ClientId { get; set; }
        [JsonRequired]
        [MinLength(1, ErrorMessage = "La commande doit contenir au moins une ligne de commande.")]
        public ICollection<CreateLigneCommandeDto> LignesCommande { get; set; } = [];
    }
}
 