using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MigrationApiBdd.Helpers
{
    /// <summary>
    /// Déclare le schéma de sécurité « Bearer » (JWT) dans la description OpenAPI.
    /// Sans cela, Scalar ne propose aucun champ pour coller le jeton d'accès : le visiteur
    /// devrait ajouter l'en-tête Authorization à la main dans chaque requête.
    /// Les routes /api/Auth (inscription, connexion, refresh, déconnexion) sont anonymes :
    /// elles ne reçoivent pas l'exigence d'authentification.
    /// </summary>
    internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider)
        : IOpenApiDocumentTransformer
    {
        private const string SchemeName = "Bearer";

        public async Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();
            if (!authenticationSchemes.Any(authScheme => authScheme.Name == SchemeName))
            {
                return;
            }

            var securitySchemes = new Dictionary<string, IOpenApiSecurityScheme>
            {
                [SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer", // "bearer" désigne le type d'autorisation dans l'en-tête Authorization
                    In = ParameterLocation.Header,
                    BearerFormat = "Json Web Token"
                }
            };
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes = securitySchemes;

            var operationsProtegees = document.Paths
                .Where(path => !path.Key.StartsWith("/api/Auth", StringComparison.OrdinalIgnoreCase))
                .SelectMany(path => path.Value.Operations);

            foreach (var operation in operationsProtegees)
            {
                operation.Value.Security ??= [];
                operation.Value.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeName, document)] = []
                });
            }
        }
    }
}
