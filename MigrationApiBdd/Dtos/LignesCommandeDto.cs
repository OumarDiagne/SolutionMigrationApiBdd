using MigrationApiBdd.Models;

namespace MigrationApiBdd.Dtos
{
    public class LignesCommandeDto
    {
        public int LigneCommandeId { get; set; }
        public int CommandeId { get; set; }
        public int ProduitId { get; set; }
        public int Quantite { get; set; }
        public decimal PrixUnitaireTTC { get; set; }
    }
}
