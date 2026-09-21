# Screenshots

The README references these five filenames. Drop the images in and they render
with no edit to the README:

| File | What to capture |
|---|---|
| `upload.png` | `POST /api/documents` — the response showing document id, blob URI, and chunk count |
| `ask.png` | `POST /api/questions` — an answer with its numbered citations |
| `agent.png` | `POST /api/questions/agent` — an answer showing `searchCount` |
| `trace.png` | One request in Application Insights or the Aspire dashboard, spanning embedding → search → completion |
| `health.png` | `/health` with per-dependency status |

The README does not reference them until they exist: a broken image in a
portfolio landing page reads as neglect, and a screenshot of a UI that was never
run reads as worse. Add the files, then add the table.

**Check every image before committing it.** A screenshot of a real environment
will show endpoint hostnames, resource names, document ids, and — in a trace
view — request payloads. None of that is a credential, but resource names and
corpus content are not things a public repository needs to disclose. Redact, or
capture against throwaway resources with sample documents.
