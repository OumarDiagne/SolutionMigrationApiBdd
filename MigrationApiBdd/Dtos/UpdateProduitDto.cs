using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos
{
    public class UpdateProduitDto
    {
        public string NomProduit { get; set; } = null!;
        public string? Description { get; set; }
        public decimal PrixUnitaireTTC { get; set; }
        public int Stock { get; set; }
        public bool EstDisponible { get; set; } = true;

        // Reçue depuis le GET précédent, renvoyée sans modification par le front.
        [Required(ErrorMessage = "La version de la ligne est requise.")]
        public string RowVersion { get; init; } = string.Empty;
    }
}
