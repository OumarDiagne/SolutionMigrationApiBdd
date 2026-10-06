using MigrationApiBdd.Models;

namespace MigrationApiBdd.Dtos
{
    public class ProduitDto
    {
        public int ProduitId { get; set; }
        public string NomProduit { get; set; } = null!;
        public string? Description { get; set; }
        public decimal PrixUnitaireTTC { get; set; }
        public int Stock { get; set; }
        public bool EstDisponible { get; set; } = true;
        public ICollection<LignesCommandeDto> LignesCommande { get; set; } = [];
        public virtual ICollection<StockMouvementDto> StockMouvements { get; set; } = [];
        public string RowVersion { get; init; } = string.Empty;
    }
}
