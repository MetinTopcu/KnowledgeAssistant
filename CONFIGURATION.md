# Configuration & Secrets

**Local development uses .NET User Secrets. Production uses environment
variables. No API key or connection string is ever committed.**

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
dotnet user-secrets set "Azure:AiFoundry:ChatDeploymentName"      "gpt-4o-mini"
dotnet user-secrets set "Azure:AiFoundry:EmbeddingDeploymentName" "text-embedding-3-small"
```

```powershell
dotnet user-secrets list      # show all
dotnet user-secrets remove "Azure:Search:Endpoint"
dotnet user-secrets clear     # wipe
```

### Sign in so `DefaultAzureCredential` can find you

With no key in play, the credential chain uses **your own identity** locally —
via Visual Studio, VS Code, or:

```powershell
az login
```

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
| `Azure:DocumentIntelligence:Endpoint` | `Azure__DocumentIntelligence__Endpoint` |
| `Azure:Credential:ManagedIdentityClientId` | `Azure__Credential__ManagedIdentityClientId` |
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

---

## 5. RBAC roles for the app's managed identity

Enable a managed identity on the App Service / Container App, then assign the
data-plane roles. "Contributor" on the resource is **not** sufficient — it grants
management-plane rights, not data access, which is a common and confusing
source of `403`s.

| Resource | Role |
|---|---|
| Storage account | `Storage Blob Data Contributor` |
| AI Search | `Search Index Data Contributor` (read/write documents) |
| AI Search | `Search Service Contributor` (create/update the index) |
| AI Foundry / OpenAI | `Cognitive Services OpenAI User` |
| Document Intelligence | `Cognitive Services User` *(only if an endpoint is configured)* |
| Key Vault | `Key Vault Secrets User` |

Grant the same roles to each developer's own account for local development.

### The vectorizer needs a role assignment on a *different* identity

`Azure:Search:EnableVectorizer` declares integrated vectorization on the chunk
index, so the search service can embed a query string at query time. That call is
made by the **search service's own managed identity**, not by this application's
— so granting the app `Cognitive Services OpenAI User` does nothing for it:

| Identity | Resource | Role |
|---|---|---|
| **Search service** managed identity | AI Foundry / OpenAI | `Cognitive Services OpenAI User` |

Enable a system-assigned identity on the search service and assign that role. The
index will be created successfully without it — the vectorizer is only exercised
by queries, which nothing issues yet — so a missing grant surfaces later, as a
failing search rather than a failing deployment.

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
