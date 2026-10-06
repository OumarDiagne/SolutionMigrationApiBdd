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

Au démarrage, l'API crée les rôles et le compte administrateur, puis charge un jeu de données de démonstration si la base est vide.
En développement, la description OpenAPI est exposée par l'API.

## Tests

La solution contient **plus de 200 tests** (xUnit, Moq) :

- **Tests unitaires** : services (`StockService`, `CommandeService`, `AuthService`), cache, hash, décodage de la `RowVersion`.
- **Tests d'intégration** (`WebApplicationFactory`) : l'API complète démarre en mémoire avec le vrai pipeline HTTP, le vrai JWT et une **vraie base SQL Server temporaire**, créée puis supprimée à la fin. Ils couvrent l'authentification et la rotation des tokens, les droits d'accès, l'idempotence, la concurrence (409) et la journalisation en base.

```bash
dotnet test
```

Par défaut, les tests d'intégration utilisent SQL Server LocalDB. La variable d'environnement `MIGAPI_TEST_CONNECTION` permet d'indiquer un autre serveur ; seul le nom de la base est remplacé, et la base de développement n'est jamais utilisée.

## Intégration continue

Le workflow GitHub Actions (`.github/workflows/ci.yml`) compile la solution et lance tous les tests à chaque push et à chaque pull request, avec un conteneur SQL Server pour les tests d'intégration.

## Limites connues et feuille de route

- Le décrément du stock se fait en mémoire puis s'appuie sur la `RowVersion` pour détecter les conflits ; il sera remplacé par une mise à jour atomique en base (`ExecuteUpdateAsync`).
- Cache en mémoire du processus : un cache distribué (Redis) sera nécessaire avec plusieurs instances.
- Étapes prévues : déploiement sur un serveur de test, puis conteneurisation (Docker) et migration vers Azure (Azure SQL, Key Vault, Managed Identity, Application Insights), puis Kubernetes (AKS).

## Auteur

Oumar Diagne, développeur full-stack C# / ASP.NET Core / Angular, certifié Microsoft Azure Developer Associate (AZ-204).
