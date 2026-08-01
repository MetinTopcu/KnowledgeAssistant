# Application / Common

Cross-cutting helpers used *by* this layer and no other: `Result<T>` / `Error`
if you route it through Application, `PagedResult<T>`, pagination and sorting
primitives, mapping profiles, application-level exceptions
(`NotFoundException`, `ForbiddenAccessException`), and shared constants.

**Why it exists:** without it these types get dumped into `DTOs/` or duplicated
per feature, and `PagedResult` ends up defined three times with subtly different
property names.

**Rule:** the standing risk is that `Common` becomes a junk drawer. A type earns
its place here only when **two or more** slices genuinely use it. Until then it
lives in the slice that needs it. Prefer small, focused sub-folders
(`Common/Pagination`, `Common/Mapping`) over a flat pile of unrelated helpers.
