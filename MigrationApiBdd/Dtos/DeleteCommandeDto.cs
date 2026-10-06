using System.ComponentModel.DataAnnotations;

namespace MigrationApiBdd.Dtos
{
    public class DeleteCommandeDto
    {
        [Required(ErrorMessage = "La version de la ligne est requise.")]
        public string RowVersion { get; init; } = string.Empty;
    }
}
