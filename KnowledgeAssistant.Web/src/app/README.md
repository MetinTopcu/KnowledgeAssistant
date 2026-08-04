# app/

The composition root. It wires things together and owns no product behaviour.

| Folder | Holds |
|---|---|
| `layout/` | The global shell — nav rail, top bar, status bar |
| `providers/` | Cross-cutting context, composed in `AppProviders` |
| `router/` | The route tree, the path constants, and the route-title hook |
| `routes/` | Routes that belong to no feature: the catch-all and the error boundary |

Feature screens are **not** registered here by hand-written route objects that
guess at a feature's shape. Each slice adds its own route to `router/appRouter.ts`
alongside the screen it renders, so a route and the thing it resolves to arrive
in the same change.

`routePaths.ts` explains why the health screen is routed at `/status`: the API
serves its own probes at `/health`, and a client route on that path would shadow
them behind the dev proxy and in any co-hosted deployment.
