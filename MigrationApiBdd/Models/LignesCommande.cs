namespace MigrationApiBdd.Models
{
    /// <summary>
    /// Represents a line item in an order, linking a product to a specific order with quantity and unit price.
    /// </summary>
    public class LignesCommande
    {
        public int LigneCommandeId { get; set; }
        public int CommandeId { get; set; }
        public Commandes Commande { get; set; } = null!;
        public int ProduitId { get; set; }
        public Produits Produit { get; set; } = null!;
        public int Quantite { get; set; }
        public decimal PrixUnitaireTTC { get; set; }
    }
}
