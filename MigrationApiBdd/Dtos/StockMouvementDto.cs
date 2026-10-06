using MigrationApiBdd.Models;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MigrationApiBdd.Dtos
{
    public class StockMouvementDto
    {
        public int StockMouvementId { get; set; }

        public int ProduitId { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TypeMouvementStock TypeMouvement { get; set; }

        [Range(1, int.MaxValue)]
        public int Quantite { get; set; }

        public int StockAvant { get; set; }
        public int StockApres { get; set; }

        [Required]
        [MaxLength(100)]
        public string? SourceOperation { get; set; }

        [Required]
        [MaxLength(100)]
        public string? Motif { get; set; }

        [MaxLength(100)]
        public string? OperationId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}

