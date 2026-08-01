?# Api / Configuration

Strongly-typed options classes for settings the **web host itself** owns — CORS
policies, rate limits, API versioning, authentication parameters — together with
their validators and binding extensions.

> **Corrected in Sprint 3.** This README originally listed `BlobStorageOptions`,
> `AzureSearchOptions`, and `AiFoundryOptions` as living here. That was wrong and
> unimplementable: those options configure Infrastructure adapters, and
> Infrastructure cannot reference the API project without inverting the
> dependency rule. **Options belong beside the adapter that consumes them** —
> `BlobStorageOptions` now sits in `Infrastructure/Azure/Blob/`. The rule of
> thumb: if the only code that reads a setting lives in Infrastructure, the
> options class lives there too.

**Why it exists:** the alternative is `IConfiguration["Azure:Search:Endpoint"]`
scattered through the codebase, where a typo is invisible until runtime and the
full set of required settings is knowable only by grepping. A typed options
class makes configuration a **compile-time contract** and a single source of
truth for what an environment must supply.

**Rule:** validate at startup with `.ValidateDataAnnotations().ValidateOnStart()`
so a misconfigured deployment fails immediately and visibly rather than
degrading under traffic. Inject `IOptions<T>` (or `IOptionsMonitor<T>` when you
genuinely need hot reload) — never `IConfiguration` — into services.

**Never commit secrets.** Use user-secrets locally, managed identity plus Key
Vault in Azure. These classes hold endpoints, deployment names, and tuning
values; credentials come from the credential chain, not from a settings file.

**See `CONFIGURATION.md` at the repo root** for the provider precedence chain,
the `Azure:Search:Endpoint` � `Azure__Search__Endpoint` environment-variable
mapping, and the RBAC roles each environment needs.
