using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos
{
    public class CreateClientDto
    {
        [Required(ErrorMessage = "Le nom du client est requis.", AllowEmptyStrings = false)]
        public string Nom { get; init; } = string.Empty;

        [Required(ErrorMessage = "Le prénom du client est requis.", AllowEmptyStrings = false)]
        public string Prenom { get; init; } = string.Empty;
    }
}

