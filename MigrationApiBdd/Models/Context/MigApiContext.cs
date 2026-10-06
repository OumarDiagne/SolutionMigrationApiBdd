using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Models.Auth;
using MigrationApiBdd.Models.Identity;

namespace MigrationApiBdd.Models.Context
{
    public class MigApiContext : IdentityDbContext<ApplicationUser, IdentityRole,string>
    {
        public MigApiContext(DbContextOptions<MigApiContext> options)
            : base(options)
        {

        }

        public virtual DbSet<Commandes> Commandes { get; set; }
        public virtual DbSet<LignesCommande> LignesCommandes { get; set; }
        public virtual DbSet<Produits> Produits { get; set; }
        public virtual DbSet<Clients> Clients { get; set; }
        public virtual DbSet<AuditLog> AuditLogs { get; set; }
        public virtual DbSet<OperationLog> OperationLogs { get; set; }
        public virtual DbSet<StockMouvement> StockMouvements { get; set; }
        public virtual DbSet<IdempotencyRecord> IdempotencyRecords { get; set; }
        public virtual DbSet<RefreshToken> RefreshTokens { get; set; }



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Call the base method to ensure IdentityDbContext configurations are applied
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("RefreshTokens");

                entity.HasKey(rt => rt.Id);

                entity.Property(rt => rt.ApplicationUserId)
                    .HasMaxLength(450)
                    .IsRequired();

                entity.Property(rt => rt.TokenHash)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(rt => rt.ReplacedByTokenHash)
                    .HasMaxLength(64);

                entity.Property(rt => rt.RevocationReason)
                    .HasMaxLength(100);

                entity.HasIndex(rt => rt.TokenHash)
                    .IsUnique();

                entity.HasIndex(rt => new
                {
                    rt.ApplicationUserId,
                    rt.ExpiresAtUtc
                });

                entity.HasOne(rt => rt.ApplicationUser)
                    .WithMany(user => user.RefreshTokens)
                    .HasForeignKey(rt => rt.ApplicationUserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Ignore(rt => rt.IsActive);
            });

            modelBuilder.Entity<Commandes>(entity =>
            {
                entity.HasKey(c => c.CommandeId);
                entity.Property(c => c.Statut).HasConversion<string>().IsRequired();
                entity.Property(c => c.CreatedByUserId).HasMaxLength(450);
                entity.Property(c => c.FacturePath);
                entity.Property(c => c.TotalCommandeTTC).HasColumnType("decimal(18,2)").IsRequired();
                entity.Property(c => c.DateCommande).HasColumnType("datetime2").IsRequired();
                entity.HasOne(c => c.ClientCommande)
                    .WithMany(cl => cl.Commandes)
                    .HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.SetNull);
                entity.Property(c => c.RowVersion)
                    .IsRowVersion();
            });
            modelBuilder.Entity<Clients>(entity =>
            {
                entity.HasKey(cl => cl.ClientId);
                entity.Property(cl => cl.Nom).IsRequired().HasMaxLength(50);
                entity.Property(cl => cl.Prenom).IsRequired().HasMaxLength(50);
                entity.Property(cl=>cl.IsActive).IsRequired().HasDefaultValue(true);
                entity.Property(cl => cl.RowVersion)
                  .IsRowVersion();
                entity.HasOne(c => c.ApplicationUser)
              .WithOne(u => u.Client)
              .HasForeignKey<Clients>(c => c.ApplicationUserId)
              .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(c => c.ApplicationUserId)
                    .IsUnique();
            });

            modelBuilder.Entity<LignesCommande>(entity =>
            {
                entity.HasKey(lc => lc.LigneCommandeId);
                entity.HasOne(lc => lc.Commande)
                    .WithMany(c => c.LignesCommande)
                    .HasForeignKey(lc => lc.CommandeId).IsRequired();
                entity.HasOne(lc => lc.Produit)
                    .WithMany(p => p.LignesCommande)
                    .HasForeignKey(lc => lc.ProduitId).IsRequired();
                entity.Property(lc => lc.Quantite).IsRequired();

                entity.Property(lc => lc.PrixUnitaireTTC)
                      .HasColumnType("decimal(18,2)").IsRequired();
                entity.HasIndex(lc => new { lc.CommandeId, lc.ProduitId }).IsUnique();
            });

            modelBuilder.Entity<Produits>(entity =>
            {
                entity.HasKey(p => p.ProduitId);
                entity.Property(p => p.NomProduit).IsRequired().HasMaxLength(50);
                entity.Property(p => p.Description).HasMaxLength(200);
                entity.Property(p => p.PrixUnitaireTTC).HasColumnType("decimal(18,2)").IsRequired();
                entity.Property(p => p.EstDisponible).HasDefaultValue(true).HasSentinel(true);
                entity.Property(p => p.Stock).IsRequired().HasDefaultValue(0);
                entity.Property(p => p.RowVersion)
                  .IsRowVersion();
            });
            modelBuilder.Entity<StockMouvement>(entity =>
            {
                entity.HasKey(sm => sm.StockMouvementId);
                entity.Property(sm => sm.ProduitId).IsRequired();
                entity.Property(sm => sm.Quantite).IsRequired();
                entity.Property(sm => sm.CreatedAtUtc).HasColumnType("datetime2").IsRequired();
                entity.Property(sm => sm.TypeMouvement).IsRequired();
                entity.HasOne(sm => sm.Produit)
                      .WithMany(p => p.StockMouvements)
                      .HasForeignKey(sm => sm.ProduitId).IsRequired().OnDelete(DeleteBehavior.NoAction);
                entity.HasIndex(sm => sm.ProduitId);
            });

            modelBuilder.Entity<IdempotencyRecord>(entity =>
            {
                entity.ToTable("IdempotencyRecords");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Scope)
                    .HasMaxLength(100)
                    .IsRequired();

                entity.Property(x => x.IdempotencyKey)
                    .HasMaxLength(128)
                    .IsRequired();

                entity.Property(x => x.RequestHash)
                    .HasMaxLength(64)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .HasMaxLength(20)
                    .IsRequired();
                entity.Property(e => e.CreatedAtUtc)
                    .HasDefaultValueSql("SYSUTCDATETIME()");

                entity.HasIndex(x => new { x.Scope, x.IdempotencyKey })
                    .IsUnique();

                entity.HasIndex(x => x.ExpiresAtUtc);
            });
        }
    }
}
