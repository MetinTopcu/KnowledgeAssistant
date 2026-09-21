# Configuration & Secrets

**Local development uses .NET User Secrets. Production uses environment
variables. No real Azure API key, account key, or connection string is ever
committed.** The one committed connection string is Azurite's public,
Development-only `devstoreaccount1` credential — see §1.

---

## 1. The principle that does the heavy lifting

The most reliable way to keep a secret out of Git is for the secret not to
exist. This solution authenticates to Azure with **Entra ID via
`DefaultAzureCredential`**, not with keys:

| Service | Instead of | Use |
|---|---|---|
| Blob Storage | account key / connection string | account **URI** + `TokenCredential` |
| AI Search | admin or query API key | endpoint + `TokenCredential` |
| AI Foundry | API key | endpoint + `TokenCredential` |

What remains in `appsettings.json` is endpoints, container names, deployment
names, and tuning values — none of which are credentials. A leaked endpoint is
not an incident; a leaked account key is.

**The local Blob Storage emulator is the single exception.** Azurite accepts only
shared-key authentication, so in the Development environment
`Azure:Storage:ConnectionString` may point the blob client at Azurite using the
standard public `devstoreaccount1` credential, which is identical on every
installation and is not a secret. The application refuses that setting outside
Development, and refuses any account or key other than Azurite's, so no real
Azure account key can be used through this path. Production Azure Blob Storage
always uses `Azure:Storage:ServiceUri` + `DefaultAzureCredential`.

Keys are supported as a fallback (some environments still require them), but
they belong **only** in user secrets locally and Key Vault in production — never
in a file inside the repository.

---

## 2. Provider precedence

`WebApplication.CreateBuilder(args)` layers providers so that later sources win:

```
appsettings.json                      committed, non-secret baseline
  └─ appsettings.{Environment}.json   local overrides (gitignored)
      └─ User Secrets                 Development environment ONLY
          └─ Environment variables    ← production
              └─ Command-line args
```

Two consequences worth internalising:

- **User secrets load only when `ASPNETCORE_ENVIRONMENT=Development`.** They are
  a developer convenience, not a deployment mechanism, and cannot leak into
  production by accident.
- **Environment variables outrank every file.** Production configuration never
  needs a file edit, which is why no production value ever has a reason to be
  written into the repo.

---

## 3. Local development — User Secrets

Already initialised. `KnowledgeAssistant.Api.csproj` carries:

```xml
<UserSecretsId>374251c9-201f-46f8-8830-b3c3e86f8e50</UserSecretsId>
```

That ID **is** meant to be committed — it is a folder name, not a credential.
The values live outside the repository at:

```
%APPDATA%\Microsoft\UserSecrets\374251c9-201f-46f8-8830-b3c3e86f8e50\secrets.json
```

Because that path is outside the working tree, no `.gitignore` rule is what
protects it — it is structurally unreachable by git.

### Set values

Run from the `KnowledgeAssistant.Api` folder (or pass `--project`). Note the
**colon** separator here:

```powershell
dotnet user-secrets set "Azure:Storage:ServiceUri"  "https://<account>.blob.core.windows.net/"
dotnet user-secrets set "Azure:Search:Endpoint"     "https://<service>.search.windows.net/"
dotnet user-secrets set "Azure:AiFoundry:Endpoint"  "https://<resource>.services.ai.azure.com/"
dotnet user-secrets set "Azure:AiFoundry:ChatDeploymentName"      "gpt-5-mini"
dotnet user-secrets set "Azure:AiFoundry:EmbeddingDeploymentName" "text-embedding-3-large"
```

Set `Azure:Storage:ServiceUri` only if you use a real dev storage account.
`appsettings.Development.example.json` defaults to Azurite through
`Azure:Storage:ConnectionString`, and the host refuses to start with both set.

**The models, and why the defaults look the way they do.** The verified
deployments are `text-embedding-3-large` (3072-dimensional vectors) and
`gpt-5-mini`. `EmbeddingModelName` and `EmbeddingDimensions` default to that
embedding model, and for a known model the host refuses to start with any other
dimension — `knowledge-chunks` is created from it and cannot be changed in place.
`ChatTemperature` and `Agent:Temperature` default to **unset**, which sends no
temperature at all: `gpt-5-mini`, a reasoning model, accepts only its default and
answers `400 unsupported_value` to anything else. Set `0.0` only for a
non-reasoning deployment.

```powershell
dotnet user-secrets list      # show all
dotnet user-secrets remove "Azure:Search:Endpoint"
dotnet user-secrets clear     # wipe
```

### Sign in with the Azure CLI

With no key in play, the application uses **your own identity** locally. In the
`Development` environment that identity comes from the Azure CLI only
(`AzureCliCredential`); every other environment keeps the full
`DefaultAzureCredential` chain and resolves to a managed identity in Azure.

```powershell
az login
```

Development skips the chain because, on a laptop, it was measured spending ~27 s
probing for a managed identity and ~24 s failing in Visual Studio before the CLI
answered — long enough for every readiness probe to time out. Visual Studio and
VS Code sign-ins are therefore not used locally.

**In Docker Compose** the API container is the production image, which has no
Azure CLI. There, `Azure:Credential:DevelopmentCredential=ManagedIdentity` makes
Development use `ManagedIdentityCredential` instead, answered by the dev-only
`azure-token-proxy` container (`tools/azure-token-proxy`), which holds your own
`az login` in a Docker volume and speaks the managed identity protocol
(`IDENTITY_ENDPOINT` / `IDENTITY_HEADER`):

```powershell
docker compose run --rm azure-token-proxy az login --use-device-code
docker compose up --build
```

That reproduces the credential *type* used in Azure, not Azure's identity: calls
still run as you, with your roles. Outside Development the setting is ignored.

Then grant your user account the same RBAC roles listed in §5. If a call returns
`403`, the cause is almost always a missing role assignment, not a bad endpoint.

### Non-secret local overrides

Copy the committed template — the real file is gitignored:

```powershell
copy appsettings.Development.example.json appsettings.Development.json
```

---

## 4. Production — Environment variables

Use `__` (**double underscore**) in place of `:`. Colons work on Windows but
**not** on Linux, so always use the double underscore — it is portable
everywhere.

| Configuration key | Environment variable |
|---|---|
| `Azure:Storage:ServiceUri` | `Azure__Storage__ServiceUri` |
| `Azure:Search:Endpoint` | `Azure__Search__Endpoint` |
| `Azure:Search:IndexName` | `Azure__Search__IndexName` |
| `Azure:Search:ChunkIndexName` | `Azure__Search__ChunkIndexName` |
| `Azure:Search:IndexingBatchSize` | `Azure__Search__IndexingBatchSize` |
| `Azure:Search:EnableVectorizer` | `Azure__Search__EnableVectorizer` |
| `Azure:AiFoundry:Endpoint` | `Azure__AiFoundry__Endpoint` |
| `Azure:AiFoundry:ChatDeploymentName` | `Azure__AiFoundry__ChatDeploymentName` |
| `Azure:AiFoundry:EmbeddingDeploymentName` | `Azure__AiFoundry__EmbeddingDeploymentName` |
| `Azure:AiFoundry:EmbeddingBatchSize` | `Azure__AiFoundry__EmbeddingBatchSize` |
| `Azure:AiFoundry:ChatMaxOutputTokens` | `Azure__AiFoundry__ChatMaxOutputTokens` |
| `Azure:AiFoundry:ChatTemperature` | `Azure__AiFoundry__ChatTemperature` |
| `Azure:AiFoundry:EmbeddingModelName` | `Azure__AiFoundry__EmbeddingModelName` |
| `Azure:AiFoundry:EmbeddingDimensions` | `Azure__AiFoundry__EmbeddingDimensions` |
| `Azure:AiFoundry:Agent:ProjectEndpoint` | `Azure__AiFoundry__Agent__ProjectEndpoint` |
| `Azure:AiFoundry:Agent:Version` | `Azure__AiFoundry__Agent__Version` |
| `Azure:DocumentIntelligence:Endpoint` | `Azure__DocumentIntelligence__Endpoint` |
| `Azure:Credential:ManagedIdentityClientId` | `Azure__Credential__ManagedIdentityClientId` |
| `Azure:Credential:DevelopmentCredential` | `Azure__Credential__DevelopmentCredential` (Development only) |
| `Chunking:MaxChunkSize` | `Chunking__MaxChunkSize` |
| `Chunking:OverlapSize` | `Chunking__OverlapSize` |
| *(host)* | `ASPNETCORE_ENVIRONMENT=Production` |

### App Service / Container Apps

```powershell
az webapp config appsettings set `
  --name <app> --resource-group <rg> `
  --settings Azure__Search__Endpoint="https://<service>.search.windows.net/"
```

### Key Vault for anything genuinely secret

Values that must remain secret are stored in Key Vault and surfaced as
configuration through managed identity — so the app holds no bootstrap
credential at all:

```powershell
az webapp config appsettings set `
  --name <app> --resource-group <rg> `
  --settings Azure__Search__ApiKey="@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/search-api-key/)"
```

Never place a secret in a Bicep/ARM parameter file, a pipeline YAML, or a
Dockerfile `ENV` — all three are committed artefacts, and a `docker history`
call will happily print the last one back to you.

### CI holds no Azure credential either

`.github/workflows/acr-publish.yml` pushes images to Azure Container Registry
and stores no password to do it: it signs in with **GitHub OIDC federated with
Entra ID**, so the only thing GitHub holds is three identifiers —
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`. They identify;
they do not authorise. Authorisation comes from a federated credential on the
Azure side that names this repository and branch, and from one role assignment
(`AcrPush`, on one registry).

The registry's admin user stays disabled. Enabling it to simplify a pipeline
would recreate exactly the long-lived shared credential this whole document
exists to avoid. See DEPLOYMENT.md §8.

---

## 5. RBAC roles for the app's managed identity

Enable a managed identity on the App Service / Container App, then assign the
data-plane roles. "Contributor" on the resource is **not** sufficient — it grants
management-plane rights, not data access, which is a common and confusing
source of `403`s.

| Resource | Role | Narrowest scope that works |
|---|---|---|
| Storage | `Storage Blob Data Contributor` | The documents **container** |
| AI Search | `Search Index Data Contributor` (read/write documents) | The search service |
| AI Search | `Search Service Contributor` (read and create the two indexes) | The search service |
| AI Foundry account | `Azure AI User` — shown as **Foundry User** in the portal | The **account** — chat, embeddings and Document Intelligence are account-level endpoints |
| AI Foundry project | `Azure AI Project Manager` *(Development only, while no version is pinned)* | The project |
| Container registry | `AcrPull` | The registry — the app *pulls*; CI's separate OIDC identity is the only thing that pushes |
| Key Vault | `Key Vault Secrets User` *(only if a secret is sourced from one)* | The vault |

`Azure AI User` carries the data actions for OpenAI inference, Document
Intelligence, and agents, so `Cognitive Services OpenAI User` and
`Cognitive Services User` are redundant beside it.

Grant the same roles to each developer's own account for local development.

### The agent's write role is avoidable, and should be avoided

`Azure AI Project Manager` is only needed because an empty
`Azure:AiFoundry:Agent:Version` lets the running process create an agent version
on the first question — and that is now allowed **only in Development**. Any
other environment refuses to start without a pinned version.

1. In Development, with the role on your own account, ask one agent question.
2. Take the version from the log: `Created Foundry agent '...' version N` (or
   `Reusing ... version N` when a matching one exists).
3. Set `Azure:AiFoundry:Agent:Version=N` for every deployed environment.

A pinned version is **verified, not trusted**: on first use the app reads that
version and compares its stored definition fingerprint (instructions, tool
schema, model deployment, temperature) with the one this build computes. A
mismatch fails the agent route with a log naming both fingerprints — changing
any of those inputs means provisioning and pinning a new version. With a version
pinned, the app reads one version and creates nothing — `Azure AI User` is
sufficient.

### The service-side vectorizer is disabled

`Azure:Search:EnableVectorizer` is `false`, in `appsettings.json` and as the code
default, and is not used. The application generates query embeddings itself
through `IEmbeddingService` and submits a `VectorizedQuery`, so query and corpus
vectors always come from the same configured model. The chunk index therefore
declares no Azure OpenAI vectorizer.

Consequently the search service needs **no managed identity and no role on AI
Foundry** — only the application's identity calls Azure OpenAI, under the roles
listed above.

---

## 6. Fail fast on missing configuration

Bind options in `Infrastructure/DependencyInjection` with validation at startup:

```
.Bind(configuration.GetSection(AzureSearchOptions.SectionName))
.ValidateDataAnnotations()
.ValidateOnStart()
```

`ValidateOnStart()` is the important call. Without it a missing endpoint
surfaces as a `NullReferenceException` on the first user request that touches
search — typically in production, typically at the worst time. With it, the
process refuses to start and the deployment fails visibly and immediately.

---

## 7. What `.gitignore` does and does not protect

`.gitignore` only affects files git is **not already tracking**. Adding a rule
for a file that has already been committed hides it from `git status` while
leaving it in history, fully readable by anyone with clone access.

**If a credential ever reaches a commit: rotate it first.** Treat it as
compromised the moment it is pushed — history rewriting (`git filter-repo`,
BFG) removes the copy but cannot recall what has already been cloned, cached,
or indexed. Rotation is the fix; history cleanup is housekeeping afterwards.

Worth adding when the repo goes live: secret scanning (GitHub push protection,
Defender for Cloud, or `gitleaks` as a pre-commit hook) so the rule is enforced
by a machine rather than by everyone remembering.

### This repo hit exactly that case

`appsettings.Development.json` was **already tracked** when the ignore rule was
added, so the rule was inert — `git check-ignore` reported it as not ignored,
and any credential pasted into it would have been committed despite the rule
appearing to cover it.

Fixed by untracking it while leaving it on disk:

```powershell
git rm --cached KnowledgeAssistant.Api/appsettings.Development.json
```

It now resolves as ignored. The file held only logging levels, so nothing needed
rotating — but this is precisely how a settings file that "is in `.gitignore`"
ends up in history anyway. Worth running `git ls-files` against the patterns in
§7 whenever you add an ignore rule.

---

## 8. Checklist

- [x] User Secrets enabled (`UserSecretsId` in `KnowledgeAssistant.Api.csproj`)
- [x] `.gitignore` covers `secrets.json`, `.env*`, `appsettings.Development.json`, `appsettings.Local.json`, `local.settings.json`
- [x] `appsettings.json` contains no credential — endpoints and names only
- [x] Committed template `appsettings.Development.example.json` with placeholders
- [x] `appsettings.Development.json` untracked so the ignore rule actually applies
- [ ] Managed identity enabled and RBAC roles assigned per environment
- [x] Options bound with `ValidateOnStart()` — `BlobStorageOptions` in `AddInfrastructure`
- [ ] Secret scanning enabled on the remote
