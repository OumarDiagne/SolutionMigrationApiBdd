using System.Net;
using Microsoft.EntityFrameworkCore;
using MigrationApiBdd.Dtos;
using Xunit;

namespace MigrationApiBdd.Tests.Integration;

[Collection(ApiCollection.Nom)]
public class ClientEndpointsTests
{
    private readonly ApiFactory _factory;

    public ClientEndpointsTests(ApiFactory factory) => _factory = factory;

    private static UpdateClientDto Modification(string rowVersion, string nom = "Nouveau") => new()
    {
        Nom = nom,
        Prenom = "Prenom",
        RowVersion = rowVersion
    };

    // ---------- Liste : administrateur uniquement ----------

    [Fact]
    public async Task GetListe_SansToken_Retourne401()
    {
        var reponse = await _factory.CreerClient().GetAsync("/api/Client");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task GetListe_UtilisateurStandard_Retourne403()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.GetAsync("/api/Client");

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task GetListe_Administrateur_Retourne200()
    {
        var admin = await _factory.CreerAdminAsync();

        var reponse = await admin.GetAsync("/api/Client");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.NotEmpty(await reponse.LireAsync<List<ClientDto>>());
    }

    // ---------- Création : administrateur uniquement ----------

    [Fact]
    public async Task Create_UtilisateurStandard_Retourne403()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.PostJsonAsync("/api/Client", new CreateClientDto { Nom = "A", Prenom = "B" });

        Assert.Equal(HttpStatusCode.Forbidden, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_SansToken_Retourne401()
    {
        var reponse = await _factory.CreerClient().PostJsonAsync("/api/Client", new CreateClientDto { Nom = "A", Prenom = "B" });

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_Administrateur_Retourne201()
    {
        var admin = await _factory.CreerAdminAsync();

        var reponse = await admin.PostJsonAsync("/api/Client", new CreateClientDto { Nom = "Durand", Prenom = "Zoe" });

        Assert.Equal(HttpStatusCode.Created, reponse.StatusCode);
        var client = await reponse.LireAsync<ClientDto>();
        Assert.True(client.ClientId > 0);
        Assert.Equal("Durand", client.Nom);
    }

    // ---------- Mon client (relation 0..1 avec le compte) ----------

    [Fact]
    public async Task Me_SansToken_Retourne401()
    {
        var reponse = await _factory.CreerClient().GetAsync("/api/Client/me");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task Me_UtilisateurInscrit_RetourneSonClient()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();

        var reponse = await utilisateur.Client.GetAsync("/api/Client/me");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal(utilisateur.ClientId, (await reponse.LireAsync<ClientDto>()).ClientId);
    }

    [Fact]
    public async Task Me_Administrateur_Retourne404CarIlNAPasDeClient()
    {
        var admin = await _factory.CreerAdminAsync();

        var reponse = await admin.GetAsync("/api/Client/me");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task Create_PlusieursClientsSansCompte_Coexistent()
    {
        // L'index unique sur ApplicationUserId ignore les NULL : un client sans compte reste possible, en nombre illimité.
        var admin = await _factory.CreerAdminAsync();

        var premier = await admin.PostJsonAsync("/api/Client", new CreateClientDto { Nom = "SansCompte", Prenom = "Un" });
        var second = await admin.PostJsonAsync("/api/Client", new CreateClientDto { Nom = "SansCompte", Prenom = "Deux" });

        Assert.Equal(HttpStatusCode.Created, premier.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task ClientSansCompte_ResteLisibleEtModifiableParLAdministrateur()
    {
        var admin = await _factory.CreerAdminAsync();
        var creation = await admin.PostJsonAsync("/api/Client", new CreateClientDto { Nom = "Telephone", Prenom = "Client" });
        var client = await creation.LireAsync<ClientDto>();

        var lu = await admin.GetAsync($"/api/Client/{client.ClientId}");
        var commandes = await admin.GetAsync($"/api/Client/{client.ClientId}/commandes");

        Assert.Equal(HttpStatusCode.OK, lu.StatusCode);
        Assert.Equal(HttpStatusCode.OK, commandes.StatusCode);
    }

    // ---------- Lecture d'un client ----------

    [Fact]
    public async Task GetParId_SansToken_Retourne401()
    {
        var reponse = await _factory.CreerClient().GetAsync("/api/Client/1");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }

    [Fact]
    public async Task GetParId_SonPropreClient_Retourne200()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;

        var client = await utilisateur.Client.LireClientAsync(clientId);

        Assert.Equal(clientId, client.ClientId);
    }

    [Fact]
    public async Task GetParId_ClientDUnAutreUtilisateur_Retourne404()
    {
        var proprietaire = await _factory.CreerUtilisateurAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var clientId = proprietaire.ClientId;

        var reponse = await intrus.Client.GetAsync($"/api/Client/{clientId}");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task GetParId_ClientNonRelieAUnCompte_Retourne404PourUnUtilisateurEt200PourLAdmin()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var admin = await _factory.CreerAdminAsync();
        var clientId = await _factory.UnClientIdAsync(); // client du jeu de données initial, sans compte

        var pourUtilisateur = await utilisateur.Client.GetAsync($"/api/Client/{clientId}");
        var pourAdmin = await admin.GetAsync($"/api/Client/{clientId}");

        Assert.Equal(HttpStatusCode.NotFound, pourUtilisateur.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pourAdmin.StatusCode);
    }

    [Fact]
    public async Task GetCommandesDuClient_ClientDUnAutreUtilisateur_Retourne404()
    {
        var proprietaire = await _factory.CreerUtilisateurAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var clientId = proprietaire.ClientId;

        var reponse = await intrus.Client.GetAsync($"/api/Client/{clientId}/commandes");

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
    }

    [Fact]
    public async Task GetCommandesDuClient_SonPropreClient_Retourne200()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;

        var reponse = await utilisateur.Client.GetAsync($"/api/Client/{clientId}/commandes");

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    // ---------- Modification ----------

    [Fact]
    public async Task Put_SonPropreClient_Retourne200EtModifieLesInformations()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;
        var lu = await utilisateur.Client.LireClientAsync(clientId);

        var reponse = await utilisateur.Client.PutJsonAsync($"/api/Client/{clientId}", Modification(lu.RowVersion, "Martin"));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
        Assert.Equal("Martin", (await reponse.LireAsync<ClientDto>()).Nom);
    }

    [Fact]
    public async Task Put_ClientDUnAutreUtilisateur_Retourne404EtNeModifieRien()
    {
        var proprietaire = await _factory.CreerUtilisateurAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var clientId = proprietaire.ClientId;
        var lu = await proprietaire.Client.LireClientAsync(clientId);

        var reponse = await intrus.Client.PutJsonAsync($"/api/Client/{clientId}", Modification(lu.RowVersion, "Pirate"));

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        Assert.Equal(ApiTestHelpers.NomParDefaut, (await proprietaire.Client.LireClientAsync(clientId)).Nom);
    }

    [Fact]
    public async Task Put_Administrateur_PeutModifierUnClientNonRelie()
    {
        var admin = await _factory.CreerAdminAsync();
        var creation = await admin.PostJsonAsync("/api/Client", new CreateClientDto { Nom = "Avant", Prenom = "Admin" });
        var client = await creation.LireAsync<ClientDto>();
        var lu = await admin.LireClientAsync(client.ClientId);

        var reponse = await admin.PutJsonAsync($"/api/Client/{client.ClientId}", Modification(lu.RowVersion, "Apres"));

        Assert.Equal(HttpStatusCode.OK, reponse.StatusCode);
    }

    [Fact]
    public async Task Put_RowVersionPerimee_Retourne409()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;
        var versionInitiale = (await utilisateur.Client.LireClientAsync(clientId)).RowVersion;
        var premiere = await utilisateur.Client.PutJsonAsync($"/api/Client/{clientId}", Modification(versionInitiale, "Un"));
        Assert.Equal(HttpStatusCode.OK, premiere.StatusCode);

        var perimee = await utilisateur.Client.PutJsonAsync($"/api/Client/{clientId}", Modification(versionInitiale, "Deux"));

        Assert.Equal(HttpStatusCode.Conflict, perimee.StatusCode);
    }

    [Fact]
    public async Task Put_RowVersionInvalide_Retourne400()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;

        var reponse = await utilisateur.Client.PutJsonAsync($"/api/Client/{clientId}", Modification("pas-du-base64!"));

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    // ---------- Suppression (désactivation) ----------

    [Fact]
    public async Task Delete_SonPropreClient_Retourne204EtDesactiveLeClient()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;
        var lu = await utilisateur.Client.LireClientAsync(clientId);

        var reponse = await utilisateur.Client.SupprimerAsync($"/api/Client/{clientId}", lu.RowVersion);

        Assert.Equal(HttpStatusCode.NoContent, reponse.StatusCode);
        var actif = await _factory.DansLaBaseAsync(db =>
            db.Clients.Where(c => c.ClientId == clientId).Select(c => c.IsActive).SingleAsync());
        Assert.False(actif);
    }

    [Fact]
    public async Task Delete_ClientDUnAutreUtilisateur_Retourne404EtNeDesactiveRien()
    {
        var proprietaire = await _factory.CreerUtilisateurAsync();
        var intrus = await _factory.CreerUtilisateurAsync();
        var clientId = proprietaire.ClientId;
        var lu = await proprietaire.Client.LireClientAsync(clientId);

        var reponse = await intrus.Client.SupprimerAsync($"/api/Client/{clientId}", lu.RowVersion);

        Assert.Equal(HttpStatusCode.NotFound, reponse.StatusCode);
        var actif = await _factory.DansLaBaseAsync(db =>
            db.Clients.Where(c => c.ClientId == clientId).Select(c => c.IsActive).SingleAsync());
        Assert.True(actif);
    }

    [Fact]
    public async Task Delete_SansIfMatch_Retourne400()
    {
        var utilisateur = await _factory.CreerUtilisateurAsync();
        var clientId = utilisateur.ClientId;

        var reponse = await utilisateur.Client.SupprimerAsync($"/api/Client/{clientId}", ifMatch: null);

        Assert.Equal(HttpStatusCode.BadRequest, reponse.StatusCode);
    }

    [Fact]
    public async Task Delete_SansToken_Retourne401()
    {
        var reponse = await _factory.CreerClient().SupprimerAsync("/api/Client/1", ifMatch: "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, reponse.StatusCode);
    }
}
