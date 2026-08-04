# shared/

What more than one feature needs. Nothing here may import from `features/`.

| Folder | Holds | Notes |
|---|---|---|
| `api/` | The Axios client, the failure contract, the query client | `httpClient` is the only place Axios is named; features import the client, never the library |
| `components/` | Reusable presentational components | One component per file, named for what it is |
| `components/ui/` | shadcn/ui primitives, when one is genuinely needed | Generated code, excluded from lint |
| `config/` | Validated environment configuration | Fails at startup, not at first use |
| `forms/` | The React Hook Form ↔ Zod binding | |
| `hooks/` | Cross-feature React hooks | |
| `lib/` | Framework-free helpers | |
| `theme/` | Theme resolution and application | Kept out of the provider that calls it |

The failure contract in `api/apiProblem.ts` is worth reading before writing any
call: the server puts the machine-readable error code in different places
depending on whether the failure was a validation error, and that difference is
normalised there so no feature has to know about it.
