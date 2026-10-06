using MigrationApiBdd.Models;
using System.Text.Json.Serialization;

namespace MigrationApiBdd.Dtos
{
    public class CommandeDto
    {
        public int CommandeId { get; set; }
        public DateTime DateCommande { get; set; }
        public decimal TotalCommandeTTC { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public StatutCommande Statut { get; set; }
        public string? FacturePath { get; set; }
        public int? ClientId { get; set; }
        public ICollection<LignesCommandeDto> LignesCommande { get; set; } = new HashSet<LignesCommandeDto>();
        public string RowVersion { get; init; } = string.Empty;
    }                   
}

