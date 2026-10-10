🇬🇧 English · 🇫🇷 [Français](README.fr.md) · 🇮🇹 [Italiano](README.it.md)

# MigAgiBdd: Order and Stock Management API

![CI](https://github.com/OumarDiagne/SolutionMigrationApiBdd/actions/workflows/ci.yml/badge.svg)

REST API built with **ASP.NET Core (.NET 10)**, **EF Core** and **SQL Server**: customers, products, orders, stock management, audit logging and JWT authentication.
A personal project designed to be mastered end to end: API, database, tests, continuous integration, then deployment (see the roadmap).

## Live demo

The interactive documentation (Scalar) lets you try the API deployed on Azure: **https://ca-migapi.salmonriver-8486a327.francecentral.azurecontainerapps.io/scalar/v1**

> The architecture uses serverless hosting (scale to zero) and an Azure SQL Serverless database. If the application has not been used recently, it goes to sleep automatically. The first load (cold start) may therefore take about a minute while the resources wake up.

Suggested walkthrough (nothing to install):

1. `POST /api/Auth/register`: create an account (email, password of at least 8 characters, confirmation, last name, first name). The response contains the `clientId` of the business customer created with the account.
2. `POST /api/Auth/login`: copy the `accessToken` from the response (valid for 15 minutes).
3. In Scalar, paste the token into the **Bearer** authentication field: it is then sent with every protected request.
4. `GET /api/Produit`: read the catalogue (20 demo products).
5. `POST /api/Commande` with an `Idempotency-Key` header (any unique string) and a body such as `{"clientId": <your clientId>, "lignesCommande": [{"produitId": <a catalogue id>, "quantite": 2}]}`.
6. `GET /api/Commande`, then `GET /api/Produit/<id>`: the order is saved and the stock of the ordered product has decreased.

A `User` account only sees its own orders and its own customer record; creating products, restocking and listing customers are reserved for `Admin`. The registration and login routes are limited to 20 requests per minute per IP address (**429** response beyond that).

> The field names and error messages of the API are in French (for example `nomProduit`, `quantite`).

## What the API does

| Domain | Features |
|---|---|
| **Authentication** | Registration (account and business customer created together), login, short-lived access JWT, refresh token with rotation, logout |
| **Products** | Catalogue, creation, update, archiving; in-memory cache invalidated on every change |
| **Orders** | Creation, update, archiving; total calculation, availability check and stock decrement |
| **Customers** | Read, update, deactivate; a user only manages their own customer record |
| **Stock** | Restocking, traced stock movements (in / out) |
| **Traceability** | Audit log (old and new value), stock movements, operation log, correlation identifier |

## Technical choices

- **Layered architecture**: Controllers → Services (business rules) → Repositories (data access) → EF Core / SQL Server.
- **Optimistic concurrency**: `RowVersion` column on customers, products and orders. The client sends back the version it read (request body or `If-Match` header); a stale version returns **409**, a missing version **428**, an invalid version **400**.
- **Idempotency**: the `Idempotency-Key` header is mandatory when creating a product or an order. Replaying the same call returns the same response with no duplicate and no second stock decrement; the same key with different content returns **422**. Creation runs in a transaction.
- **Security**:
  - 15-minute access JWT, refresh token in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie, stored **hashed** (SHA-256) in the database.
  - The refresh token is rotated on every use; reusing an old token revokes all of the user's tokens.
  - Access control by role (`Admin`, `User`) and by owner: another user's resource answers **404**, as if it did not exist (no existence leak).
- **Consistent errors**: a global handler returns `ProblemDetails` (RFC 7807).
- **Data model**: a business customer can exist without an account (0..1 relationship with the Identity user, nullable foreign key with a filtered unique index). A `User` account always has a customer, created at registration; an `Admin` account has none.
- **Libraries**: FluentValidation, Mapster, ASP.NET Core Identity, JwtBearer.

## Access rights

| Resource | Anonymous | `User` | `Admin` |
|---|---|---|---|
| Registration, login, refresh, logout | yes | yes | yes |
| Products: read | no | yes | yes |
| Products: create, update, archive | no | no | yes |
| Stock: restocking | no | no | yes |
| Orders | no | their own (for their customer) | all, for any customer |
| Customers: list, create | no | no | yes |
| Customers: read, update, deactivate | no | their own (`/api/client/me`) | all |

## Running the project

Prerequisites: .NET 10 SDK and SQL Server (LocalDB is enough on Windows).

```bash
cd MigrationApiBdd

# Development secrets (never in the repository)
dotnet user-secrets set "Jwt:SigningKey" "<a key of at least 32 characters>"
dotnet user-secrets set "SeedAdmin:Email" "admin@example.com"
dotnet user-secrets set "SeedAdmin:Password" "<a password that meets the Identity policy>"

# Create the database (connection string in appsettings.json, LocalDB by default)
dotnet ef database update

dotnet run
```

At startup, the API creates the `Admin` and `User` roles and the administrator account. The demo data set (customers) is only loaded if `SeedDemoData` is `true` **and** the database contains no customer.
In development, the OpenAPI description is exposed by the API.

### Configuration per environment

| Key (environment variable) | Purpose | Local | Test server / production |
|---|---|---|---|
| `Jwt:SigningKey` (`Jwt__SigningKey`) | JWT signing key | user-secrets | environment variable or secret vault |
| `SeedAdmin:Email`, `SeedAdmin:Password` (`SeedAdmin__Email`, `SeedAdmin__Password`) | Administrator account created at startup | user-secrets | environment variables |
| `SeedDemoData` | Loads demo customers | `true` (set in `launchSettings.json`) | unset or `false` |
| `OpenApi:Enabled` (`OpenApi__Enabled`) | Exposes the OpenAPI description and Scalar outside development | not needed (always exposed) | `true` for the demo |
| `RateLimiting:Auth:PermitLimit`, `RateLimiting:Auth:WindowSeconds` (`RateLimiting__Auth__...`) | Calls allowed per IP on registration and login | 20 per 60 s (default) | 20 per 60 s (default) |

The administrator account is created **only once**: if the email already exists, the password is not read again, so changing `SeedAdmin:Password` later does not change the password in the database. The application refuses to start if the email or the password is missing. None of these secrets may appear in `appsettings.json` or in the repository.

### Running with Docker (local environment)

Prerequisite: Docker Desktop. The `docker-compose.yml` starts three components: **nginx** (HTTPS reverse proxy), the **API** and **SQL Server**. Secrets are read from a `.env` file (ignored by git).

```bash
cp .env.example .env     # then fill in the values
docker compose up --build
```

The API is then available at `https://localhost:9443`. nginx terminates HTTPS with a **self-signed** certificate (generated on first start) and forwards requests over HTTP to the API on the internal Docker network; the API is not exposed directly. You therefore need to accept the browser warning, or disable certificate verification in the test client.

| `.env` variable | Purpose |
|---|---|
| `SQL_PASSWORD` | Password of the SQL Server `sa` account (at least 8 characters, upper case, lower case, digit, symbol) |
| `JWT_SIGNING_KEY` | JWT signing key (at least 32 characters) |
| `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD` | Administrator account created at startup |
| `HTTPS_PORT` | HTTPS port on the host machine (9443 by default) |

The Compose file enables settings reserved for local use: `ApplyMigrationsOnStartup=true` (schema created at startup, because a new database is empty) and `SeedDemoData=true`. On a test or production server they stay unset or `false`. nginx passes the original scheme in `X-Forwarded-Proto`; the API takes it into account thanks to `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, which makes the `Secure` refresh token cookie usable and avoids any needless HTTPS redirect.

### Deployment on Azure

The API runs on **Azure Container Apps** (consumption environment, 0 to 1 replica) with **Azure SQL** (free serverless offer, automatic pause).

- **Secrets**: the JWT key, the administrator password and the connection string are in **Azure Key Vault**; the application reads them with its **managed identity** (read-only).
- **Database**: the application connects to Azure SQL **without a password**, using its managed identity (`Authentication=Active Directory Managed Identity`); the server only accepts Microsoft Entra authentication.
- **Continuous delivery**: a push to `main` runs the tests, publishes the Docker image to GitHub Container Registry (tag `sha-<commit>`) and deploys that image to Azure. GitHub authenticates to Azure with a federated identity (OIDC), with no stored secret.
- **Costs**: zero replicas when idle, one replica at most, monthly budget alerts.

Accepted trade-off: the SQL firewall rule "Azure services" stays open, because the outbound addresses of a Container App in consumption mode are not fixed; access still requires a valid Microsoft Entra token for the application's identity.

### Demo data (catalogue)

`scripts/seed-demo-produits.sh` creates 20 products through the API with the administrator account (the password is read from Key Vault). Going through the API rather than a SQL `INSERT` preserves traceability: the "initial stock" stock movement and the audit log are written exactly as for a real creation. The script can be rerun safely: a product that already exists is skipped.

```bash
export ADMIN_EMAIL="<administrator account email>"
bash scripts/seed-demo-produits.sh
```

## Tests

The solution contains **more than 200 tests** (xUnit, Moq):

- **Unit tests**: services (`StockService`, `CommandeService`, `AuthService`), cache, hash, `RowVersion` decoding.
- **Integration tests** (`WebApplicationFactory`): the complete API starts in memory with the real HTTP pipeline, the real JWT and a **real temporary SQL Server database**, created and then deleted at the end. They cover authentication and token rotation, access rights, idempotency, concurrency (409) and database logging.

```bash
dotnet test
```

By default, the integration tests use SQL Server LocalDB. The `MIGAPI_TEST_CONNECTION` environment variable lets you point to another server; only the database name is replaced, and the development database is never used.

### Functional tests (Talend API Tester)

Six scenarios (authentication, products, orders and stock, isolation between users, customers, stock) replay the API deployed on the test server. They use a Talend environment with `adminEmail`, `adminPassword` and `runId` (to be filled in yourself; no secret is stored in the scenario file). **Before every new run of a scenario that has already been executed, change the value of `runId`** (for example `r1`, `r2`, `r3`…) in the Talend environment: the accounts created at registration and the idempotency keys are derived from it, so replaying with the same value causes conflicts (account already exists, key already used) that make the scenario fail without the API being at fault.

## Continuous integration

The GitHub Actions workflow (`.github/workflows/ci.yml`) chains three jobs:

1. **tests**: builds the solution and runs all tests, with a SQL Server container for the integration tests (on every push and every pull request);
2. **docker**: builds the Docker image; on `main`, publishes it to GitHub Container Registry (`sha-<commit>` and `latest`);
3. **deploy**: on `main` only, updates the Azure Container Apps application with the commit's image, then checks that it responds.

## Known limitations and roadmap

- An update (`PUT`) with a stale `RowVersion` but content identical to the current state returns 200: nothing is written, so no conflict is detected.
- In-process memory cache: a distributed cache (Redis) will be needed with several instances.
- Migrations run when the API starts (`ApplyMigrationsOnStartup`): the application's identity therefore has the right to change the schema. Moving them to a dedicated pipeline step would let its rights be reduced to read and write.
- Done: test server (Windows Server 2022, IIS), containerisation (Docker, nginx), Azure Container Apps, Azure SQL, Key Vault, managed identity, continuous delivery, rate limiting.
- Upcoming: persistent Data Protection keys, Application Insights, custom Azure role for the pipeline, then Kubernetes (AKS).

## Author

Oumar Diagne, full-stack C# / ASP.NET Core / Angular developer, Microsoft Certified: Azure Developer Associate (AZ-204).
