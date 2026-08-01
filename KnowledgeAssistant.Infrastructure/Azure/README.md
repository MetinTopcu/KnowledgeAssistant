?# Infrastructure / Azure

Azure SDK plumbing and the adapters for Azure services, in per-service
sub-folders:

- `Common/` — `TokenCredential` setup (`DefaultAzureCredential` /
  managed identity), the `Microsoft.Extensions.Azure` client factory
  registration, shared retry and resilience options.
- `Blob/` — the `IBlobStorageService` adapter over `Azure.Storage.Blobs`
  (raw document upload/download, SAS issuance, container conventions).
- `AiFoundry/` — the `IChatCompletionService` / embedding adapter over
  Azure AI Foundry, including deployment names, prompt transport, and
  token-usage capture.

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
