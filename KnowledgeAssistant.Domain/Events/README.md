?# Domain / Events

Facts that have already happened, raised by aggregates and named in the past
tense: `DocumentIngestedEvent`, `IndexRebuildRequestedEvent`,
`DocumentClassifiedEvent`.

**Why it exists:** events are how an aggregate announces a change without
knowing who cares. That decoupling is what lets you add re-indexing, auditing,
or a notification without reopening the aggregate that triggered them —
Open/Closed at the architectural scale.

**Rule:** immutable, serialisable, and carrying identifiers plus the minimum
payload. They record *what happened*, never *what should happen next*; the
reaction is an Application concern. Raise them into the aggregate's event
collection and let the Application layer dispatch after the unit of work
commits — never dispatch from inside the Domain.
