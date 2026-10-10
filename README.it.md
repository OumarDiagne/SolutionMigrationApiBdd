🇬🇧 [English](README.md) · 🇫🇷 [Français](README.fr.md) · 🇮🇹 Italiano

# MigAgiBdd: API per la gestione di ordini e magazzino

![CI](https://github.com/OumarDiagne/SolutionMigrationApiBdd/actions/workflows/ci.yml/badge.svg)

API REST sviluppata con **ASP.NET Core (.NET 10)**, **EF Core** e **SQL Server**: clienti, prodotti, ordini, gestione del magazzino, registro delle operazioni e autenticazione JWT.
Progetto personale pensato per essere padroneggiato da capo a fondo: API, database, test, integrazione continua, poi distribuzione (vedi la roadmap).

## Demo online

La documentazione interattiva (Scalar) permette di provare l'API distribuita su Azure: **https://ca-migapi.salmonriver-8486a327.francecentral.azurecontainerapps.io/scalar/v1**

> L'architettura usa un hosting serverless (scale to zero) e un database Azure SQL Serverless. Se l'applicazione non è stata usata di recente, va in sospensione automaticamente. Il primo caricamento (cold start) può quindi richiedere circa un minuto, il tempo necessario perché le risorse si riattivino.

Percorso consigliato (nessuna installazione):

1. `POST /api/Auth/register`: creare un account (e-mail, password di almeno 8 caratteri, conferma, cognome, nome). La risposta contiene il `clientId` del cliente aziendale creato insieme all'account.
2. `POST /api/Auth/login`: copiare l'`accessToken` della risposta (valido 15 minuti).
3. In Scalar, incollare il token nel campo di autenticazione **Bearer**: viene poi inviato con tutte le richieste protette.
4. `GET /api/Produit`: leggere il catalogo (20 prodotti dimostrativi).
5. `POST /api/Commande` con l'intestazione `Idempotency-Key` (una stringa univoca a scelta) e un corpo come `{"clientId": <il tuo clientId>, "lignesCommande": [{"produitId": <un id del catalogo>, "quantite": 2}]}`.
6. `GET /api/Commande`, poi `GET /api/Produit/<id>`: l'ordine è registrato e la giacenza del prodotto ordinato è diminuita.

Un account `User` vede solo i propri ordini e il proprio cliente; la creazione di prodotti, il rifornimento e l'elenco dei clienti sono riservati ad `Admin`. Le route di registrazione e di accesso sono limitate a 20 richieste al minuto per indirizzo IP (risposta **429** oltre il limite).

> I nomi dei campi e i messaggi di errore dell'API sono in francese (ad esempio `nomProduit`, `quantite`).

## Cosa fa l'API

| Ambito | Funzionalità |
|---|---|
| **Autenticazione** | Registrazione (account e cliente aziendale creati insieme), accesso, JWT di accesso di breve durata, refresh token con rotazione, disconnessione |
| **Prodotti** | Catalogo, creazione, modifica, archiviazione; cache in memoria invalidata a ogni modifica |
| **Ordini** | Creazione, modifica, archiviazione; calcolo del totale, controllo della disponibilità e scarico della giacenza |
| **Clienti** | Lettura, modifica, disattivazione; un utente gestisce solo il proprio cliente |
| **Magazzino** | Rifornimento, movimenti di magazzino tracciati (entrata / uscita) |
| **Tracciabilità** | Registro di audit (valore precedente e nuovo), movimenti di magazzino, registro delle operazioni, identificatore di correlazione |

## Scelte tecniche

- **Architettura a livelli**: Controller → Servizi (regole di business) → Repository (accesso ai dati) → EF Core / SQL Server.
- **Concorrenza ottimistica**: colonna `RowVersion` su clienti, prodotti e ordini. Il client rinvia la versione letta (corpo della richiesta o intestazione `If-Match`); una versione obsoleta restituisce **409**, una versione assente **428**, una versione non valida **400**.
- **Idempotenza**: l'intestazione `Idempotency-Key` è obbligatoria alla creazione di un prodotto o di un ordine. La stessa chiamata ripetuta restituisce la stessa risposta senza duplicati né un secondo scarico della giacenza; la stessa chiave con un contenuto diverso restituisce **422**. La creazione avviene in una transazione.
- **Sicurezza**:
  - JWT di accesso di 15 minuti, refresh token in un cookie `HttpOnly`, `Secure`, `SameSite=Strict`, salvato **con hash** (SHA-256) nel database.
  - Rotazione del refresh token a ogni utilizzo; il riutilizzo di un vecchio token revoca tutti i token dell'utente.
  - Controllo degli accessi per ruolo (`Admin`, `User`) e per proprietario: la risorsa di un altro utente risponde **404**, come se non esistesse (nessuna fuga di informazioni sull'esistenza).
- **Errori omogenei**: un gestore globale restituisce `ProblemDetails` (RFC 7807).
- **Modello dei dati**: un cliente aziendale può esistere senza account (relazione 0..1 con l'utente Identity, chiave esterna nullable con indice univoco filtrato). Un account `User` ha sempre un cliente, creato alla registrazione; un account `Admin` non ne ha.
- **Librerie**: FluentValidation, Mapster, ASP.NET Core Identity, JwtBearer.

## Diritti di accesso

| Risorsa | Anonimo | `User` | `Admin` |
|---|---|---|---|
| Registrazione, accesso, refresh, disconnessione | sì | sì | sì |
| Prodotti: lettura | no | sì | sì |
| Prodotti: creazione, modifica, archiviazione | no | no | sì |
| Magazzino: rifornimento | no | no | sì |
| Ordini | no | i propri (per il proprio cliente) | tutti, per qualsiasi cliente |
| Clienti: elenco, creazione | no | no | sì |
| Clienti: lettura, modifica, disattivazione | no | il proprio (`/api/client/me`) | tutti |

## Avviare il progetto

Prerequisiti: SDK .NET 10 e SQL Server (LocalDB è sufficiente su Windows).

```bash
cd MigrationApiBdd

# Segreti di sviluppo (mai nel repository)
dotnet user-secrets set "Jwt:SigningKey" "<una chiave di almeno 32 caratteri>"
dotnet user-secrets set "SeedAdmin:Email" "admin@example.com"
dotnet user-secrets set "SeedAdmin:Password" "<una password conforme alla policy di Identity>"

# Creazione del database (stringa di connessione in appsettings.json, LocalDB di default)
dotnet ef database update

dotnet run
```

All'avvio, l'API crea i ruoli `Admin` e `User` e l'account amministratore. Il set di dati dimostrativi (clienti) viene caricato solo se `SeedDemoData` vale `true` **e** il database non contiene alcun cliente.
In sviluppo, la descrizione OpenAPI è esposta dall'API.

### Configurazione per ambiente

| Chiave (variabile d'ambiente) | Ruolo | Locale | Server di test / produzione |
|---|---|---|---|
| `Jwt:SigningKey` (`Jwt__SigningKey`) | Chiave di firma dei JWT | user-secrets | variabile d'ambiente o archivio dei segreti |
| `SeedAdmin:Email`, `SeedAdmin:Password` (`SeedAdmin__Email`, `SeedAdmin__Password`) | Account amministratore creato all'avvio | user-secrets | variabili d'ambiente |
| `SeedDemoData` | Carica clienti dimostrativi | `true` (definito in `launchSettings.json`) | non definita o `false` |
| `OpenApi:Enabled` (`OpenApi__Enabled`) | Espone la descrizione OpenAPI e Scalar fuori dallo sviluppo | non necessaria (sempre esposta) | `true` per la demo |
| `RateLimiting:Auth:PermitLimit`, `RateLimiting:Auth:WindowSeconds` (`RateLimiting__Auth__...`) | Limite di chiamate per IP su registrazione e accesso | 20 ogni 60 s (predefinito) | 20 ogni 60 s (predefinito) |

L'account amministratore viene creato **una sola volta**: se l'e-mail esiste già, la password non viene riletta, quindi modificare `SeedAdmin:Password` in seguito non cambia la password nel database. L'applicazione si rifiuta di avviarsi se l'e-mail o la password mancano. Nessuno di questi segreti deve comparire in `appsettings.json` né nel repository.

### Avviare con Docker (ambiente locale)

Prerequisito: Docker Desktop. Il `docker-compose.yml` avvia tre componenti: **nginx** (reverse proxy HTTPS), l'**API** e **SQL Server**. I segreti sono letti da un file `.env` (ignorato da git).

```bash
cp .env.example .env     # poi compilare i valori
docker compose up --build
```

L'API è quindi disponibile su `https://localhost:9443`. nginx termina l'HTTPS con un certificato **autofirmato** (generato al primo avvio) e inoltra le richieste in HTTP all'API sulla rete interna di Docker; l'API non è esposta direttamente. Occorre quindi accettare l'avviso del browser, oppure disattivare la verifica del certificato nel client di test.

| Variabile del `.env` | Ruolo |
|---|---|
| `SQL_PASSWORD` | Password dell'account `sa` di SQL Server (minimo 8 caratteri, maiuscola, minuscola, cifra, simbolo) |
| `JWT_SIGNING_KEY` | Chiave di firma dei JWT (minimo 32 caratteri) |
| `SEED_ADMIN_EMAIL`, `SEED_ADMIN_PASSWORD` | Account amministratore creato all'avvio |
| `HTTPS_PORT` | Porta HTTPS sulla macchina host (9443 di default) |

Il file Compose attiva impostazioni riservate all'uso locale: `ApplyMigrationsOnStartup=true` (creazione dello schema all'avvio, perché un database nuovo è vuoto) e `SeedDemoData=true`. Su un server di test o di produzione restano assenti o a `false`. nginx trasmette lo schema originale in `X-Forwarded-Proto`; l'API lo tiene in considerazione grazie a `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, il che rende utilizzabile il cookie `Secure` del refresh token ed evita qualsiasi reindirizzamento HTTPS inutile.

### Distribuzione su Azure

L'API gira su **Azure Container Apps** (ambiente a consumo, da 0 a 1 replica) con **Azure SQL** (offerta gratuita serverless, pausa automatica).

- **Segreti**: la chiave JWT, la password dell'amministratore e la stringa di connessione sono in **Azure Key Vault**; l'applicazione li legge con la propria **identità gestita** (sola lettura).
- **Database**: l'applicazione si connette ad Azure SQL **senza password**, con la propria identità gestita (`Authentication=Active Directory Managed Identity`); il server accetta solo l'autenticazione Microsoft Entra.
- **Distribuzione continua**: un push su `main` esegue i test, pubblica l'immagine Docker su GitHub Container Registry (tag `sha-<commit>`) e distribuisce quell'immagine su Azure. GitHub si autentica presso Azure con un'identità federata (OIDC), senza alcun segreto memorizzato.
- **Costi**: zero repliche a riposo, al massimo una replica, avvisi di budget mensile.

Compromesso accettato: la regola del firewall SQL «servizi Azure» resta aperta, perché gli indirizzi in uscita di una Container App in modalità a consumo non sono fissi; l'accesso richiede comunque un token Microsoft Entra valido per l'identità dell'applicazione.

### Dati dimostrativi (catalogo)

`scripts/seed-demo-produits.sh` crea 20 prodotti tramite l'API con l'account amministratore (la password viene letta da Key Vault). Passare dall'API anziché da un `INSERT` SQL conserva la tracciabilità: il movimento di magazzino «giacenza iniziale» e il registro di audit vengono scritti come per una vera creazione. Lo script può essere rieseguito senza rischi: un prodotto già presente viene ignorato.

```bash
export ADMIN_EMAIL="<e-mail dell'account amministratore>"
bash scripts/seed-demo-produits.sh
```

## Test

La soluzione contiene **più di 200 test** (xUnit, Moq):

- **Test unitari**: servizi (`StockService`, `CommandeService`, `AuthService`), cache, hash, decodifica della `RowVersion`.
- **Test di integrazione** (`WebApplicationFactory`): l'API completa si avvia in memoria con la vera pipeline HTTP, il vero JWT e un **vero database SQL Server temporaneo**, creato e poi eliminato alla fine. Coprono l'autenticazione e la rotazione dei token, i diritti di accesso, l'idempotenza, la concorrenza (409) e la registrazione nel database.

```bash
dotnet test
```

Per impostazione predefinita, i test di integrazione usano SQL Server LocalDB. La variabile d'ambiente `MIGAPI_TEST_CONNECTION` permette di indicare un altro server; viene sostituito solo il nome del database, e il database di sviluppo non viene mai usato.

### Test funzionali (Talend API Tester)

Sei scenari (autenticazione, prodotti, ordini e magazzino, isolamento tra utenti, clienti, magazzino) rieseguono l'API distribuita sul server di test. Usano un ambiente Talend con `adminEmail`, `adminPassword` e `runId` (da compilare da sé; nessun segreto è memorizzato nel file degli scenari). **Prima di ogni nuova esecuzione di uno scenario già eseguito, cambiare il valore di `runId`** (ad esempio `r1`, `r2`, `r3`…) nell'ambiente Talend: gli account creati alla registrazione e le chiavi di idempotenza ne derivano, quindi rieseguire con lo stesso valore provoca conflitti (account già esistente, chiave già usata) che fanno fallire lo scenario senza che l'API sia in causa.

## Integrazione continua

Il workflow GitHub Actions (`.github/workflows/ci.yml`) concatena tre job:

1. **tests**: compila la soluzione ed esegue tutti i test, con un container SQL Server per i test di integrazione (a ogni push e a ogni pull request);
2. **docker**: costruisce l'immagine Docker; su `main`, la pubblica su GitHub Container Registry (`sha-<commit>` e `latest`);
3. **deploy**: solo su `main`, aggiorna l'applicazione Azure Container Apps con l'immagine del commit, poi verifica che risponda.

## Limiti noti e roadmap

- Un aggiornamento (`PUT`) con una `RowVersion` obsoleta ma un contenuto identico allo stato corrente restituisce 200: non viene scritto nulla, quindi nessun conflitto viene rilevato.
- Cache in memoria del processo: servirà una cache distribuita (Redis) con più istanze.
- Le migrazioni vengono eseguite all'avvio dell'API (`ApplyMigrationsOnStartup`): l'identità dell'applicazione ha quindi il diritto di modificare lo schema. Spostarle in una fase dedicata della pipeline permetterebbe di ridurre i suoi diritti a lettura e scrittura.
- Realizzato: server di test (Windows Server 2022, IIS), containerizzazione (Docker, nginx), Azure Container Apps, Azure SQL, Key Vault, identità gestita, distribuzione continua, limitazione della frequenza delle richieste.
- In arrivo: chiavi Data Protection persistenti, Application Insights, ruolo Azure personalizzato per la pipeline, poi Kubernetes (AKS).

## Autore

Oumar Diagne, sviluppatore full-stack C# / ASP.NET Core / Angular, Microsoft Certified: Azure Developer Associate (AZ-204).
