namespace MigrationApiBdd.Tests.Support;

/// <summary>
/// Mapster utilise une configuration globale statique : on l'initialise une seule fois
/// pour toute la suite de tests (xUnit exécute les classes de tests en parallèle).
/// </summary>
internal static class TestMapster
{
    private static readonly Lazy<bool> _init = new(() =>
    {
        MapsterConfig.Register();
        return true;
    });

    public static void Init() => _ = _init.Value;
}
