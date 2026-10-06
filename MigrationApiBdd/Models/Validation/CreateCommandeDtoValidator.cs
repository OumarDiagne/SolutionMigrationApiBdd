using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.Models.Validation
{
    public sealed class CreateCommandeDtoValidator
      : AbstractValidator<CreateCommandeDto>
    {
        public CreateCommandeDtoValidator(MigApiContext context)
        {
            RuleFor(x => x.ClientId)
            .Cascade(CascadeMode.Stop)
            .GreaterThan(0)
            .WithMessage("L'identifiant du client doit être supérieur à 0.");
           

            RuleFor(x => x.LignesCommande).Must(lignesCommande =>
                    lignesCommande.GroupBy(ligneCommande => ligneCommande.ProduitId)
                    .All(groupeProduit => groupeProduit.Count() == 1))
                    .WithMessage( "Un même produit ne peut apparaître qu'une seule fois dans une commande.")
                    .NotEmpty()
                    .WithMessage("Une commande doit contenir au moins une ligne de commande.");

            RuleForEach(x => x.LignesCommande)
                .ChildRules(ligne =>
                {
                    ligne.RuleFor(x => x.ProduitId).GreaterThan(0).WithMessage("L'identifiant du produit doit être supérieur à 0.");
                    ligne.RuleFor(x => x.Quantite).GreaterThan(0).WithMessage("La quantité doit être supérieure à 0.");
                });
        }
    }
}
