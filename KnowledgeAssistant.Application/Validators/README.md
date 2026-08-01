?# Application / Validators

FluentValidation `AbstractValidator<T>` classes for **input shape** validation:
required fields, string lengths, ranges, allowed file extensions, page sizes.

**Why it exists:** it moves guard clauses out of handlers and into declarative,
independently testable classes that `ValidationBehavior` applies automatically.
A request that reaches a handler is already structurally valid, so the handler
reads as pure use case.

**Note on placement:** validators may live *here* (grouped) or beside their
command in its feature folder — both are defensible; pick one and be consistent.
This scaffold favours co-locating them in the feature slice and reserving this
folder for **shared/reusable rules and custom extension methods**
(`RuleFor(x => x.Uri).MustBeAValidDocumentUri()`).

**Rule:** input validation only. A rule that needs to load an aggregate to
decide ("this document cannot be deleted while indexing") is a **business
invariant** and belongs in the Domain — enforcing it here duplicates the rule in
a place that cannot guarantee it.
