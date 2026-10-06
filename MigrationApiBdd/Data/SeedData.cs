using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Models;
using MigrationApiBdd.Models.Context;

namespace MigrationApiBdd.Data
{
    public static class SeedData
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MigApiContext>();

            await db.Database.MigrateAsync();

            if (await db.Clients.AnyAsync())
                return;

            var clients = new List<Clients>
        {
            new() { Nom = "Dupont", Prenom = "Alice" },
            new() { Nom = "Martin", Prenom = "Bob" },
            new() { Nom = "Bernard", Prenom = "Claire" },
            new() { Nom = "Petit", Prenom = "David" },
            new() { Nom = "Robert", Prenom = "Emma" },
            new() { Nom = "Richard", Prenom = "Thomas" },
            new() { Nom = "Durand", Prenom = "Ines" },
            new() { Nom = "Dubois", Prenom = "Lucas" },
            new() { Nom = "Moreau", Prenom = "Lina" },
            new() { Nom = "Laurent", Prenom = "Hugo" },
            new() { Nom = "Simon", Prenom = "Chloe" },
            new() { Nom = "Michel", Prenom = "Nathan" },
            new() { Nom = "Lefevre", Prenom = "Sara" },
            new() { Nom = "Legrand", Prenom = "Louis" },
            new() { Nom = "Roux", Prenom = "Julie" },
            new() { Nom = "Vincent", Prenom = "Maxime" },
            new() { Nom = "Fournier", Prenom = "Manon" },
            new() { Nom = "Girard", Prenom = "Paul" },
            new() { Nom = "Bonnet", Prenom = "Marine" },
            new() { Nom = "Dupuis", Prenom = "Antoine" },
            new() { Nom = "Lambert", Prenom = "Eva" },
            new() { Nom = "Fontaine", Prenom = "Jules" },
            new() { Nom = "Rousseau", Prenom = "Nina" },
            new() { Nom = "Blanc", Prenom = "Leo" },
            new() { Nom = "Guerin", Prenom = "Zoe" }
        };



            var produits = new List<Produits>
        {
            new() { NomProduit = "Clavier mécanique", Description = "Clavier filaire RGB", PrixUnitaireTTC = 89.99m, Stock = 40, EstDisponible = true },
            new() { NomProduit = "Ecran 27 pouces", Description = "Moniteur IPS QHD", PrixUnitaireTTC = 249.99m, Stock = 12, EstDisponible = true },
            new() { NomProduit = "Cable HDMI", Description = "Cable HDMI 2m", PrixUnitaireTTC = 9.99m, Stock = 100, EstDisponible = true },
            new() { NomProduit = "Souris sans fil", Description = "Souris ergonomique", PrixUnitaireTTC = 29.99m, Stock = 55, EstDisponible = true },
            new() { NomProduit = "Casque audio", Description = "Casque bluetooth", PrixUnitaireTTC = 59.99m, Stock = 25, EstDisponible = true },
            new() { NomProduit = "Webcam", Description = "Webcam Full HD", PrixUnitaireTTC = 39.99m, Stock = 18, EstDisponible = true },
            new() { NomProduit = "Dock USB-C", Description = "Station d’accueil 8 ports", PrixUnitaireTTC = 79.99m, Stock = 14, EstDisponible = true },
            new() { NomProduit = "SSD 1To", Description = "Disque SSD NVMe", PrixUnitaireTTC = 109.99m, Stock = 20, EstDisponible = true },
            new() { NomProduit = "Chargeur laptop", Description = "Chargeur universel", PrixUnitaireTTC = 49.99m, Stock = 30, EstDisponible = true },
            new() { NomProduit = "Support ecran", Description = "Bras articulé", PrixUnitaireTTC = 69.99m, Stock = 16, EstDisponible = true }
        };
            // Les clients et produits n'existent pas encore en base.
            // On les ajoute tous au ChangeTracker d'EF Core.
            db.Clients.AddRange(clients);
            db.Produits.AddRange(produits);

            // Un seul enregistrement pour insérer les clients et les produits.
            // Après cet appel, SQL Server a généré les ClientId et ProduitId.
            await db.SaveChangesAsync();


            // Crée un générateur pseudo-aléatoire avec une "graine" fixe.
            var rng = new Random(42);

            // Date UTC sans l'heure : par exemple 2026-08-16 00:00:00 UTC.
            // Cela facilite la génération de dates de commandes cohérentes.
            var now = DateTime.UtcNow.Date;


            // Ces listes contiennent toutes les données qui seront ajoutées
            // lors du SaveChangesAsync final.
            var commandes = new List<Commandes>();
            var lignes = new List<LignesCommande>();
            var stockMovements = new List<StockMouvement>();
            var auditLogs = new List<AuditLog>();
            var operationLogs = new List<OperationLog>();


            // Génère 50 commandes fictives.
            for (var i = 1; i <= 50; i++)
            {
                // Sélectionne un client existant au hasard.
                var client = clients[rng.Next(clients.Count)];

                // Génère une date entre hier et il y a 89 jours.
                var date = now.AddDays(-rng.Next(1, 90));

                // Génère une valeur 0, 1 ou 2, convertie en StatutCommande.
                // Cela suppose que l'enum possède bien ces trois valeurs.
                var statut = (StatutCommande)rng.Next(0, 3);

                // Chaque commande contient entre 1 et 3 lignes.
                var nbLignes = rng.Next(1, 4);


                // Création de la commande en mémoire seulement.
                // Aucun INSERT SQL n'est envoyé ici.


                var commande = new Commandes
                {
                    DateCommande = date,
                    Statut = statut,

                    // Le client est déjà sauvegardé ; son ClientId est donc connu.
                    ClientId = client.ClientId,

                    // Le total sera recalculé après la création des lignes.
                    TotalCommandeTTC = 0m
                };

                // La commande sera ajoutée au DbContext plus bas, en une seule fois.
                commandes.Add(commande);

                decimal total = 0m;


                // Évite de sélectionner un produit dont le stock est déjà à zéro.
                // OrderBy avec un nombre aléatoire mélange la liste.
                // Take limite ensuite le résultat au nombre de lignes souhaité.
                var produitsSelectionnes = produits
                    .Where(p => p.Stock > 0)
                    .OrderBy(_ => rng.Next())
                    .Take(nbLignes)
                    .ToList();


                foreach (var produit in produitsSelectionnes)
                {
                    // Quantité aléatoire : 1, 2 ou 3.
                    // Math.Min garantit que l'on ne vend jamais davantage
                    // que le stock réellement disponible.
                    var quantite = Math.Min(rng.Next(1, 4), produit.Stock);

                    // Sécurité : ne crée pas de ligne vide ou invalide.
                    if (quantite <= 0)
                        continue;


                    // Création de la ligne de commande.
                    // Le point important est : Commande = commande.
                    //
                    // La commande n'a pas encore de CommandeId SQL définitif.
                    // EF Core va néanmoins mémoriser le lien entre cette ligne
                    // et l'objet "commande" en mémoire.
                    //
                    // Lors du SaveChangesAsync final, EF Core :
                    // 1. Insère la commande.
                    // 2. Récupère son CommandeId généré par SQL Server.
                    // 3. Insère la ligne avec ce CommandeId.
                    lignes.Add(new LignesCommande
                    {
                        Commande = commande,
                        ProduitId = produit.ProduitId,
                        Quantite = quantite,
                        PrixUnitaireTTC = produit.PrixUnitaireTTC
                    });


                    // On garde l'ancien stock pour assurer la traçabilité.
                    var stockAvant = produit.Stock;

                    // Simule la sortie de stock provoquée par cette vente.
                    produit.Stock -= quantite;

                    // Un produit est disponible seulement si son stock reste positif.
                    produit.EstDisponible = produit.Stock > 0;


                    // Trace métier du mouvement physique/logique de stock.
                    stockMovements.Add(new StockMouvement
                    {
                        ProduitId = produit.ProduitId,
                        TypeMouvement = TypeMouvementStock.Sortie,
                        Quantite = quantite,
                        StockAvant = stockAvant,
                        StockApres = produit.Stock,

                        // Ici, évite CommandeId car il n'existe pas encore réellement.
                        // On garde une description compréhensible sans ID définitif.
                        Motif = $"Commande seedée pour le client {client.ClientId}",

                        SourceOperation = "SeedCommande",
                        CreatedAtUtc = DateTime.UtcNow
                    });


                    // Calcul du montant total TTC de la commande.
                    total += produit.PrixUnitaireTTC * quantite;
                    // La commande est toujours suivie en mémoire.
                    // EF Core détectera que TotalCommandeTTC doit être inséré avec cette valeur.
                    commande.TotalCommandeTTC = total;
                }

            }
            
                // Ajoute toutes les commandes au suivi EF Core.
                // Il n'y a pas encore de requête SQL à ce moment-là.
                db.Commandes.AddRange(commandes);

                // Ajoute toutes les lignes de commandes.
                db.LignesCommandes.AddRange(lignes);

                // Ajoute tous les mouvements de stock.
                db.StockMouvements.AddRange(stockMovements);

                await db.SaveChangesAsync();




      

            foreach (var commande in commandes)
            {
                var clientId = commande.ClientId;
                var correlationId = Guid.NewGuid().ToString();

                auditLogs.Add(new AuditLog
                {
                    EntityName = nameof(Commandes),

                    // Conforme à ton modèle Required et à la sémantique attendue.
                    EntityId = commande.CommandeId.ToString(),

                    ActionType = "INSERT",
                    OldValue = null,
                    NewValue = $"Commande seedée pour le client {clientId}",
                    ChangedBy = "Seeder",
                    ChangedAtUtc = DateTime.UtcNow,
                    CorrelationId = correlationId,
                    Reason = "Initial seed"
                });

                operationLogs.Add(new OperationLog
                {
                    OperationName = "SeedCommande",
                    Level = "Information",
                    Message = $"Commande {commande.CommandeId} créée pour le client {clientId}",
                    Exception = null,
                    ExecutedAtUtc = DateTime.UtcNow,
                    DurationMs = rng.Next(5, 20),
                    CorrelationId = correlationId
                });

            }


            // Journal technique / applicatif.
            // Cette entrée ne représente pas une modification d'une entité métier.
            // Elle décrit l'exécution d'une opération dans l'application.
            db.OperationLogs.AddRange(operationLogs);

            // Audit : trace une modification de donnée métier.
            // Ici, l'entité auditée est Commandes.
            db.AuditLogs.AddRange(auditLogs);
            await db.SaveChangesAsync();
         
            await db.SaveChangesAsync();

        }
    }

}
