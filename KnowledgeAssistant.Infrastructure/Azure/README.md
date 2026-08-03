?# Infrastructure / Azure

Azure SDK plumbing and the adapters for Azure services, in per-service
sub-folders:

- `Common/` — `TokenCredential` setup (`DefaultAzureCredential` /
  managed identity), the `Microsoft.Extensions.Azure` client factory
  registration, shared retry and resilience options.
- `Blob/` — the `IBlobStorageService` adapter over `Azure.Storage.Blobs`
  (raw document upload/download, SAS issuance, container conventions).
- `OpenAI/` — the `IEmbeddingService` and `IChatService` adapters over the
  Azure OpenAI surface of AI Foundry, plus the Polly retry pipeline both of
  them share.
- `Agents/` — the `IAgentService` adapter over Azure AI Foundry Agents: the
  agent's single retrieval tool, the run loop that answers its tool calls, and
  the fingerprinted version provisioning.
- `DocumentIntelligence/` — the OCR text extractor, used in place of the local
  PdfPig one when an endpoint is configured.

> These were originally scaffolded as one `AiFoundry/` folder. They were split
> once it was clear the two speak different protocols against the same resource:
> `OpenAI/` uses the OpenAI-compatible chat and embedding APIs, while `Agents/`
> uses the Foundry Agents control plane and the Responses API. They share an
> endpoint, a credential, and a retry budget — not a client library.

**Why it exists:** every reference to an Azure SDK type is confined here. That
containment is what makes the vendor a replaceable detail rather than a
structural commitment — and it means an Azure SDK major-version upgrade is a
diff in one folder.

**Why sub-folders rather than one flat folder:** each Azure service has its own
options, credential scope, and failure modes. Keeping them separate stops a
single `AzureServices.cs` from turning into a thousand-line grab bag.

**Rule:** register clients as **singletons** via `Microsoft.Extensions.Azure` —
the SDK clients are thread-safe and expensive to construct, and building one per
request will exhaust connections under load. Prefer managed identity over
connection strings.
