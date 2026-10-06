using MigrationApiBdd.Dtos;

namespace MigrationApiBdd.Models
{
    public class Commandes
    {
        public int CommandeId { get; set; }
        public  DateTime DateCommande { get; set; }
        public  decimal TotalCommandeTTC { get; set; }
        public  StatutCommande Statut { get; set; }
        public string? FacturePath { get; set; }
        public int? ClientId { get; set; }
        public string? CreatedByUserId { get; set; }
        public Clients? ClientCommande { get; set; }

        public ICollection<LignesCommande> LignesCommande { get; set; } = new HashSet<LignesCommande>();
        public byte[] RowVersion { get; set; } = [];
    }

    public enum StatutCommande
    {
        EnCours,
        Acquittee,
        Archivee,
        Annulee
    }
}
