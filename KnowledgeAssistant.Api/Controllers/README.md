?# Api / Controllers

Attribute-routed MVC controllers — the versioned public REST surface
(`DocumentsController`, `SearchController`, `KnowledgeBasesController`).

**Why it exists:** controllers are an **adapter over HTTP**, and nothing more.
They bind and model-bind the request, send a command or query through MediatR,
and translate the result into a status code. That is the whole job.

**Rule:** a controller action should be a handful of lines. It must contain no
business logic, no orchestration across multiple handlers, no `if` chain
deciding what the operation means, and no direct Infrastructure reference. If an
action needs two handlers to complete one user intent, that intent is a missing
use case — model it in Application. Return typed results and declare
`[ProducesResponseType]` so the OpenAPI document is honest.
