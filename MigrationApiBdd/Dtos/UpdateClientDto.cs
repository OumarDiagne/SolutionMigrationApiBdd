using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos
{
    public class UpdateClientDto
    {
        [Required(ErrorMessage ="Le nom du client est requis.", AllowEmptyStrings = false)]
        public string Nom { get; init; } = string.Empty;

        [Required(ErrorMessage ="Le prénom du client est requis.", AllowEmptyStrings = false)]
        public string Prenom { get; init; } = string.Empty;

        // Reçue depuis le GET précédent, renvoyée sans modification par le front.
        [Required(ErrorMessage ="La version de la ligne est requise.")]
        public string RowVersion { get; init; } = string.Empty;
    }
}
