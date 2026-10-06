namespace MigrationApiBdd.Models
{
    public class Produits
    {
        public int ProduitId { get; set; }
        public string NomProduit { get; set; }=null!;
        public string? Description { get; set; }
        public decimal PrixUnitaireTTC { get; set; }
        public int Stock { get; set; }
        public bool EstDisponible { get; set; } = true;
        public ICollection<LignesCommande> LignesCommande { get; set; } = [];
        public virtual ICollection<StockMouvement> StockMouvements { get; set; } = [];
        public byte[] RowVersion { get; set; } = [];
    }

    public enum OperationProduit
    {
        Creation,
        Modification,
        Suppression,
        Reapprovisionnement
    }
}
