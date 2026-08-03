# Deployment

How to provision the Azure resources KnowledgeAssistant needs, wire them to a
managed identity, and deploy the container.

Everything below assumes the principle the rest of this codebase is built on:
**no secret is created, stored, or transmitted at any point.** There is no API
key, no connection string, and no client secret — not in configuration, not in
CI, not in the image. Every call is authorised by Entra ID through
`DefaultAzureCredential`. If a step below seems to be missing a credential, that
is the design working.

> Role assignments propagate. A fresh grant can take a few minutes to take
> effect, so a `403` immediately after `az role assignment create` usually means
> "wait", not "wrong role".

---

## Contents

1. [What you are provisioning](#1-what-you-are-provisioning)
2. [Azure Blob Storage](#2-azure-blob-storage)
3. [Azure AI Search](#3-azure-ai-search)
4. [Azure AI Foundry](#4-azure-ai-foundry)
5. [Managed identity](#5-managed-identity)
6. [Required RBAC roles](#6-required-rbac-roles)
7. [Deploy the container](#7-deploy-the-container)
8. [Configuration reference](#8-configuration-reference)
9. [Verify the deployment](#9-verify-the-deployment)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. What you are provisioning

| Resource | Holds | Required |
|---|---|---|
| Storage account | Uploaded PDFs, in one container | Yes |
| AI Search service | Two indexes: documents, and chunks with vectors | Yes |
| AI Foundry account + project | Embedding and chat deployments, and the agent | Yes |
| Document Intelligence | OCR for scanned PDFs | No — falls back to local PdfPig |
| Application Insights | Traces, metrics, logs | No — falls back to console only |

Set these once and reuse them throughout:

```bash
RG=rg-knowledge-assistant
LOCATION=swedencentral
STORAGE=stknowledge$RANDOM
SEARCH=srch-knowledge-assistant
FOUNDRY=aif-knowledge-assistant
PROJECT=knowledge-assistant
APP=ca-knowledge-assistant

az group create --name $RG --location $LOCATION
```

> Pick a region that offers both AI Search and the AI Foundry models you intend
> to deploy. Model availability varies by region far more than the other
> services do, so choose the region for AI Foundry first and place everything
> else alongside it.

---

## 2. Azure Blob Storage

Holds the original uploaded documents. The blob is the record; the search index
is a rebuildable projection of it.

```bash
az storage account create \
  --name $STORAGE \
  --resource-group $RG \
  --location $LOCATION \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false \
  --allow-shared-key-access false
```

Two flags carry weight:

- `--allow-blob-public-access false` — uploaded documents are private corpus
  material. Public blob access would make every ingested document readable by
  anyone with the URL.
- `--allow-shared-key-access false` — **disables account keys entirely.** This
  is what turns "we use managed identity" from a convention into something the
  platform enforces: with shared keys off, a connection string cannot
  authenticate even if one leaked. The application never uses a key, so nothing
  breaks.

Create the container with your own identity (the data plane needs a role, which
you also grant to yourself for local development — see §6):

```bash
az storage container create \
  --name documents \
  --account-name $STORAGE \
  --auth-mode login
```

The application creates the container on demand too, but provisioning it here
means a first upload is not also the first thing to discover a missing role.

Configuration: `Azure:Storage:ServiceUri` — the **account URI**, not a
connection string.

```
https://<account>.blob.core.windows.net/
```

---

## 3. Azure AI Search

Holds two indexes at two granularities: one keyed by document (which documents
exist) and one keyed by chunk with vectors (which passages match a query). The
application creates both on demand from the schemas in
`Infrastructure/Search/`.

```bash
az search service create \
  --name $SEARCH \
  --resource-group $RG \
  --location $LOCATION \
  --sku basic \
  --auth-options aadOrApiKey \
  --aad-auth-failure-mode http403
```

`--auth-options aadOrApiKey` is the minimum that permits Entra ID
authentication; `--sku basic` is the smallest tier supporting vector search at a
useful scale. The free tier works for a trial but caps index count and size.

Enable a system-assigned identity on the **search service itself** — this is not
the application's identity, and §6 explains why it needs one:

```bash
az search service update \
  --name $SEARCH \
  --resource-group $RG \
  --identity-type SystemAssigned
```

Configuration:

```
Azure:Search:Endpoint         https://<service>.search.windows.net/
Azure:Search:IndexName        knowledge-index
Azure:Search:ChunkIndexName   knowledge-chunks
```

> `HnswM` and `HnswEfConstruction` are fixed when an index is created. Changing
> them later requires deleting the index and reloading the corpus.
> `HnswEfSearch` can be revised in place.

---

## 4. Azure AI Foundry

One resource serves three things: the embedding model, the chat model, and the
agent. That is why they share one endpoint and one retry budget in
configuration.

```bash
az cognitiveservices account create \
  --name $FOUNDRY \
  --resource-group $RG \
  --location $LOCATION \
  --kind AIServices \
  --sku S0 \
  --custom-domain $FOUNDRY \
  --assign-identity
```

`--custom-domain` is required: token-based authentication does not work against
the regional endpoint, only against a custom subdomain. Omitting it produces an
account that works with keys and fails with Entra ID, which is a confusing thing
to debug later.

### Model deployments

```bash
az cognitiveservices account deployment create \
  --name $FOUNDRY --resource-group $RG \
  --deployment-name text-embedding-3-small \
  --model-name text-embedding-3-small \
  --model-version 1 --model-format OpenAI \
  --sku-capacity 50 --sku-name Standard

az cognitiveservices account deployment create \
  --name $FOUNDRY --resource-group $RG \
  --deployment-name gpt-4o-mini \
  --model-name gpt-4o-mini \
  --model-version 2024-07-18 --model-format OpenAI \
  --sku-capacity 50 --sku-name Standard
```

**Deployment names, not model names.** The application addresses deployments.
Using a model name where Azure expects a deployment is the single most common
cause of a `404` from otherwise correct configuration. They are identical above
only because that is a sensible convention, not because Azure requires it.

Capacity is in thousands of tokens per minute. 50 is comfortable for evaluation;
the retry pipeline handles `429` with `Retry-After`, but sustained throttling is
a quota problem that retries only disguise.

### The project, for the agent

The agent belongs to a **project** inside the account. Create one in the Azure
AI Foundry portal, then note its endpoint:

```
https://<account>.services.ai.azure.com/api/projects/<project>
```

Configuration:

```
Azure:AiFoundry:Endpoint                    https://<account>.services.ai.azure.com/
Azure:AiFoundry:ChatDeploymentName          gpt-4o-mini
Azure:AiFoundry:EmbeddingDeploymentName     text-embedding-3-small
Azure:AiFoundry:EmbeddingModelName          text-embedding-3-small
Azure:AiFoundry:EmbeddingDimensions         1536
Azure:AiFoundry:Agent:ProjectEndpoint       (leave empty if the account has one project)
```

`Agent:ProjectEndpoint` may be left empty while the account hosts exactly one
project, because the account endpoint resolves to it. Set it explicitly as soon
as there is a second, or the agent resolves against whichever one Azure picks.

### Pin the agent version before production

Left empty, `Azure:AiFoundry:Agent:Version` makes the running process resolve or
create an agent version on the first question — convenient for a first run, and
wrong for production, because it requires giving the application **write**
permission on the project.

```
1. Run once with Azure AI Project Manager granted.
2. Read the version from the log:
     Created Foundry agent 'knowledge-assistant' version 3 (definition a1b2c3…)
3. Set Azure:AiFoundry:Agent:Version=3
4. Remove the Azure AI Project Manager assignment.
```

With a version pinned the application lists nothing and creates nothing, and
`Azure AI User` alone is sufficient. It also puts the agent's instructions and
tool schema under deployment control rather than leaving them to whatever the
process decided at boot.

---

## 5. Managed identity

The identity is what replaces every credential this service does not have.
`DefaultAzureCredential` resolves to your `az login` on a laptop and to the
platform-assigned identity in Azure — the same code path in both.

**System-assigned** is the default and the simpler choice: it is created with the
app, deleted with it, and cannot be attached to anything else.

```bash
az containerapp identity assign \
  --name $APP --resource-group $RG --system-assigned
```

**User-assigned** is right when several services must share one identity, or when
the identity must outlive any single app. It requires one extra setting, because
a container can carry more than one identity and the SDK cannot guess which:

```
Azure:Credential:ManagedIdentityClientId = <client-id-of-the-user-assigned-identity>
```

A client id is not a secret. Leaving this empty selects the system-assigned
identity; setting it to an empty string is **not** the same as leaving it unset —
the code treats a blank value as a deliberate request for a user-assigned
identity with a blank id, and the whole credential chain fails.

---

## 6. Required RBAC roles

Data-plane roles, not management-plane. `Owner` and `Contributor` on the
subscription grant neither — they let you *manage* a storage account without
letting you *read a blob*, which is the most common and most confusing source of
`403`s here.

Capture the principal id once:

```bash
PRINCIPAL=$(az containerapp identity show \
  --name $APP --resource-group $RG --query principalId -o tsv)
```

### The application's identity

| Resource | Role | Needed for |
|---|---|---|
| Storage account | `Storage Blob Data Contributor` | Upload and re-read documents |
| AI Search | `Search Index Data Contributor` | Write and query index documents |
| AI Search | `Search Service Contributor` | Create the two indexes on first use |
| AI Foundry | `Cognitive Services OpenAI User` | Embeddings and chat completions |
| AI Foundry project | `Azure AI User` | Run the agent |
| AI Foundry project | `Azure AI Project Manager` | **Only** while the agent version is unpinned — see §4 |
| Document Intelligence | `Cognitive Services User` | Only if an endpoint is configured |

```bash
STORAGE_ID=$(az storage account show -n $STORAGE -g $RG --query id -o tsv)
SEARCH_ID=$(az search service show -n $SEARCH -g $RG --query id -o tsv)
FOUNDRY_ID=$(az cognitiveservices account show -n $FOUNDRY -g $RG --query id -o tsv)

az role assignment create --assignee $PRINCIPAL \
  --role "Storage Blob Data Contributor" --scope $STORAGE_ID
az role assignment create --assignee $PRINCIPAL \
  --role "Search Index Data Contributor" --scope $SEARCH_ID
az role assignment create --assignee $PRINCIPAL \
  --role "Search Service Contributor"    --scope $SEARCH_ID
az role assignment create --assignee $PRINCIPAL \
  --role "Cognitive Services OpenAI User" --scope $FOUNDRY_ID
az role assignment create --assignee $PRINCIPAL \
  --role "Azure AI User"                  --scope $FOUNDRY_ID
```

`Search Service Contributor` is only needed because the application creates its
indexes on demand. If you provision both indexes out of band, drop it — the
running service then has no permission to alter the schema it queries, which is
the better arrangement for production.

### The search service's identity — the step people miss

`Azure:Search:EnableVectorizer` declares integrated vectorization on the chunk
index, so the search service can embed a query string itself at query time. That
call is made by the **search service's** identity, not the application's.
Granting the application `Cognitive Services OpenAI User` does nothing for it.

| Identity | Resource | Role |
|---|---|---|
| Search service | AI Foundry | `Cognitive Services OpenAI User` |

```bash
SEARCH_PRINCIPAL=$(az search service show \
  -n $SEARCH -g $RG --query identity.principalId -o tsv)

az role assignment create --assignee $SEARCH_PRINCIPAL \
  --role "Cognitive Services OpenAI User" --scope $FOUNDRY_ID
```

The index is created successfully without this, because the vectorizer is only
exercised by queries. So a missing grant surfaces later, as a failing search
rather than a failing deployment — which is exactly why it is easy to miss.

### Your own account, for local development

Grant yourself the same roles on the same scopes. `DefaultAzureCredential` uses
your `az login` locally, so your account needs precisely what the managed
identity needs:

```bash
ME=$(az ad signed-in-user show --query id -o tsv)

az role assignment create --assignee $ME --role "Storage Blob Data Contributor"  --scope $STORAGE_ID
az role assignment create --assignee $ME --role "Search Index Data Contributor"  --scope $SEARCH_ID
az role assignment create --assignee $ME --role "Search Service Contributor"     --scope $SEARCH_ID
az role assignment create --assignee $ME --role "Cognitive Services OpenAI User" --scope $FOUNDRY_ID
az role assignment create --assignee $ME --role "Azure AI User"                  --scope $FOUNDRY_ID
```

---

## 7. Deploy the container

The published image is multi-arch (`linux/amd64`, `linux/arm64`) and runs as a
non-root user on port 8080.

```
ghcr.io/metintopcu/knowledgeassistant:latest
```

Prefer a version tag over `latest` in production: `latest` moves, and a rollback
you cannot name is not a rollback.

### Azure Container Apps

```bash
az containerapp create \
  --name $APP \
  --resource-group $RG \
  --environment cae-knowledge-assistant \
  --image ghcr.io/metintopcu/knowledgeassistant:latest \
  --target-port 8080 \
  --ingress external \
  --system-assigned \
  --min-replicas 1 \
  --env-vars \
    Azure__Storage__ServiceUri="https://$STORAGE.blob.core.windows.net/" \
    Azure__Search__Endpoint="https://$SEARCH.search.windows.net/" \
    Azure__AiFoundry__Endpoint="https://$FOUNDRY.services.ai.azure.com/" \
    Azure__AiFoundry__ChatDeploymentName="gpt-4o-mini" \
    Azure__AiFoundry__EmbeddingDeploymentName="text-embedding-3-small"
```

Then configure the probes. They are deliberately different endpoints, and using
one for both is a real availability bug:

| Probe | Path | Why |
|---|---|---|
| Liveness | `/health/live` | Never contacts a dependency. It decides whether to **restart**, and restarting cannot fix someone else's outage — pointing it at `/health/ready` turns a dependency incident into a cluster-wide crash loop |
| Readiness | `/health/ready` | Contacts dependencies. It decides whether to **route traffic**, which is exactly the right response to an unreachable index |
| Startup | `/health/live` | Gives the host time to bind before liveness begins |

`min-replicas 1` because scale-to-zero costs a cold start on the first question,
and the agent path is already the slower of the two.

### Azure App Service

```bash
az webapp create \
  --name $APP --resource-group $RG --plan asp-knowledge-assistant \
  --deployment-container-image-name ghcr.io/metintopcu/knowledgeassistant:latest

az webapp config appsettings set --name $APP --resource-group $RG --settings \
  WEBSITES_PORT=8080 \
  Azure__Storage__ServiceUri="https://$STORAGE.blob.core.windows.net/" \
  Azure__Search__Endpoint="https://$SEARCH.search.windows.net/" \
  Azure__AiFoundry__Endpoint="https://$FOUNDRY.services.ai.azure.com/" \
  Azure__AiFoundry__ChatDeploymentName="gpt-4o-mini" \
  Azure__AiFoundry__EmbeddingDeploymentName="text-embedding-3-small"

az webapp identity assign --name $APP --resource-group $RG
```

`WEBSITES_PORT=8080` is required — App Service defaults to probing port 80, and
the container binds 8080 because a non-root user cannot bind below 1024.

### Publishing from CI

`.github/workflows/publish.yml` pushes to GHCR using the automatic
`GITHUB_TOKEN`, so there is no registry secret. To push to Azure Container
Registry instead, replace the login step with `azure/login@v2` using **OIDC
federated credentials** and grant `id-token: write`. Do not add a registry
password as a repository secret: federated credentials exist precisely so a
long-lived one does not have to.

---

## 8. Configuration reference

Configuration comes from environment variables in Azure. Nesting uses double
underscores — `Azure__Search__Endpoint` is `Azure:Search:Endpoint`. A single
underscore does **not** nest, and a mistyped key binds nothing.

| Variable | Required | Notes |
|---|---|---|
| `Azure__Storage__ServiceUri` | Yes | Account URI, not a connection string |
| `Azure__Storage__DocumentsContainer` | No | Defaults to `documents` |
| `Azure__Search__Endpoint` | Yes | |
| `Azure__Search__IndexName` | No | Defaults to `knowledge-index` |
| `Azure__Search__ChunkIndexName` | No | Defaults to `knowledge-chunks` |
| `Azure__AiFoundry__Endpoint` | Yes | |
| `Azure__AiFoundry__ChatDeploymentName` | Yes | Deployment, not model |
| `Azure__AiFoundry__EmbeddingDeploymentName` | Yes | Deployment, not model |
| `Azure__AiFoundry__EmbeddingDimensions` | No | Must match the model and the index |
| `Azure__AiFoundry__Agent__ProjectEndpoint` | No | Required once the account has >1 project |
| `Azure__AiFoundry__Agent__Version` | No | **Set in production** — see §4 |
| `Azure__DocumentIntelligence__Endpoint` | No | Empty selects local PdfPig |
| `Azure__Credential__ManagedIdentityClientId` | No | User-assigned identity only |
| `Observability__AzureMonitor__ConnectionString` | No | Ingestion-scoped, but still not for source control |

Every section is validated at startup. A missing required value fails the
process with a message naming the setting, so a misconfigured deployment fails
visibly instead of on the first request that reaches storage.

`appsettings.Example.json` and `.env.example` document the full surface.

---

## 9. Verify the deployment

```bash
URL=$(az containerapp show -n $APP -g $RG \
  --query properties.configuration.ingress.fqdn -o tsv)

# Liveness — should be 200 as soon as the process is up
curl -s -o /dev/null -w "live:  %{http_code}\n" https://$URL/health/live

# Readiness — 200 only once every dependency answers
curl -s -o /dev/null -w "ready: %{http_code}\n" https://$URL/health/ready

# End to end
curl -F "file=@handbook.pdf" https://$URL/api/documents
curl -H "Content-Type: application/json" \
     -d '{"question":"What does the handbook say about refunds?","topK":5}' \
     https://$URL/api/questions
```

A `200` from `/health/live` with a `503` from `/health/ready` is the signature of
a role assignment that has not propagated or is missing. Turn on
`Observability__HealthChecks__ExposeDetails` **temporarily** to see which
dependency is failing — it enumerates your dependencies to anyone who can reach
the endpoint, so turn it off again.

---

## 10. Troubleshooting

| Symptom | Cause |
|---|---|
| `403` from storage, search, or Foundry | A data-plane role is missing or has not propagated. `Owner` is not sufficient — see §6 |
| Searches return nothing after a successful ingest | The **search service's** identity is missing `Cognitive Services OpenAI User`. §6 |
| `404` from AI Foundry with correct-looking config | A model name was used where a deployment name is required. §4 |
| `429` under load | Deployment capacity. The retry pipeline handles bursts; sustained throttling is a quota increase |
| Startup fails naming a setting | `ValidateOnStart` working as intended. The message names the missing key |
| `CredentialUnavailableException` locally | Not logged in. Run `az login` |
| `CredentialUnavailableException` in Azure | No identity assigned, or `ManagedIdentityClientId` set to an empty string rather than left unset. §5 |
| Agent fails with `Agent.ProvisioningFailed` | Missing `Azure AI Project Manager` while the version is unpinned, or a pinned version that no longer exists. §4 |
| `WRN Failed to determine the https port for redirect` | Expected and harmless behind TLS-terminating ingress. Container Apps and App Service terminate TLS at the edge and forward HTTP, so `UseHttpsRedirection` finds no port to redirect to and passes the request through. Requests are still HTTPS end to end from the client |
| Vectors mismatch the index | `EmbeddingDimensions` changed, or the deployment was repointed at another model. Vector length is fixed at index creation; rebuild the index |

---

## Related

| | |
|---|---|
| [README.md](README.md) | Overview, endpoints, local setup |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Layer rules and the reasoning behind them |
| [CONFIGURATION.md](CONFIGURATION.md) | Provider precedence, user secrets, `.gitignore` guarantees |
