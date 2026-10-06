using MigrationApiBdd.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MigrationApiBdd.Dtos
{
    public class UpdateCommandeDto
    {
        [JsonRequired]
        [MinLength(1, ErrorMessage = "La commande doit contenir au moins une ligne de commande.")]
        public ICollection<LignesCommandeDto> LignesCommande { get; init; } = new List<LignesCommandeDto>();

        // Reçue depuis le GET précédent, renvoyée sans modification par le front.
        [Required(ErrorMessage = "La version de la ligne est requise.")]
        public string RowVersion { get; init; } =string.Empty;
    }
}
