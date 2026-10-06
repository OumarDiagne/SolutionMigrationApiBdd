namespace MigrationApiBdd.Dtos
{
    public class CreateProduitDto
    {
        public string NomProduit { get; set; } = null!;
        public string? Description { get; set; }
        public decimal PrixUnitaireTTC { get; set; }
        public int Stock { get; set; }
        // Pas de EstDisponible : un produit créé est toujours disponible (règle métier).
    }
}
