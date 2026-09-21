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
8. [GitHub Actions → ACR with OIDC](#8-github-actions--acr-with-oidc)
9. [Configuration reference](#9-configuration-reference)
10. [Verify the deployment](#10-verify-the-deployment)
11. [Troubleshooting](#11-troubleshooting)

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

The search service itself needs no managed identity: the service-side vectorizer
is disabled, so it never calls AI Foundry (§6).

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

The verified pair is `text-embedding-3-large` (3072-dimensional vectors) and
`gpt-5-mini`. Reuse existing deployments of them if the account has them —
`az cognitiveservices account deployment list -n $FOUNDRY -g $RG` — rather than
creating duplicates.

```bash
az cognitiveservices account deployment create \
  --name $FOUNDRY --resource-group $RG \
  --deployment-name text-embedding-3-large \
  --model-name text-embedding-3-large \
  --model-version 1 --model-format OpenAI \
  --sku-capacity 120 --sku-name Standard

az cognitiveservices account deployment create \
  --name $FOUNDRY --resource-group $RG \
  --deployment-name gpt-5-mini \
  --model-name gpt-5-mini \
  --model-version 2025-08-07 --model-format OpenAI \
  --sku-capacity 50 --sku-name GlobalStandard
```

`gpt-5-mini` is a reasoning model: it accepts only its default temperature, which
is why `ChatTemperature` and `Agent:Temperature` are unset by default and no
temperature is sent. The embedding dimension is checked against the model at
startup; `knowledge-chunks` is created with it.

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
Azure:AiFoundry:ChatDeploymentName          gpt-5-mini
Azure:AiFoundry:EmbeddingDeploymentName     text-embedding-3-large
Azure:AiFoundry:Agent:ProjectEndpoint       https://<account>.services.ai.azure.com/api/projects/<project>
Azure:AiFoundry:Agent:Version               <pinned version, e.g. 2>
```

`EmbeddingModelName` / `EmbeddingDimensions` default to `text-embedding-3-large`
/ `3072`; set both only for a different embedding model.

`Agent:ProjectEndpoint` is **required**, even for a single-project account. The
account endpoint answered 404 to every agent call against a live one-project
account, so there is no fallback: an empty value, or one without an
`/api/projects/<name>` path, stops the host at startup.

### Pin the agent version

`Azure:AiFoundry:Agent:Version` is **required outside Development**. Only a
Development host may leave it empty, in which case it provisions (or reuses) a
version matching its definition on the first question — which needs the
**write** role `Azure AI Project Manager` on the project, granted to a
developer, never to the deployed identity.

```
1. In Development, ask one agent question.
2. Read the version from the log:
     Created Foundry agent 'knowledge-assistant' version 2 (definition 17b3f4…)
3. Set Azure__AiFoundry__Agent__Version=2 on the deployed app.
```

The deployed app then reads that one version on first use and compares its
stored definition fingerprint with the one the build computes. A match logs
`Using pinned Foundry agent ... matches this build`; a mismatch — different
instructions, tool schema, model deployment, or temperature — fails the agent
route with `Agent.ProvisioningFailed` and a log naming both fingerprints, rather
than silently running an agent this code was not written against. The deployed
identity needs only `Azure AI User`.

---

## 5. Managed identity

The identity is what replaces every credential this service does not have.
In Azure the application uses `DefaultAzureCredential`, which resolves to the
platform-assigned identity. Only in the `Development` environment does it use a
single developer credential instead: `AzureCliCredential` for `dotnet run`, or
`ManagedIdentityCredential` against the local `azure-token-proxy` under Docker
Compose (`Azure:Credential:DevelopmentCredential`). Neither can be selected in a
deployed environment.

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

| Resource | Role | Scope | Needed for |
|---|---|---|---|
| Storage | `Storage Blob Data Contributor` | The documents **container** | Upload and re-read documents |
| AI Search | `Search Index Data Contributor` | Search service | Write and query index documents |
| AI Search | `Search Service Contributor` | Search service | Read, and on first use create, the two indexes |
| AI Foundry | `Azure AI User` (portal: **Foundry User**) | The **account** | Embeddings, chat, Document Intelligence, and the agent |
| Container registry | `AcrPull` | The registry | Pull the image with the app's identity |

`Azure AI User` carries the account's data actions (OpenAI inference, Document
Intelligence, agents), so `Cognitive Services OpenAI User` and
`Cognitive Services User` would be redundant. It must be on the account, not the
project: chat, embeddings, and Document Intelligence are account-level
endpoints. `Azure AI Project Manager` is never granted to the deployed identity —
the agent version is pinned (§4).

```bash
STORAGE_ID=$(az storage account show -n $STORAGE -g $RG --query id -o tsv)
SEARCH_ID=$(az search service show -n $SEARCH -g $RG --query id -o tsv)
FOUNDRY_ID=$(az cognitiveservices account show -n $FOUNDRY -g $RG --query id -o tsv)
ACR_ID=$(az acr show -n $ACR --query id -o tsv)

az role assignment create --assignee-object-id $PRINCIPAL --assignee-principal-type ServicePrincipal \
  --role "Storage Blob Data Contributor" \
  --scope "$STORAGE_ID/blobServices/default/containers/$CONTAINER"
az role assignment create --assignee-object-id $PRINCIPAL --assignee-principal-type ServicePrincipal \
  --role "Search Index Data Contributor" --scope $SEARCH_ID
az role assignment create --assignee-object-id $PRINCIPAL --assignee-principal-type ServicePrincipal \
  --role "Search Service Contributor"    --scope $SEARCH_ID
az role assignment create --assignee-object-id $PRINCIPAL --assignee-principal-type ServicePrincipal \
  --role "Azure AI User"                 --scope $FOUNDRY_ID
az role assignment create --assignee-object-id $PRINCIPAL --assignee-principal-type ServicePrincipal \
  --role "AcrPull"                       --scope $ACR_ID
```

`Search Service Contributor` is only needed because the application creates its
indexes on demand. If you provision both indexes out of band, drop it — the
running service then has no permission to alter the schema it queries, which is
the better arrangement for production.

### The search service's identity — none required

`Azure:Search:EnableVectorizer` is `false` and the service-side Azure OpenAI
vectorizer is not used. The application generates query embeddings itself and
submits a `VectorizedQuery`, so the search service never calls AI Foundry: it
needs no managed identity and no `Cognitive Services OpenAI User` assignment.

### Your own account, for local development

Grant yourself the same roles on the same scopes. In `Development` the
application authenticates as your `az login`, so your account needs precisely
what the managed identity needs:

```bash
ME=$(az ad signed-in-user show --query id -o tsv)

az role assignment create --assignee $ME --role "Storage Blob Data Contributor" \
  --scope "$STORAGE_ID/blobServices/default/containers/$CONTAINER"
az role assignment create --assignee $ME --role "Search Index Data Contributor"  --scope $SEARCH_ID
az role assignment create --assignee $ME --role "Search Service Contributor"     --scope $SEARCH_ID
az role assignment create --assignee $ME --role "Azure AI User"                  --scope $FOUNDRY_ID
# Only while provisioning an agent version in Development:
az role assignment create --assignee $ME --role "Azure AI Project Manager"       --scope $FOUNDRY_ID/projects/$PROJECT
```

---

## 7. Deploy the container

The image runs as a non-root user on port 8080 and contains no Azure CLI, no
SDK, and no source — only the published application on the ASP.NET runtime.

CI builds and publishes it on every push to `master`, to the existing Azure
Container Registry, tagged with the commit SHA (§8):

```
<registry>.azurecr.io/knowledge-assistant-api:<commit-sha>
```

Deploy by SHA tag or by digest, never by a moving tag: a rollback you cannot
name is not a rollback. ACR is the only registry this repository publishes to.

### Azure Container Apps

This is the path that was deployed and verified end to end (upload, RAG, agent)
with the app's managed identity. It was driven through ARM (`az rest`) because
the `containerapp` CLI extension failed to install on the machine used; the
`az containerapp` commands are equivalent but were not the ones exercised.

**1. Environment** — Consumption only, no Log Analytics (no fixed cost; the
console log stream still works). Register the provider once per subscription.

```bash
az provider register -n Microsoft.App --wait
ENV_ID=/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.App/managedEnvironments/cae-knowledge-assistant
az rest --method put --url "https://management.azure.com$ENV_ID?api-version=2024-03-01" \
  --body '{"location":"'$LOCATION'","properties":{"zoneRedundant":false}}'
```

**2. Image** — build with the repository `Dockerfile` and push to an existing
registry. The app pulls it with its own identity (`AcrPull`), so the registry
needs no admin user and the app holds no registry password.

```bash
docker build -t $ACR.azurecr.io/knowledge-assistant-api:$TAG .
az acr login -n $ACR
docker push $ACR.azurecr.io/knowledge-assistant-api:$TAG
```

**3. App, in two steps.** A system-assigned identity does not exist until the
app does, and the app cannot pull a private image until that identity holds
`AcrPull`. So create it first with a public placeholder image
(`mcr.microsoft.com/k8se/quickstart:latest`) and `"identity":{"type":"SystemAssigned"}`,
read `identity.principalId`, assign the §6 roles, then PUT the real definition:

```jsonc
{
  "location": "<location>",
  "identity": { "type": "SystemAssigned" },
  "properties": {
    "managedEnvironmentId": "<ENV_ID>",
    "configuration": {
      "activeRevisionsMode": "Single",
      "ingress": {
        "external": true, "targetPort": 8080, "allowInsecure": false,
        // The API has no user authentication (docs/DESIGN.md): restrict who can reach it.
        "ipSecurityRestrictions": [ { "name": "allow-dev", "ipAddressRange": "<your-ip>/32", "action": "Allow" } ]
      },
      "registries": [ { "server": "<acr>.azurecr.io", "identity": "system" } ],
      // Ingestion-scoped, but not for source control: a Container Apps secret.
      "secrets": [ { "name": "appinsights-connection-string", "value": "<connection string>" } ]
    },
    "template": {
      "containers": [ {
        "name": "api",
        "image": "<acr>.azurecr.io/knowledge-assistant-api:<tag>",
        "resources": { "cpu": 0.5, "memory": "1Gi" },
        "env": [
          { "name": "Azure__Storage__ServiceUri", "value": "https://<storage>.blob.core.windows.net/" },
          { "name": "Azure__Storage__DocumentsContainer", "value": "<container>" },
          { "name": "Azure__Search__Endpoint", "value": "https://<search>.search.windows.net/" },
          { "name": "Azure__DocumentIntelligence__Endpoint", "value": "https://<foundry>.cognitiveservices.azure.com/" },
          { "name": "Azure__AiFoundry__Endpoint", "value": "https://<foundry>.services.ai.azure.com/" },
          { "name": "Azure__AiFoundry__ChatDeploymentName", "value": "gpt-5-mini" },
          { "name": "Azure__AiFoundry__EmbeddingDeploymentName", "value": "text-embedding-3-large" },
          { "name": "Azure__AiFoundry__Agent__ProjectEndpoint", "value": "https://<foundry>.services.ai.azure.com/api/projects/<project>" },
          { "name": "Azure__AiFoundry__Agent__Version", "value": "<pinned version>" },
          { "name": "Observability__AzureMonitor__ConnectionString", "secretRef": "appinsights-connection-string" }
        ],
        "probes": [
          { "type": "Startup",   "httpGet": { "path": "/health/live",  "port": 8080 }, "periodSeconds": 5,  "timeoutSeconds": 3, "failureThreshold": 12 },
          { "type": "Liveness",  "httpGet": { "path": "/health/live",  "port": 8080 }, "periodSeconds": 30, "timeoutSeconds": 3, "failureThreshold": 3 },
          { "type": "Readiness", "httpGet": { "path": "/health/ready", "port": 8080 }, "periodSeconds": 15, "timeoutSeconds": 6, "failureThreshold": 3 }
        ]
      } ],
      "scale": { "minReplicas": 0, "maxReplicas": 1 }
    }
  }
}
```

```bash
APP_ID=/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.App/containerApps/$APP
az rest --method put --url "https://management.azure.com$APP_ID?api-version=2024-03-01" \
  --headers "Content-Type=application/json" --body @containerapp.json
```

Keep the filled-in body out of the repository — it carries the connection
string — and delete it after the PUT. `ASPNETCORE_ENVIRONMENT` is not set: the
image defaults to `Production`, which is what selects `DefaultAzureCredential`
and requires the pinned agent version.

The probes are deliberately different endpoints, and using one for both is a
real availability bug:

| Probe | Path | Why |
|---|---|---|
| Liveness | `/health/live` | Never contacts a dependency. It decides whether to **restart**, and restarting cannot fix someone else's outage — pointing it at `/health/ready` turns a dependency incident into a cluster-wide crash loop |
| Readiness | `/health/ready` | Contacts dependencies. It decides whether to **route traffic**, which is exactly the right response to an unreachable index. Its 6 s timeout sits just above the application's own 5 s dependency budget, so the platform receives the app's verdict instead of cutting it off |
| Startup | `/health/live` | Gives the host time to bind before liveness begins |

**Scale.** `minReplicas 0` keeps an idle app free on a credit-limited
subscription, at the cost of a cold start on the first request after idle. Use
`minReplicas 1` where that latency matters more than the idle charge.

**Logs without Log Analytics.** The console stream is reachable through ARM:
`POST $APP_ID/getAuthtoken`, then `GET` the app's `eventStreamEndpoint` host at
`/subscriptions/.../containerApps/$APP/revisions/<rev>/replicas/<replica>/containers/api/logstream?tailLines=100`
with that token.

### Azure App Service

```bash
az webapp create \
  --name $APP --resource-group $RG --plan asp-knowledge-assistant \
  --deployment-container-image-name $ACR.azurecr.io/knowledge-assistant-api:$TAG

az webapp config appsettings set --name $APP --resource-group $RG --settings \
  WEBSITES_PORT=8080 \
  Azure__Storage__ServiceUri="https://$STORAGE.blob.core.windows.net/" \
  Azure__Search__Endpoint="https://$SEARCH.search.windows.net/" \
  Azure__AiFoundry__Endpoint="https://$FOUNDRY.services.ai.azure.com/" \
  Azure__AiFoundry__ChatDeploymentName="gpt-5-mini" \
  Azure__AiFoundry__EmbeddingDeploymentName="text-embedding-3-large" \
  Azure__AiFoundry__Agent__ProjectEndpoint="https://$FOUNDRY.services.ai.azure.com/api/projects/$PROJECT" \
  Azure__AiFoundry__Agent__Version="$AGENT_VERSION"

az webapp identity assign --name $APP --resource-group $RG
```

`WEBSITES_PORT=8080` is required — App Service defaults to probing port 80, and
the container binds 8080 because a non-root user cannot bind below 1024.

### Publishing from CI

`.github/workflows/acr-publish.yml` builds and pushes this image to the registry
on every push to `master`, after `ci.yml` (which it calls) passes, authenticating
with OIDC. §8 sets that up.

It stops there. **Nothing in CI deploys.** The Container App above keeps serving
the image it was last given until someone changes the `image` in its template by
hand.

---

## 8. GitHub Actions → ACR with OIDC

One workflow ([`acr-publish.yml`](.github/workflows/acr-publish.yml)) needs one
capability: push an image to one registry. This section creates an identity that
can do exactly that and nothing else, with no password in existence.

### Why federation rather than a secret

A service principal password in a repository secret is a long-lived credential
that works from anywhere, for anyone who can read it, until somebody remembers
to rotate it. A federated credential is a statement of trust instead: *tokens
issued by GitHub, for this repository, on this branch, may act as this
identity*. The runner presents a token minted for that single workflow run and
Entra ID exchanges it for an Azure token. Nothing is stored, so nothing can
leak, and a fork cannot use it — the subject would not match.

This is the same principle the application itself follows with managed identity.

### Create the identity

```bash
APP_NAME=github-knowledge-assistant-acr
REPO=<owner>/<repository>          # e.g. MetinTopcu/KnowledgeAssistant
BRANCH=master
ACR=<registry-name>
RG=<registry-resource-group>

# 1. An application and its service principal. No credential is created here —
#    `az ad app credential reset` is exactly what we are avoiding.
APP_ID=$(az ad app create --display-name $APP_NAME --query appId -o tsv)
az ad sp create --id $APP_ID

# 2. The federated credential: who may act as this identity, and from where.
#    The subject must match GitHub's token exactly. For a branch push it is
#    `repo:<owner>/<repo>:ref:refs/heads/<branch>` — a tag, a pull request, or
#    an environment each have a different subject and need their own credential.
az ad app federated-credential create --id $APP_ID --parameters "{
  \"name\": \"github-$BRANCH\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:$REPO:ref:refs/heads/$BRANCH\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# 3. One role, one scope: push to this registry. AcrPush includes pull, so no
#    second assignment is needed. Do NOT use Contributor, and do NOT assign at
#    resource-group or subscription scope — the identity would then be able to
#    change the Container App, which is precisely what this pipeline must not do.
ACR_ID=$(az acr show -n $ACR -g $RG --query id -o tsv)
SP_OBJECT_ID=$(az ad sp show --id $APP_ID --query id -o tsv)
az role assignment create \
  --assignee-object-id $SP_OBJECT_ID --assignee-principal-type ServicePrincipal \
  --role "AcrPush" --scope $ACR_ID

echo "AZURE_CLIENT_ID       $APP_ID"
echo "AZURE_TENANT_ID       $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID $(az account show --query id -o tsv)"
```

### Tell GitHub

Add the three values as repository secrets — *Settings → Secrets and variables →
Actions → New repository secret*:

| Secret | Value |
|---|---|
| `AZURE_CLIENT_ID` | The application's client id |
| `AZURE_TENANT_ID` | The tenant id |
| `AZURE_SUBSCRIPTION_ID` | The subscription id |

None of the three is a credential — they identify, they do not authorise. They
are secrets rather than variables only so a public build log does not carry
them.

**Do not add a registry username or password, and do not enable the registry's
admin user.** `az acr login` in the workflow exchanges the Entra token for a
short-lived registry token; an admin password would be a second, permanent way
in that nothing needs.

### What this identity cannot do

| | |
|---|---|
| Deploy or restart the Container App | No. It has no role on `Microsoft.App/*` |
| Change ingress, scale, or configuration | No |
| Read or write Blob Storage, Search, or Foundry | No — no data-plane role anywhere |
| Create or delete Azure resources | No |
| Delete images or the registry | No. `AcrPush` grants pull and push, not `AcrDelete` and not registry management |
| Act from another branch, a tag, or a fork | No. The token's subject would not match the federated credential |

### Verify it

Push to `master`, or run the workflow manually from `master`, and check that the
*Sign in to Azure with OIDC* step succeeds. Then:

```bash
az acr repository show-tags -n $ACR --repository knowledge-assistant-api -o table
```

The new commit SHA should be listed. Nothing about the running Container App
should have changed.

---

## 9. Configuration reference

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
| `Azure__AiFoundry__EmbeddingModelName` | No | Defaults to `text-embedding-3-large` |
| `Azure__AiFoundry__EmbeddingDimensions` | No | Defaults to `3072`; checked against a known model at startup; fixes the chunk index schema |
| `Azure__AiFoundry__ChatTemperature` | No | Unset sends none (required for reasoning models) |
| `Azure__AiFoundry__Agent__ProjectEndpoint` | **Yes** | `https://<account>.services.ai.azure.com/api/projects/<project>` — the account endpoint is rejected |
| `Azure__AiFoundry__Agent__Version` | **Yes** (outside Development) | Pinned and verified against the build — see §4 |
| `Azure__DocumentIntelligence__Endpoint` | No | Empty selects local PdfPig |
| `Azure__Credential__ManagedIdentityClientId` | No | User-assigned identity only |
| `Observability__AzureMonitor__ConnectionString` | No | Ingestion-scoped, but still not for source control |

Every section is validated at startup. A missing required value fails the
process with a message naming the setting, so a misconfigured deployment fails
visibly instead of on the first request that reaches storage.

`appsettings.Example.json` and `.env.example` document the full surface.

---

## 10. Verify the deployment

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

## 11. Troubleshooting

| Symptom | Cause |
|---|---|
| `403` from storage, search, or Foundry | A data-plane role is missing or has not propagated. `Owner` is not sufficient — see §6 |
| `404` from AI Foundry with correct-looking config | A model name was used where a deployment name is required. §4 |
| `429` under load | Deployment capacity. The retry pipeline handles bursts; sustained throttling is a quota increase |
| Startup fails naming a setting | `ValidateOnStart` working as intended. The message names the missing key |
| `CredentialUnavailableException` locally | Not logged in, or `az` not on `PATH`. `Development` uses the Azure CLI only. Run `az login` |
| `Agent.ProvisioningFailed`, log says `does not exist in the project` | The pinned `Agent:Version` is not in that project. §4 |
| `Agent.ProvisioningFailed`, log says `was created from definition ... but this build defines ...` | The pinned version was provisioned from different instructions, tool schema, model deployment, or temperature. Provision in Development and pin the new version. §4 |
| `400 unsupported_value` naming `temperature` | A temperature is set for a reasoning model. Unset `ChatTemperature` / `Agent:Temperature` |
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
