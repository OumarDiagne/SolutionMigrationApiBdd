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
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


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
await IdentitySeedData.SeedRolesAsync(services);
await IdentitySeedData.SeedAdminAsync(services, builder.Configuration);

// Données de démonstration : seulement si SeedDemoData=true (local) ET aucun client en base.
var context = services.GetRequiredService<MigApiContext>();

if (builder.Configuration.GetValue<bool>("SeedDemoData") && !await context.Clients.AnyAsync())
{
    await SeedData.SeedAsync(services);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseCors("Frontend");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Nécessaire pour WebApplicationFactory<Program> dans les tests d'intégration.
public partial class Program { }
