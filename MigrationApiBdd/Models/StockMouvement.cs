using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Models
{
    public class StockMouvement
    {
        public int StockMouvementId { get; set; }

        public int ProduitId { get; set; }
        public Produits Produit { get; set; } = null!;

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

    public enum TypeMouvementStock
    {
        Entree = 1,
        Sortie = 2,
        Ajustement = 3,
        RAZ = 4
    }
}
