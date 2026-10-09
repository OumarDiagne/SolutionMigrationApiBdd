# MigAgiBdd : API de gestion de commandes et de stock

![CI](https://github.com/OumarDiagne/SolutionMigrationApiBdd/actions/workflows/ci.yml/badge.svg)

API REST en **ASP.NET Core (.NET 10)** avec **EF Core** et **SQL Server** : clients, produits, commandes, gestion du stock, journalisation et authentification JWT.
Projet personnel conçu pour être maîtrisé de bout en bout : API, base de données, tests, intégration continue, puis déploiement (voir la feuille de route).

## Démo en ligne

La documentation interactive (Scalar) permet d'essayer l'API déployée sur Azure : **https://ca-migapi.salmonriver-8486a327.francecentral.azurecontainerapps.io/scalar/v1**

> L'architecture utilise un hébergement Serverless (Scale-to-Zero) et une base de données Azure SQL Serverless. Si l'application n'a pas été sollicitée récemment, elle se met en veille automatiquement. Le premier chargement (démarrage à froid / cold start) peut donc nécessiter environ une minute, le temps que les ressources se réactivent.

Parcours conseillé (aucune installation) :

1. `POST /api/Auth/register` : créer un compte (e-mail, mot de passe d'au moins 8 caractères, confirmation, nom, prénom). La réponse donne le `clientId` du client métier créé avec le compte.
2. `POST /api/Auth/login` : copier l'`accessToken` de la réponse (valable 15 minutes).
3. Dans Scalar, coller le jeton dans le champ d'authentification **Bearer** : il est ensuite envoyé avec toutes les requêtes protégées.
4. `GET /api/Produit` : lire le catalogue (20 produits de démonstration).
5. `POST /api/Commande` avec l'en-tête `Idempotency-Key` (une chaîne unique de votre choix) et un corps `{"clientId": <votre clientId>, "lignesCommande": [{"produitId": <un identifiant du catalogue>, "quantite": 2}]}`.
6. `GET /api/Commande`, puis `GET /api/Produit/<identifiant>` : la commande est enregistrée et le stock du produit commandé a baissé.

Un compte `User` ne voit que ses propres commandes et son propre client ; la création de produits, le réapprovisionnement et la liste des clients sont réservés à `Admin`. Les routes d'inscription et de connexion sont limitées à 20 requêtes par minute et par adresse IP (réponse **429** au-delà).

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
| `OpenApi:Enabled` (`OpenApi__Enabled`) | Expose la description OpenAPI et Scalar hors développement | inutile (toujours exposée) | `true` pour la démo |
| `RateLimiting:Auth:PermitLimit`, `RateLimiting:Auth:WindowSeconds` (`RateLimiting__Auth__...`) | Limite d'appels par IP sur l'inscription et la connexion | 20 par 60 s (défaut) | 20 par 60 s (défaut) |

Le compte administrateur est créé **une seule fois** : si l'e-mail existe déjà, le mot de passe n'est pas relu, donc modifier `SeedAdmin:Password` plus tard ne change pas le mot de passe en base. L'application refuse de démarrer si l'e-mail ou le mot de passe est absent. Aucun de ces secrets ne doit figurer dans `appsettings.json` ni dans le dépôt.

### Lancer avec Docker (environnement local)

Prérequis : Docker Desktop. Le `docker-compose.yml` démarre trois éléments : **nginx** (reverse proxy HTTPS), l'**API** et **SQL Server**. Les secrets sont lus dans un fichier `.env` (ignoré par git).

```bash
cp .env.example .env     # puis renseigner les valeurs
docker compose up --build
```

L'API est alors disponible sur `https://localhost:9443`. nginx termine le HTTPS avec un certificat **auto-signé** (généré au premier lancement) puis transmet les requêtes en HTTP à l'API sur le réseau interne Docker ; l'API n'est pas exposée directement. Il faut donc accepter l'avertissement du navigateur, ou désactiver la vérification du certificat dans le client de test.

| Variable du `.env` | Rôle |
|---|---|
| `SQL_PASSWORD` | Mot de passe du compte `sa` de SQL Server (8 caractères minimum, majuscule, minuscule, chiffre, symbole) |
| `JWT_SIGNING_KEY` | Clé de signature des JWT (32 caractères minimum) |
| `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD` | Compte administrateur créé au démarrage |
| `HTTPS_PORT` | Port HTTPS sur la machine hôte (9443 par défaut) |

Le fichier Compose active des réglages réservés au local : `ApplyMigrationsOnStartup=true` (création du schéma au démarrage, car une base neuve est vide) et `SeedDemoData=true`. Sur un serveur de test ou de production, ils restent absents ou à `false`. nginx transmet le schéma d'origine dans `X-Forwarded-Proto` ; l'API le prend en compte grâce à `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, ce qui rend le cookie `Secure` du refresh token utilisable et évite toute redirection HTTPS inutile.

### Déploiement sur Azure

L'API tourne sur **Azure Container Apps** (environnement à la consommation, de 0 à 1 réplique) avec **Azure SQL** (offre gratuite serverless, pause automatique).

- **Secrets** : la clé JWT, le mot de passe de l'administrateur et la chaîne de connexion sont dans **Azure Key Vault** ; l'application les lit avec son **identité managée** (lecture seule).
- **Base de données** : l'application se connecte à Azure SQL **sans mot de passe**, avec son identité managée (`Authentication=Active Directory Managed Identity`) ; le serveur n'accepte que l'authentification Microsoft Entra.
- **Livraison continue** : un push sur `main` lance les tests, publie l'image Docker sur GitHub Container Registry (tag `sha-<commit>`) puis déploie cette image sur Azure. GitHub s'authentifie auprès d'Azure par identité fédérée (OIDC), sans secret stocké.
- **Coûts** : zéro réplique au repos, une réplique maximale, alertes de budget mensuel.

Compromis assumé : la règle de pare-feu SQL « services Azure » reste ouverte, car les adresses de sortie d'une Container App en mode consommation ne sont pas fixes ; l'accès exige de toute façon un jeton Microsoft Entra valide pour l'identité de l'application.

### Données de démonstration (catalogue)

`scripts/seed-demo-produits.sh` crée 20 produits via l'API avec le compte administrateur (le mot de passe est lu dans Key Vault). Le passage par l'API, et non par un `INSERT` SQL, conserve la traçabilité : mouvement de stock « stock initial » et journal d'audit sont écrits comme pour une vraie création. Le script est rejouable : un produit déjà présent est ignoré.

```bash
export ADMIN_EMAIL="<e-mail du compte administrateur>"
bash scripts/seed-demo-produits.sh
```

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

Le workflow GitHub Actions (`.github/workflows/ci.yml`) enchaîne trois jobs :

1. **tests** : compile la solution et lance tous les tests, avec un conteneur SQL Server pour les tests d'intégration (à chaque push et à chaque pull request) ;
2. **docker** : construit l'image Docker ; sur `main`, la publie sur GitHub Container Registry (`sha-<commit>` et `latest`) ;
3. **deploy** : sur `main` uniquement, met à jour l'application Azure Container Apps avec l'image du commit, puis vérifie qu'elle répond.

## Limites connues et feuille de route

- Une mise à jour (`PUT`) avec une `RowVersion` périmée mais un contenu identique à l'état courant renvoie 200 : rien n'est écrit, donc aucun conflit n'est détecté.
- Cache en mémoire du processus : un cache distribué (Redis) sera nécessaire avec plusieurs instances.
- Les migrations s'exécutent au démarrage de l'API (`ApplyMigrationsOnStartup`) : l'identité de l'application a donc le droit de modifier le schéma. Les sortir dans une étape dédiée du pipeline permettrait de réduire ses droits à la lecture et à l'écriture.
- Réalisé : serveur de test (Windows Server 2022, IIS), conteneurisation (Docker, nginx), Azure Container Apps, Azure SQL, Key Vault, identité managée, déploiement continu, limitation de débit.
- À venir : clés Data Protection persistantes, Application Insights, rôle Azure personnalisé pour le pipeline, puis Kubernetes (AKS).

## Auteur

Oumar Diagne, développeur full-stack C# / ASP.NET Core / Angular, certifié Microsoft Azure Developer Associate (AZ-204).
