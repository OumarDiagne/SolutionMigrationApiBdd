# MigAgiBdd : API de gestion de commandes et de stock

![CI](https://github.com/OumarDiagne/SolutionMigrationApiBdd/actions/workflows/ci.yml/badge.svg)

API REST en **ASP.NET Core (.NET 10)** avec **EF Core** et **SQL Server** : clients, produits, commandes, gestion du stock, journalisation et authentification JWT.
Projet personnel conçu pour être maîtrisé de bout en bout : API, base de données, tests, intégration continue, puis déploiement (voir la feuille de route).

## Ce que fait l'API

| Domaine | Fonctionnalités |
|---|---|
| **Authentification** | Inscription (compte + client métier créés ensemble), connexion, JWT d'accès de courte durée, refresh token avec rotation, déconnexion |
| **Produits** | Catalogue, création, modification, archivage ; cache mémoire invalidé à chaque changement |
| **Commandes** | Création, modification, archivage ; calcul du total, contrôle de disponibilité et décrément du stock |
| **Clients** | Lecture, modification, désactivation ; un utilisateur ne gère que son propre client |
| **Stock** | Réapprovisionnement, mouvements de stock tracés (entrée / sortie) |
| **Traçabilité** | Journal d'audit (ancienne et nouvelle valeur), mouvements de stock, journal d'opérations, identifiant de corrélation |

## Choix techniques

- **Architecture en couches** : Contrôleurs → Services (règles métier) → Repositories (accès aux données) → EF Core / SQL Server.
- **Concurrence optimiste** : colonne `RowVersion` sur les clients, produits et commandes. Le client renvoie la version lue (corps de requête ou en-tête `If-Match`) ; une version périmée donne une **409**, une version absente une **428**, une version invalide une **400**.
- **Idempotence** : l'en-tête `Idempotency-Key` est obligatoire à la création d'un produit ou d'une commande. Le même appel rejoué renvoie la même réponse sans doublon ni double décrément du stock ; la même clé avec un contenu différent donne une **422**. La création s'exécute dans une transaction.
- **Sécurité** :
  - JWT d'accès de 15 minutes, refresh token en cookie `HttpOnly`, `Secure`, `SameSite=Strict`, stocké **haché** (SHA-256) en base.
  - Rotation du refresh token à chaque utilisation ; la réutilisation d'un ancien token révoque tous les tokens de l'utilisateur.
  - Contrôle d'accès par rôle (`Admin`, `User`) et par propriétaire : la ressource d'un autre utilisateur répond **404**, comme si elle n'existait pas (pas de fuite d'existence).
- **Erreurs homogènes** : gestionnaire global qui renvoie des `ProblemDetails` (RFC 7807).
- **Modèle de données** : un client métier peut exister sans compte (relation 0..1 avec l'utilisateur Identity, clé étrangère nullable avec index unique filtré). Un compte `User` possède toujours un client, créé à l'inscription ; un compte `Admin` n'en a pas.
- **Bibliothèques** : FluentValidation, Mapster, ASP.NET Core Identity, JwtBearer.

## Droits d'accès

| Ressource | Anonyme | `User` | `Admin` |
|---|---|---|---|
| Inscription, connexion, refresh, déconnexion | oui | oui | oui |
| Produits : lecture | non | oui | oui |
| Produits : création, modification, archivage | non | non | oui |
| Stock : réapprovisionnement | non | non | oui |
| Commandes | non | les siennes (pour son client) | toutes, pour n'importe quel client |
| Clients : liste, création | non | non | oui |
| Clients : lecture, modification, désactivation | non | le sien (`/api/client/me`) | tous |

## Lancer le projet

Prérequis : SDK .NET 10 et SQL Server (LocalDB suffit sous Windows).

```bash
cd MigrationApiBdd

# Secrets de développement (jamais dans le dépôt)
dotnet user-secrets set "Jwt:SigningKey" "<une clé d'au moins 32 caractères>"
dotnet user-secrets set "SeedAdmin:Email" "admin@example.com"
dotnet user-secrets set "SeedAdmin:Password" "<mot de passe respectant la politique Identity>"

# Création de la base (chaîne de connexion dans appsettings.json, LocalDB par défaut)
dotnet ef database update

dotnet run
```

Au démarrage, l'API crée les rôles `Admin` et `User` ainsi que le compte administrateur. Le jeu de données de démonstration (clients) n'est chargé que si `SeedDemoData` vaut `true` **et** que la base ne contient aucun client.
En développement, la description OpenAPI est exposée par l'API.

### Configuration par environnement

| Clé (variable d'environnement) | Rôle | Local | Serveur de test / production |
|---|---|---|---|
| `Jwt:SigningKey` (`Jwt__SigningKey`) | Clé de signature des JWT | user-secrets | variable d'environnement ou coffre de secrets |
| `SeedAdmin:Email`, `SeedAdmin:Password` (`SeedAdmin__Email`, `SeedAdmin__Password`) | Compte administrateur créé au démarrage | user-secrets | variables d'environnement |
| `SeedDemoData` | Charge des clients de démonstration | `true` (défini dans `launchSettings.json`) | non définie ou `false` |

Le compte administrateur est créé **une seule fois** : si l'e-mail existe déjà, le mot de passe n'est pas relu, donc modifier `SeedAdmin:Password` plus tard ne change pas le mot de passe en base. L'application refuse de démarrer si l'e-mail ou le mot de passe est absent. Aucun de ces secrets ne doit figurer dans `appsettings.json` ni dans le dépôt.

## Tests

La solution contient **plus de 200 tests** (xUnit, Moq) :

- **Tests unitaires** : services (`StockService`, `CommandeService`, `AuthService`), cache, hash, décodage de la `RowVersion`.
- **Tests d'intégration** (`WebApplicationFactory`) : l'API complète démarre en mémoire avec le vrai pipeline HTTP, le vrai JWT et une **vraie base SQL Server temporaire**, créée puis supprimée à la fin. Ils couvrent l'authentification et la rotation des tokens, les droits d'accès, l'idempotence, la concurrence (409) et la journalisation en base.

```bash
dotnet test
```

Par défaut, les tests d'intégration utilisent SQL Server LocalDB. La variable d'environnement `MIGAPI_TEST_CONNECTION` permet d'indiquer un autre serveur ; seul le nom de la base est remplacé, et la base de développement n'est jamais utilisée.

### Tests fonctionnels (Talend API Tester)

Six scénarios (authentification, produits, commandes et stock, isolation entre utilisateurs, clients, stock) rejouent l'API déployée sur le serveur de test. Ils utilisent un environnement Talend avec `adminEmail`, `adminPassword` et `runId` (à renseigner soi-même ; aucun secret n'est stocké dans le fichier de scénarios). **Avant chaque nouveau lancement d'un scénario déjà exécuté, changer la valeur de `runId`** (par exemple `r1`, `r2`, `r3`…) dans l'environnement Talend : les comptes créés à l'inscription et les clés d'idempotence en sont dérivés, donc rejouer avec la même valeur provoque des conflits (compte déjà existant, clé déjà utilisée) qui font échouer le scénario sans que l'API soit en cause.

## Intégration continue

Le workflow GitHub Actions (`.github/workflows/ci.yml`) compile la solution et lance tous les tests à chaque push et à chaque pull request, avec un conteneur SQL Server pour les tests d'intégration.

## Limites connues et feuille de route

- Une mise à jour (`PUT`) avec une `RowVersion` périmée mais un contenu identique à l'état courant renvoie 200 : rien n'est écrit, donc aucun conflit n'est détecté.
- Cache en mémoire du processus : un cache distribué (Redis) sera nécessaire avec plusieurs instances.
- Étapes prévues : déploiement sur un serveur de test, puis conteneurisation (Docker) et migration vers Azure (Azure SQL, Key Vault, Managed Identity, Application Insights), puis Kubernetes (AKS).

## Auteur

Oumar Diagne, développeur full-stack C# / ASP.NET Core / Angular, certifié Microsoft Azure Developer Associate (AZ-204).
