?# Infrastructure / Services

Implementations of the **non-Azure** ports from `Application/Interfaces`:
`SystemDateTimeProvider`, `CurrentUserService` (reading claims from the request
principal), `GuidProvider`, typed `HttpClient` wrappers, background job
scheduling, e-mail or webhook notification.

**Why it exists:** ports like `IDateTimeProvider` exist so that "expires after
30 days" is testable without waiting a month, and they need somewhere to live
that is not cluttered with the heavyweight cloud integrations. It also keeps
`Azure/` meaning exactly one thing.

**Rule:** these are adapters, so they stay thin — translate, delegate, and map
errors. Any decision worth a unit test has drifted out of a use case and belongs
in Application or Domain.
