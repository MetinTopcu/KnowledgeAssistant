# KnowledgeAssistant.Web

The web client for KnowledgeAssistant, implementing [`docs/DESIGN.md`](../docs/DESIGN.md).

It is a **query console**, not a chat client — a distinction the API enforces
rather than merely suggests: `POST /api/questions` retains no conversation
between calls, so there is no thread to render.

---

## Running it

```bash
npm install
cp .env.example .env.development   # non-secret local defaults
npm run dev
```

The dev server listens on <http://localhost:5173> and proxies `/api` and
`/health` to the API on <http://localhost:5111>, so the API must be running for
anything to answer. Requests are proxied rather than sent cross-origin because
the API configures no CORS policy, and adding one so a dev server can reach it
would put a permanent hole in the production surface to solve a local problem.

| Script | Does |
|---|---|
| `npm run dev` | Vite dev server with the API proxy |
| `npm run build` | Type-checks the project, then builds to `dist/` |
| `npm run typecheck` | Types only |
| `npm run lint` | ESLint, including the architecture rules |
| `npm run format` / `format:check` | Prettier, with Tailwind class sorting |

`.env.example` documents every variable the client reads and is the committed
template; `.env` and `.env.*` are git-ignored, mirroring how the API commits
`appsettings.Example.json` and ignores the real one. Copy it before the first
run — `src/shared/config/env.ts` validates the result at startup and fails with
a message naming the missing variables rather than breaking on the first
request that needs one.

Nothing prefixed `VITE_` may ever hold a credential: the browser can read all of
them. The client authenticates to the API; only the API authenticates to Azure,
and it does so with a managed identity.

---

## How it is arranged

```
src/
  app/        composition root: shell, providers, router — no product behaviour
  features/   one folder per vertical slice; features never import each other
  shared/     what more than one feature needs; never imports from features/
  styles/     the design token system
```

Each folder carries a `README.md` explaining what belongs there, following the
convention the .NET projects use.

Two rules are enforced by lint rather than by memory: **no inline styles** (use
the tokens), and **no relative parent imports** (use the `@/` alias). Both are
in `eslint.config.js` as `no-restricted-syntax` and `no-restricted-imports`.

---

## Design tokens

`src/styles/globals.css` is the whole system, in three tiers — primitive,
semantic, utility. Components reference the semantic tier only; a component that
names a `--gray-*` or `--blue-*` variable has hard-coded a light-theme value
into a themeable product.

The constraints that keep the interface from drifting are all expressed there:
nothing has a corner radius above 6px, elevation is reserved for menus and
dialogs (everything else separates with a 1px border), and hue is only ever
status — green, amber, red — or interactive blue.

Dark is the default theme, set as a class on `<html>` in `index.html` so the
first paint is already correct. `ThemeProvider` takes over from there.

---

## Talking to the API

`shared/api/httpClient.ts` is the only module that names Axios. It does two
things so that no feature has to remember them:

- stamps an outgoing `X-Correlation-Id`, chosen client-side so a request that
  times out or never connects is still reportable against something the server
  can search;
- converts every failure into an `ApiRequestError` carrying a normalised
  `ApiProblem`.

That normalisation matters more than it looks, because the API reports its
machine-readable error code in two different places:

| Failure | Shape | Where the code is |
|---|---|---|
| Validation (400, 413) | `ValidationProblemDetails` | the **keys** of `errors` |
| Everything else | `ProblemDetails` | `title` |

Branch on the code, never on the message — the messages are prose and will
change. `shared/api/apiProblem.ts` flattens both into one list.

---

## Notes on the dependencies

**React Router is held at the latest v7 (7.18.2), and `npm audit` is clean.**
It was not always: audit used to report GHSA-qwww-vcr4-c8h2, a CSRF bypass in
**RSC mode**, which this client cannot reach — it uses `createBrowserRouter` in
data mode with no RSC. The "fix" npm proposed at the time was 7.11.0, which
carries eight advisories that *are* reachable from a SPA: XSS, open redirect,
and RCE via a vendored `turbo-stream`. The note is kept because the reasoning
outlives the advisory: an audit finding is a question about reachability, not
an instruction, and downgrading into eight reachable holes to close one
unreachable one is the wrong trade.

**Icons are Lucide, not Fluent.** `docs/DESIGN.md` §10 specifies Fluent UI
System Icons for their Azure lineage; Lucide was set as a stack requirement
afterwards. Sizes, stroke weight, and the rule that no icon appears without a
label or tooltip are unchanged — only the source of the glyphs.

**`Button` covers `IconButton`.** The design inventory lists them separately;
here it is a `size="icon"` variant, because they differed only in dimensions.
