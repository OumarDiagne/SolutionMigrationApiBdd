namespace MigrationApiBdd.Dtos
{
    public class ReapprovisionnerStockDto
    {
        public int Quantite { get; init; }

        public string RowVersion { get; init; } = string.Empty;

        public string? Motif { get; init; }
    }
}
