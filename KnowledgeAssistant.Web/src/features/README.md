# features/

One folder per vertical slice of the product, mirroring the API's slices on the
server. A feature owns everything it needs and nothing anyone else needs:

```
features/<feature>/
  api/          the endpoint calls and their TanStack Query hooks
  model/        schemas, types, and the logic that is not React
  components/   the screens and the parts they are composed from
```

Two rules keep the boundary real:

**A feature never imports from another feature.** Anything two features need
belongs in `shared/`. A cross-feature import is how the slice boundary quietly
stops existing, and it compiles perfectly.

**Business logic lives in `model/` and `api/`, never in a component.** A
component decides what to render; it does not decide what a low chunk yield
means or when a retry is permitted. That rule is what makes the logic testable
without mounting anything.

This folder is empty because no feature has been built yet. Screens are added
one slice at a time, per the plan in `docs/DESIGN.md` §6 — a route that renders
a stub is worse than a route that does not exist, because it looks finished.
