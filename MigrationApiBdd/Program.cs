using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.DAL;
using MigrationApiBdd.Data;
using MigrationApiBdd.Exception;
using MigrationApiBdd.Models.Context;
using MigrationApiBdd.Models.Identity;
using MigrationApiBdd.Models.Validation;
using MigrationApiBdd.Options;
using MigrationApiBdd.Services.Auth;
using MigrationApiBdd.Services.Classes;
using MigrationApiBdd.Services.Interfaces;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using MigrationApiBdd.Helpers;


var builder = WebApplication.CreateBuilder(args);


//recupere la chaine de connection a la base de données dans le fichier appsettings.json
string? connectionString = builder.Configuration.GetConnectionString("MigApiDbConnectionString");

builder.Services.AddMemoryCache();

// Add services to the container.
builder.Services.AddDbContext<MigApiContext>(option => option.UseSqlServer(connectionString ??
    throw new InvalidOperationException("Connection string 'MigApiDbConnectionString' not found.")));
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<MigApiContext>() // Use the MigApiContext for Identity
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<ICommandeService, CommandeService>();
builder.Services.AddScoped<IProduitService, ProduitService>();
builder.Services.AddScoped<IGestionService, GestionService>();
builder.Services.AddScoped<IClientRepository, ClientRepository>();
builder.Services.AddScoped<ICommandeRepository, CommandeRepository>();
builder.Services.AddScoped<IProduitRepository, ProduitRepository>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IOperationLogRepository, OperationLogRepository>();
builder.Services.AddScoped<IStockMouvementRepository, StockMouvementRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

// Register Mapster configuration
MapsterConfig.Register();

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<MigApiExceptionHandler>();

builder.Services.AddValidatorsFromAssemblyContaining<CreateCommandeDtoValidator>();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

// Add JWT authentication configuration 
builder.Services
    .AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services
    .AddOptions<RefreshTokenOptions>()
    .BindConfiguration(RefreshTokenOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()!;

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            JwtBearerDefaults.AuthenticationScheme;

        options.DefaultChallengeScheme =
            JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,

            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ClockSkew = TimeSpan.Zero,
            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };
    });
// Description OpenAPI (Scalar). Le transformateur déclare le schéma Bearer : Scalar propose alors
// un champ pour coller le jeton d'accès une fois pour toutes (voir BearerSecuritySchemeTransformer).
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

// Limiteur de débit sur l'inscription et la connexion (routes publiques sur Internet) :
// fenêtre fixe par adresse IP. Réglable sans recompiler :
//   RateLimiting:Auth:PermitLimit   (variable RateLimiting__Auth__PermitLimit, défaut 20)
//   RateLimiting:Auth:WindowSeconds (variable RateLimiting__Auth__WindowSeconds, défaut 60)
// L'adresse IP est celle du visiteur grâce à ASPNETCORE_FORWARDEDHEADERS_ENABLED (derrière le proxy).
var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:Auth:PermitLimit") ?? 20;
var authWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:Auth:WindowSeconds") ?? 60;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            }));
});


var frontendOrigin = builder.Configuration["Cors:FrontendOrigin"]
    ?? throw new InvalidOperationException(
        "Cors:FrontendOrigin est obligatoire.");

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(frontendOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();



// Seed the database with initial data if it is empty
using var scope = app.Services.CreateScope();
var services = scope.ServiceProvider;

// Application automatique des migrations : uniquement si ApplyMigrationsOnStartup=true
// (conteneur local). Absente ou false => le schéma est géré hors de l'application (test, prod).
if (builder.Configuration.GetValue<bool>("ApplyMigrationsOnStartup"))
{
    await services.GetRequiredService<MigApiContext>().Database.MigrateAsync();
}

await IdentitySeedData.SeedRolesAsync(services);
await IdentitySeedData.SeedAdminAsync(services, builder.Configuration);

// Données de démonstration : seulement si SeedDemoData=true (local) ET aucun client en base.
var context = services.GetRequiredService<MigApiContext>();

if (builder.Configuration.GetValue<bool>("SeedDemoData") && !await context.Clients.AnyAsync())
{
    await SeedData.SeedAsync(services);
}

// Documentation de l'API : toujours en développement, en production seulement si OpenApi:Enabled=true.
var openApiEnabled = app.Environment.IsDevelopment()
    || app.Configuration.GetValue<bool>("OpenApi:Enabled");

if (openApiEnabled)
{
    app.MapOpenApi();            // /openapi/v1.json
    app.MapScalarApiReference(); // /scalar/v1
}
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors("Frontend");

// Après le routage (implicite) : les politiques par route ([EnableRateLimiting]) lisent les métadonnées de l'endpoint.
app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Nécessaire pour WebApplicationFactory<Program> dans les tests d'intégration.
public partial class Program { }
