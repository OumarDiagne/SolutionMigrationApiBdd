using MigrationApiBdd.Exception;

namespace MigrationApiBdd.Helpers
{
    /// <summary>
    /// Décodage de la RowVersion envoyée par le client (corps de requête ou header If-Match).
    /// Retourne une erreur métier claire (400 / 428) au lieu d'une FormatException (500).
    /// </summary>
    public static class RowVersionHelper
    {
        public static byte[] Decoder(string? rowVersion)
        {
            if (string.IsNullOrWhiteSpace(rowVersion))
            {
                throw new BusinessRuleException(
                    "La version de ligne (RowVersion) est requise.",
                    StatusCodes.Status428PreconditionRequired);
            }

            try
            {
                // Accepte aussi la forme ETag entre guillemets : "AAAAAAAAB9E=".
                return Convert.FromBase64String(rowVersion.Trim().Trim('"'));
            }
            catch (FormatException)
            {
                throw new BusinessRuleException("La version de ligne (RowVersion) est invalide.");
            }
        }
    }
}
