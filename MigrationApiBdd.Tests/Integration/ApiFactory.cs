using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MigrationApiBdd.Models.Context;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

/// <summary>
/// Démarre l'API complète en mémoire (vrai pipeline HTTP, vraie authentification JWT, vrai EF Core)
/// sur une base SQL Server TEMPORAIRE créée pour la série de tests puis supprimée.
/// Ta base de développement (MigApiDb) n'est jamais utilisée.
///
/// Connexion : variable d'environnement MIGAPI_TEST_CONNECTION si elle existe (utile pour la CI),
/// sinon SQL Server LocalDB. Seul le nom de base est remplacé.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@tests.local";
    public const string AdminPassword = "AdminTest!123";

    private const string ConnexionParDefaut =
        @"Server=(localdb)\mssqllocaldb;Database=master;Trusted_Connection=true;TrustServerCertificate=true;";

    private readonly string _connectionString;

    public ApiFactory()
    {
        var connexionDeBase = Environment.GetEnvironmentVariable("MIGAPI_TEST_CONNECTION") ?? ConnexionParDefaut;

        var builder = new SqlConnectionStringBuilder(connexionDeBase)
        {
            InitialCatalog = $"MigApiTests_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;

        // Program.cs lit la configuration dès WebApplication.CreateBuilder : les variables
        // d'environnement sont prises en compte à coup sûr et priment sur appsettings et user-secrets.
        Environment.SetEnvironmentVariable("ConnectionStrings__MigApiDbConnectionString", _connectionString);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", "cle-de-signature-uniquement-pour-les-tests-1234567890");
        Environment.SetEnvironmentVariable("SeedAdmin__Email", AdminEmail);
        Environment.SetEnvironmentVariable("SeedAdmin__Password", AdminPassword);
        // Les tests créent des dizaines de comptes depuis la même « adresse » : on neutralise le limiteur de débit des routes publiques.
        Environment.SetEnvironmentVariable("RateLimiting__Auth__PermitLimit", "100000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    /// <summary>Crée la base et applique les migrations avant le démarrage de l'API.</summary>
    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<MigApiContext>().UseSqlServer(_connectionString).Options;
        await using var context = new MigApiContext(options);
        await context.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();

        var options = new DbContextOptionsBuilder<MigApiContext>().UseSqlServer(_connectionString).Options;
        await using var context = new MigApiContext(options);
        await context.Database.EnsureDeletedAsync();
    }

    /// <summary>Exécute une lecture directement en base (pour vérifier la journalisation).</summary>
    public async Task<T> DansLaBaseAsync<T>(Func<MigApiContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MigApiContext>();
        return await action(context);
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Nom = "Api";
}
