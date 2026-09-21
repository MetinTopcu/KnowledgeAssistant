# KnowledgeAssistant — Product Design Specification

**Status:** Approved and partially implemented. This document remains the
specification — it describes the intended product, not the current build. What
exists today is `KnowledgeAssistant.Web`: the Ask console, the Agent screen, the
upload panel, Documents, Health, and Settings. Screens marked below as depending
on an API gap (§15) are **not** built, and the navigation rail deliberately does
not link to them: a rail entry for a screen that answers 404 is worse than a
short rail. Sign-in is not built either — the API has no user authentication.
**Version:** 1.0 · 2026-08-04
**Scope:** Complete product experience — journeys, screens, navigation, layout, components, and the full design token system.

---

## Contents

1. [Design thesis](#1-design-thesis)
2. [Principles](#2-principles)
3. [Users and jobs](#3-users-and-jobs)
4. [User journeys](#4-user-journeys)
5. [Information architecture and navigation](#5-information-architecture-and-navigation)
6. [Screen list](#6-screen-list)
7. [Layout system](#7-layout-system)
8. [Component inventory](#8-component-inventory)
9. [Design tokens](#9-design-tokens)
10. [Icons](#10-icons)
11. [Empty states](#11-empty-states)
12. [Loading states](#12-loading-states)
13. [Error states](#13-error-states)
14. [Keyboard model and accessibility](#14-keyboard-model-and-accessibility)
15. [API gaps this design depends on](#15-api-gaps-this-design-depends-on)
16. [Decisions needed before implementation](#16-decisions-needed-before-implementation)

---

## 1. Design thesis

> **This is not a chat product. It is a query console with an audit trail.**

The API makes this explicit. `QuestionsController` is named for questions rather than chat because *"that is what it does: one question, one grounded answer, no conversation retained between calls. Calling it a chat endpoint would promise a continuity it does not offer."*

Every design decision follows from that sentence:

| Because the API… | The UI… |
|---|---|
| retains no conversation | has no message thread, no bubbles, no bottom-anchored composer |
| returns every retrieved chunk, numbered, untruncated | shows evidence as a **sortable data table**, not as cards |
| reports `searchCount` so an ungrounded answer is *visible* | promotes that number to a first-class banner, not a footnote |
| makes two endpoints with different cost promises | forces an explicit mode choice, never a hidden toggle |
| returns a stable machine-readable error `code` | surfaces that code verbatim in every error, next to the correlation id |
| returns `TokenUsage` per run | shows cost on every run, like a CI job shows duration |

The mental model is **a SQL client, a CI run page, or the VS Code Search panel** — a place where you issue a query, inspect what came back, and check the receipts. Not a place where you have a conversation.

A run is a **record**: question, parameters, answer, evidence, cost, timing, correlation id. Records can be re-run, compared, permalinked, and pasted into a ticket.

---

## 2. Principles

**1. Borders, not shadows.** Separation comes from a 1px line and a canvas tint. Elevation is reserved for things that genuinely float above the page — menus and modals. Nothing else casts a shadow, ever.

**2. Density is respect.** The base font size is 14px and the base row height is 36px. Engineers scan; they do not stroll. Whitespace is used to group, never to impress.

**3. Hue carries meaning, never decoration.** Green, amber, and red mean healthy, degraded, and failed. Blue means interactive or selected. Nothing is colored because it looked flat. The score bars in the evidence table are monochrome.

**4. Show the receipts.** Every answer displays what it retrieved, what it cost, how long it took, and what its correlation id was. An answer with the evidence hidden behind a disclosure is an answer the product is asking you to trust.

**5. Two weights, two faces.** 400 and 600 in one sans and one mono. Hierarchy comes from size, color, and spacing. Nothing is 800, nothing is italic, nothing is letterspaced except 11px eyebrow labels.

**6. Motion confirms, never entertains.** 80–200ms, one easing curve, no spring, no bounce, no stagger, no scale-in. If motion were removed entirely, nothing would break.

---

## 3. Users and jobs

| Persona | Who | Primary job | What they need on screen |
|---|---|---|---|
| **The Asker** | Software engineer | "What does our documentation say about X, and can I trust it?" | Fast query, inline citations, one-click access to the exact source passage |
| **The Investigator** | Senior/staff engineer | "How do A and B relate across several documents?" | Agent mode, visibility into what the agent chose to search, and how many times |
| **The Operator** | DevOps / SRE | "Is retrieval degraded, and which dependency is at fault?" | Dependency health matrix, failure rate, token spend, correlation and trace ids |
| **The Curator** | Tech writer / platform owner | "Is the corpus complete and did it ingest correctly?" | Document list with chunk yield, low-yield warnings, upload with clear failure reasons |

Design for the Asker by default; make the Operator's data reachable in one keystroke; never let the Curator's tooling get in the Asker's way.

---

## 4. User journeys

### J1 — First run (cold tenant)

```
Sign in (Entra ID)
  → Ask console, corpus empty
  → Empty state: "No documents indexed" + [Upload a document]
  → Upload panel · select PDF (≤ 20 MB) · ingest stages run
  → Success row: "handbook.pdf · 148 chunks · 2.4 MB"
  → Composer becomes enabled, focus moves to it automatically
  → First question → first grounded answer
```
**Design obligation:** the empty state must not be a dead end. The upload affordance appears *inside* the disabled composer's helper row, not only in the Documents screen.

### J2 — The daily ask (Asker)

```
⌘K → type question → Enter
  → Run status line: embedding → searching → generating (elapsed timer)
  → Answer renders, citation marks [1] [3] inline
  → Click [3] → evidence row 3 highlights and expands; inspector opens with full chunk text
  → ⌘⇧C → run copied as Markdown with citations, ready for the PR description
```

### J3 — Verification (skeptical Asker)

```
Read answer → notice a claim that matters
  → j/k through evidence rows → Enter expands the full chunk (never truncated)
  → Compare model's claim against the passage it was shown
  → Notice row 4 was retrieved but not cited → confirms the model ignored it
  → "Open source document" → page-anchored PDF view
```
**Design obligation:** the evidence table shows *everything retrieved*, including passages the answer ignored — the API returns them for exactly this reason. Rows the answer cited are marked; uncited rows are still present, dimmed, and never hidden.

### J4 — Agent investigation (Investigator)

```
Switch mode to Agent (⌘/) → ask a comparative question
  → Run status line shows the search counter incrementing: "search 2 of ≤ 5"
  → Answer renders with searchCount = 3 in the run header
  → Expand "Agent activity" → the three queries the agent chose to run, in order
  → Evidence table numbered by first sighting, duplicates collapsed
```
**Design obligation:** if `searchCount` is `0`, an amber **Ungrounded** banner sits directly above the answer. This is the single most important state in the product — a confident, well-written answer about documents the agent never opened.

### J5 — Degradation (Operator)

```
Status bar health dot turns amber
  → g then h → Health screen
  → Dependency matrix: AI Search — Unhealthy — "not reachable" — 14s ago
  → Runs screen, filtered to failures → Search.IndexUnavailable × 23 in 10 min
  → Open a failed run → copy correlation id + trace id → paste into Azure Monitor
```

### J6 — Corpus audit (Curator)

```
Documents screen, sorted by chunk yield ascending
  → "spec-v4.pdf · 9.2 MB · 3 chunks" carries a Low yield warning
  → Row detail: extraction path = PdfPig (local), no Document Intelligence configured
  → Recommendation inline: "This document appears to be scanned. Configure Azure
     Document Intelligence to extract it, then re-upload."
```
**Design obligation:** this warning is derived from the `ChunkCount` ÷ `SizeInBytes` ratio, which the upload response already returns. The API comment naming this exact failure — *"a large PDF that yields two chunks extracted badly"* — becomes a product feature.

---

## 5. Information architecture and navigation

### Structure

```
KnowledgeAssistant
├── Ask                    the console (default route)
│   └── Run detail         permalink to a single run
├── Runs                   history, filterable
├── Documents              the corpus
│   └── Document detail    metadata + chunk inspector
├── Health                 dependency status
└── Settings
    ├── Defaults           topK, maxSources, default mode
    ├── Appearance         theme, density
    └── Environment        endpoints, deployment names (read-only)
```

Four primary destinations. Everything else is a detail view, an overlay, or a setting. If a fifth ever earns a place in the rail, something must leave.

### Navigation model

**Left rail** — persistent, 240px, collapsible to 48px (⌘B). Icon + label, 32px rows, 2px accent bar on the selected item's leading edge (not a filled pill). Grouped:

```
  ┌──────────────────────┐
  │ ◆ KnowledgeAssistant │  product mark + name, 48px
  │ ────────────────────  │
  │ ▸ Ask            ⌘K   │
  │   Runs                │
  │ ────────────────────  │
  │   Documents      142  │  count badge, mono, fg.subtle
  │ ────────────────────  │
  │   Health          ●   │  status dot
  │   Settings            │
  │                       │
  │ ────────────────────  │
  │ ⊙ metin.topcu         │  account, pinned bottom
  └──────────────────────┘
```

**Top bar** — 48px, spans the content area only (the rail runs full height, VS Code style). Contains: breadcrumb / page title, contextual actions (right-aligned), environment badge.

**Status bar** — 24px, pinned to the viewport bottom, full width, VS Code inspired. Left: environment + region. Right: health dot, last run's latency, last run's correlation id (click to copy). This is the Operator's ambient channel and it costs 24 pixels.

**Command palette** — ⌘K. Navigation, actions, recent runs, and document search in one list. Not a search box with delusions of grandeur; a real command surface with sections and keybinding hints.

**Breadcrumbs** — only two levels deep, used on detail screens: `Documents / handbook.pdf`. No breadcrumb on top-level screens.

**No tabs in the top bar.** Sub-navigation within a screen (e.g. Settings sections) uses a left-aligned segmented list inside the content area.

---

## 6. Screen list

| # | Screen | Route | Backed by | Priority | Built |
|---|---|---|---|---|---|
| 0 | Sign in | `/signin` | Entra ID (see §15) | P0 | No — the API has no user authentication |
| 1 | **Ask console** | `/` | `POST /api/questions` | **P0** | Yes |
| 1b | **Agent** | `/agent` | `POST /api/questions/agent` | **P0** | Yes — split from Ask, because the two make different promises about cost |
| 2 | Run detail | `/runs/:id` | client store; see §15 | P0 | No |
| 3 | Runs | `/runs` | client store; see §15 | P1 | No — and not in the rail, see the status note above |
| 4 | Documents | `/documents` | `GET /api/documents` | P1 | Yes. The columns are what the index stores — no size or chunk count |
| 5 | Document detail | `/documents/:id` | needs `GET /api/documents/:id` | P2 | No |
| 6 | Upload (panel) | overlay on 1 & 4 | `POST /api/documents` | P0 | Yes |
| 7 | Health | `/status` | `GET /health` | P1 | Yes — at `/status`, so the client route cannot shadow the API's own probes |
| 8 | Settings | `/settings/*` | local + config read | P2 | Yes |
| 9 | Command palette | overlay | client | P1 | No |
| 10 | System error pages | `403 / 404 / 500 / offline` | — | P1 | Yes |

---

### 6.1 Ask console — the centerpiece

```
┌────────┬──────────────────────────────────────────────────┬─────────────────┐
│ RAIL   │ Ask                                    [Upload]  │  INSPECTOR      │
│ 240    ├──────────────────────────────────────────────────┤  400 (resize)   │
│        │ ┌──────────────────────────────────────────────┐ │                 │
│  Ask ▸ │ │ What is the retention policy for build logs? │ │  Source [3]     │
│  Runs  │ │                                              │ │  ───────────    │
│        │ │                                     0 / 2000 │ │  handbook.pdf   │
│  Docs  │ └──────────────────────────────────────────────┘ │  chunk 47 of148 │
│        │ ┌─Retrieval─┬─ Agent ─┐   topK ◂ 5 ▸    [Ask ⌘⏎] │  score  0.8213  │
│  Health│ └───────────┴─────────┘                          │  ───────────    │
│  Setts │                                                  │  Build logs are │
│        │ ─────────────────────────────────────────────────│  retained for   │
│        │  RETRIEVAL · 5 retrieved · 1,284 tok · 2.4 s     │  90 days in the │
│        │  16:42:08 · cid 4f2a…c81                    [⋯]  │  hot tier, then │
│        │ ─────────────────────────────────────────────────│  archived for a │
│        │                                                  │  further 275…   │
│        │  Build logs are retained for 90 days in the hot  │                 │
│        │  tier [1], after which they are archived for a   │  ───────────    │
│        │  further 275 days [3]. Deletion is permanent     │  [Open source]  │
│        │  and not recoverable [3].                        │  [Copy passage] │
│        │                                                  │                 │
│        │  EVIDENCE                        5 passages  [⇅] │                 │
│        │ ┌───┬───────┬──────────────┬───────┬───────────┐ │                 │
│        │ │ # │ SCORE │ DOCUMENT     │ CHUNK │ PREVIEW   │ │                 │
│        │ ├───┼───────┼──────────────┼───────┼───────────┤ │                 │
│        │ │ 1 │ ▮▮▮▮▯ │ handbook.pdf │ 12    │ Retention…│ │                 │
│        │ │ 2 │ ▮▮▮▯▯ │ handbook.pdf │ 13    │ Storage t…│ │  ← dimmed:      │
│        │ │ 3◂│ ▮▮▮▯▯ │ handbook.pdf │ 47    │ Build log…│ │    uncited      │
│        │ │ 4 │ ▮▮▯▯▯ │ runbook.pdf  │ 3     │ Archival …│ │                 │
│        │ │ 5 │ ▮▯▯▯▯ │ policy.pdf   │ 88    │ Permanent…│ │                 │
│        │ └───┴───────┴──────────────┴───────┴───────────┘ │                 │
└────────┴──────────────────────────────────────────────────┴─────────────────┘
   dev · westeurope                    ● healthy · 2.4s · cid 4f2a…c81
```

**Composer at the top, not the bottom.** This is the clearest structural break from chat UIs and it is functionally correct: there is no scrollback to anchor to. The composer is a 3-row textarea (auto-grows to 8 rows, then scrolls), 1px border, 4px radius, no shadow, with a live character counter that turns amber at 1,800 and red at 2,000 — mirroring the validator's `MaxQuestionLength`.

**Mode is a segmented control, never a switch.** `Retrieval | Agent`, 28px tall, with a tooltip on each stating the promise the endpoint makes:

- *Retrieval* — "One search, one completion. Predictable latency and cost."
- *Agent* — "The agent decides how many times to search. Answers questions retrieval cannot; costs what it costs."

Those are the controller's own words. A user choosing cost profile deserves to read them.

**Parameter stepper.** `topK` in Retrieval mode, `maxSources` in Agent mode. Range 1–20, enforced client-side to match the validators. A stepper, not a slider — sliders imply a continuum where a discrete integer is meant.

**Run header** is a metrics strip in the shape of a CI job header: mode · retrieved-or-search count · token total · duration · timestamp · correlation id · overflow menu (Re-run, Re-run in other mode, Copy as Markdown, Copy correlation id, Permalink).

**Answer body.** 15px/24, measure capped at 72ch. Markdown rendered: headings, lists, tables, inline and fenced code. Citation marks `[n]` are inline mono chips — 12px, 2px radius, canvas.inset background, 1px border — that are focusable buttons. Hovering one highlights its evidence row; clicking scrolls to it and opens the inspector.

**Evidence table.** Full width, 36px rows, sortable by `#` or `Score`. Columns: reference number, score bar + numeric value (4dp, mono), document name, chunk order, single-line preview. Rows expand inline (Enter, or click the chevron) to reveal the **complete** chunk text in mono at 13px/20 — never truncated, per the API's explicit contract. Rows whose reference number does not appear in the answer render at `fg.muted` with a `not cited` micro-label; they are never hidden.

**Agent activity** (Agent mode only). A collapsed disclosure below the run header: an ordered list of the queries the agent issued, with the number of passages each returned. Collapsed by default, expanded automatically when `searchCount` is 0 or 1.

**States on this screen:** empty corpus · empty run (no question asked yet) · running · answered · answered-with-zero-retrieval · answered-ungrounded (agent, `searchCount = 0`) · validation error · request failed. All specified in §11–13.

---

### 6.2 Run detail

The same run header, answer, evidence table, and agent activity as the console, but read-only, permalinked, and with the full request echo (question, mode, parameters) shown as a definition list. Actions: `Re-run`, `Re-run in Agent mode`, `Copy as Markdown`, `Report issue` (pre-fills a ticket body with correlation id, trace id, error code, and parameters).

### 6.3 Runs

A dense table — 32px rows, compact density. Columns: time · mode badge · question (truncated to one line) · retrieved/searches · tokens · duration · status. Filters as a toolbar row: mode, status (ok / ungrounded / no-results / failed), date range, free-text over questions. Row click opens run detail. Failed runs show their error code as a mono chip in the status column.

This screen is where the Operator lives during an incident, so failed-run filtering must be reachable in one click from the Health screen.

### 6.4 Documents

Table, 36px rows. Columns: file name (with PDF glyph) · size · chunks · yield · ingested · document id (mono, truncated, click-to-copy). The **yield** column is a small monochrome bar plus a `Low` amber chip when chunks-per-megabyte falls below threshold. Sortable by yield ascending — the Curator's default view.

Toolbar: `Upload` (primary), search, sort, density toggle. Bulk selection with checkboxes reserved for a future `DELETE` endpoint; not shown until that endpoint exists.

### 6.5 Document detail

Header block: file name, size, content type, chunk count, blob name (mono), ingested timestamp, document id. Below it, a chunk table — order · character count · preview — expanding to full text, so a Curator can verify extraction quality passage by passage rather than by re-reading the PDF.

### 6.6 Upload panel

A right-side panel (400px), not a centered modal — the user should still see the corpus behind it. Contents:

1. Drop zone: 1px dashed border, 4px radius, 120px tall. Copy: "Drop a PDF here or **browse**. Up to 20 MB."
2. On file selection, a queue row per file: name · size · stage · elapsed.
3. Stage display: `Uploading → Extracting → Chunking → Embedding → Indexing`. Because `POST /api/documents` is synchronous and returns only on full ingestion, these stages are **presented as an elapsed-time narrative, not as measured progress**, and the panel says so in a caption: "Ingestion completes as one operation; stages are indicative." Honest beats convincing. (§15 lists the endpoint that would make this real.)
4. On success: green check, chunk count, and a `Low yield` warning if triggered.
5. On failure: the error code chip, the message, and a targeted recovery action (see §13).

### 6.7 Health

Two blocks.

**Probes** — three rows for `/health`, `/health/live`, `/health/ready`, each with status, response time, and last-checked. A caption explains why liveness and readiness differ, since that distinction is deliberate in this codebase and invisible otherwise.

**Dependencies** — a matrix, one row per check: Blob Storage · AI Search · (AI Foundry) with status dot, message, duration, and last-probed. Degraded rows expand to show the exception detail and a `Copy diagnostics` action. Checks disabled by configuration render as a neutral `Disabled` chip, not as healthy — reporting a disabled check as green is the kind of lie that ends an incident review badly.

### 6.8 Settings

Three sections in a left segmented list:

- **Defaults** — default mode, default topK, default maxSources, answer density. Stored per user.
- **Appearance** — theme (System / Light / Dark), density (Comfortable / Compact), mono font size.
- **Environment** — read-only definition list of the configured endpoints, deployment names, and API version, each with a copy action. No secrets appear because none exist; state that plainly on the page.

### 6.9 Command palette

Overlay, 640px wide, anchored 15% from viewport top. Elevation level 2. Sections: **Ask** (type a question, Enter to run in the default mode, ⇧Enter in the other), **Go to**, **Recent runs**, **Documents**, **Actions**. Each row shows its keybinding right-aligned in mono. No fuzzy-match highlighting fireworks — a simple bold on the matched substring.

---

## 7. Layout system

### Shell

| Region | Size | Behavior |
|---|---|---|
| Left rail | 240px / 48px collapsed | Fixed, full viewport height, `canvas.subtle`, 1px right border |
| Top bar | 48px | Sticky within content column, 1px bottom border |
| Content | fluid | Scrolls independently; horizontal padding 24px |
| Inspector | 400px, resizable 320–640 | Right-docked, 1px left border, collapsible (⌘⌥B) |
| Status bar | 24px | Fixed to viewport bottom, full width, 1px top border |

### Breakpoints

| Name | Width | Behavior |
|---|---|---|
| `sm` | < 900px | Rail collapses to icons; inspector becomes an overlay sheet |
| `md` | 900–1279px | Rail expanded; inspector overlays rather than docks |
| `lg` | 1280–1679px | Full three-pane |
| `xl` | ≥ 1680px | Content column caps at 1440px, centered within its region |

The product is desktop-first without apology. A 360px phone layout is specified only for the Health screen and run permalinks — the two things an Operator opens from a phone during an incident.

### Measure

| Content | Max width |
|---|---|
| Answer prose | 72ch |
| Chunk text (mono) | 96ch |
| Empty-state copy | 44ch |
| Tables | full region width |

---

## 8. Component inventory

### 8.1 Shell

| Component | Notes |
|---|---|
| `AppShell` | Rail + top bar + content + inspector + status bar |
| `NavRail` / `NavItem` | 32px rows, 2px leading accent bar when selected, count badge slot |
| `TopBar` | Title/breadcrumb left, actions right, 48px |
| `StatusBar` | 24px; env, region, health dot, last-run latency, last correlation id |
| `Inspector` | Docked panel, resizable, header + scroll body + pinned footer actions |
| `Breadcrumb` | Two levels maximum |

### 8.2 Navigation and overlay

`CommandPalette` · `Menu` / `MenuItem` / `MenuDivider` · `Popover` · `Tooltip` (12px, 300ms delay, no arrow) · `Dialog` (modal, ≤ 520px, level-2 elevation) · `Panel` (side sheet) · `Toast` (bottom-right, stacks to 3, 6s, never for errors that need action).

### 8.3 Input

`Button` — variants `primary` · `default` · `subtle` · `danger`, sizes `sm 28` / `md 32` / `lg 40`, states rest/hover/active/focus/disabled/loading
`IconButton` — 28×28, 16px glyph
`SegmentedControl` — the mode selector; 28px, 1px border, selected segment gets `canvas.base` + inner border
`TextArea` — the composer; auto-grow, character counter slot
`TextField` · `Select` · `Checkbox` · `Radio` · `Switch` (settings only)
`NumberStepper` — topK/maxSources; mono value, ◂ ▸ buttons, min/max clamped
`SearchInput` — 28px, leading glyph, ⌘F hint, clear button
`FileDropZone` — dashed 1px, 120px, drag-active state tints to `accent.subtle`
`FilterBar` — a row of dropdown filters + result count

### 8.4 Data display

`DataTable` — the workhorse. Sticky header, sortable columns, row expansion, keyboard row navigation, zebra **off** by default (1px row borders instead), density prop
`DefinitionList` — label/value pairs for metadata blocks; labels are 11px eyebrows
`Badge` — status (`healthy`/`degraded`/`failed`/`disabled`), mode (`Retrieval`/`Agent`), warning (`Low yield`, `Ungrounded`, `Not cited`)
`Chip` — mono, 2px radius; used for error codes, ids, citation marks
`CopyableId` — mono, truncated middle (`4f2a…c81`), click to copy, 1.2s check confirmation
`ScoreBar` — 5 monochrome segments + numeric value, 4dp, mono
`StatusDot` — 8px circle, paired with a text label; never color-only
`KeyHint` — mono 11px key caps for keybindings
`Timestamp` — relative by default, absolute UTC on hover
`MarkdownBody` — the answer renderer; constrained element set
`CodeBlock` — mono 13px, `canvas.inset`, 1px border, copy button, no syntax-color carnival — comment/string/keyword only

### 8.5 Domain components

These carry the product's identity and do not exist in any generic library.

| Component | Responsibility |
|---|---|
| `RunHeader` | Mode · count · tokens · duration · timestamp · correlation id · actions |
| `RunStatusLine` | The live, staged progress line during a run (§12) |
| `AnswerBody` | Markdown + interactive `CitationMark` marks |
| `CitationMark` | Inline `[n]`; focusable; links answer text to evidence row |
| `EvidenceTable` / `EvidenceRow` | Numbered, scored, expandable, cited/uncited distinction |
| `ChunkViewer` | Full untruncated chunk text, mono, with copy and source link |
| `GroundingBanner` | The `searchCount = 0` and `retrievedChunkCount = 0` states |
| `TokenMeter` | Prompt / completion / total, with a proportional monochrome bar |
| `AgentActivity` | The ordered list of queries the agent chose to run |
| `IngestStages` | The five-stage ingestion narrative with elapsed time |
| `YieldIndicator` | Chunks-per-megabyte bar plus `Low yield` warning |
| `HealthMatrix` | Dependency × status, expandable to exception detail |
| `ProblemDetails` | RFC 7807 renderer: code chip, title, detail, correlation id, action |

### 8.6 Feedback

`Alert` — inset, 1px border + tinted background, variants `info` / `warning` / `danger` / `success`; icon 16px, no rounded blob behind it
`EmptyState` — §11
`Skeleton` — §12
`ProgressBar` — 2px, determinate and indeterminate
`ErrorPage` — §13

---

## 9. Design tokens

Three tiers: **primitive** (raw values, never referenced by a component) → **semantic** (what components reference) → **component** (only where a component genuinely deviates).

### 9.1 Color — primitives

**Neutral** (cool gray, ~215° hue at very low saturation)

| Token | Value | | Token | Value |
|---|---|---|---|---|
| `gray.0` | `#FFFFFF` | | `gray.6` | `#56606B` |
| `gray.1` | `#F6F7F9` | | `gray.7` | `#39424E` |
| `gray.2` | `#EFF1F4` | | `gray.8` | `#262C35` |
| `gray.3` | `#E6E9ED` | | `gray.9` | `#1A1F27` |
| `gray.4` | `#D8DCE2` | | `gray.10` | `#14181F` |
| `gray.5` | `#6B747E` | | `gray.11` | `#0E1116` |

**Blue** (interactive) — a deep azure, deliberately below full saturation

| Token | Value | | Token | Value |
|---|---|---|---|---|
| `blue.1` | `#EAF2FE` | | `blue.5` | `#0B5FCE` |
| `blue.2` | `#CFE2FD` | | `blue.6` | `#0A4FA8` |
| `blue.3` | `#539BF5` | | `blue.7` | `#08315F` |
| `blue.4` | `#1F6FEB` | | | |

**Semantic hues** — used only for status

| Hue | Light | Dark | Meaning |
|---|---|---|---|
| Green | `#1A7F37` | `#3FB950` | Healthy, succeeded |
| Amber | `#9A6700` | `#D29922` | Degraded, ungrounded, low yield |
| Red | `#CF222E` | `#F85149` | Failed, invalid |

There is no purple, no magenta, no cyan, and no gradient anywhere in the system.

### 9.2 Color — semantic tokens

| Semantic token | Light | Dark | Used for |
|---|---|---|---|
| `canvas.base` | `#FFFFFF` | `#0E1116` | Page background |
| `canvas.subtle` | `#F6F7F9` | `#14181F` | Rail, table headers, toolbars |
| `canvas.inset` | `#EFF1F4` | `#1A1F27` | Code blocks, chunk text, chips |
| `canvas.overlay` | `#FFFFFF` | `#191E26` | Menus, dialogs, palette |
| `canvas.hover` | `rgba(0,0,0,.035)` | `rgba(255,255,255,.045)` | Row and item hover |
| `canvas.selected` | `#EAF2FE` | `rgba(31,111,235,.16)` | Selected row, active nav |
| `border.muted` | `#E6E9ED` | `#1E242C` | Internal dividers, row separators |
| `border.default` | `#D8DCE2` | `#262C35` | Panels, inputs, tables, cards |
| `border.strong` | `#B9C0C9` | `#39424E` | Hovered inputs, drag handles |
| `fg.default` | `#14171A` | `#E6E9EE` | Body and headings |
| `fg.muted` | `#56606B` | `#9BA5B1` | Secondary text, uncited rows |
| `fg.subtle` | `#6B747E` | `#6E7783` | Timestamps, placeholders, eyebrows |
| `fg.onEmphasis` | `#FFFFFF` | `#FFFFFF` | Text on filled accent |
| `accent.fg` | `#0B5FCE` | `#539BF5` | Links, active icons, focus |
| `accent.emphasis` | `#0A5AC2` | `#1F6FEB` | Primary button fill |
| `accent.subtle` | `#EAF2FE` | `rgba(31,111,235,.15)` | Selected tint, drag-active |
| `success.fg` / `.emphasis` / `.subtle` | `#1A7F37` / `#1A7F37` / `#E6F4EA` | `#3FB950` / `#238636` / `rgba(63,185,80,.15)` | Healthy |
| `attention.fg` / `.emphasis` / `.subtle` | `#9A6700` / `#BF8700` / `#FFF6E0` | `#D29922` / `#9E6A03` / `rgba(210,153,34,.15)` | Degraded, ungrounded |
| `danger.fg` / `.emphasis` / `.subtle` | `#CF222E` / `#CF222E` / `#FDECEE` | `#F85149` / `#DA3633` / `rgba(248,81,73,.15)` | Failed |
| `focus.ring` | `#0B5FCE` | `#539BF5` | 2px outline, 2px offset |

**Contrast:** every `fg.*` token meets WCAG AA (≥ 4.5:1) against its intended canvas; `fg.default` exceeds 12:1 in both themes. Status is never conveyed by color alone — every dot has a label, every chip has text.

**Dark is the default theme**, with full light parity. Engineers work in dark; enterprise procurement demos happen in light.

### 9.3 Typography

**Faces**

| Role | Stack |
|---|---|
| Sans | `Inter`, `Segoe UI Variable Text`, `Segoe UI`, `-apple-system`, `system-ui`, sans-serif |
| Mono | `Cascadia Mono`, `SF Mono`, `Menlo`, `Consolas`, `Liberation Mono`, monospace |

Two weights only: **400** and **600**. One exception: 500 on 11px eyebrow labels, where 400 goes muddy at that size.

**Scale** — base 14px, not 16. Density is a feature.

| Token | Size / Line | Weight | Tracking | Use |
|---|---|---|---|---|
| `text.display` | 24 / 32 | 600 | −0.02em | Screen title, used once per screen at most |
| `text.h1` | 20 / 28 | 600 | −0.015em | Section heading, run detail title |
| `text.h2` | 16 / 24 | 600 | −0.01em | Panel and card headings |
| `text.h3` | 14 / 20 | 600 | 0 | Sub-headings, empty-state titles |
| `text.answer` | 15 / 24 | 400 | 0 | **Answer prose only** — the one place reading beats density |
| `text.body` | 14 / 22 | 400 | 0 | Default body |
| `text.ui` | 13 / 20 | 400 | 0 | Table cells, form labels, menu items |
| `text.caption` | 12 / 16 | 400 | 0 | Timestamps, helper text, counters |
| `text.eyebrow` | 11 / 16 | 500 | +0.04em, uppercase | Table headers, metadata labels, section markers |
| `mono.body` | 13 / 20 | 400 | 0 | Chunk text, code blocks |
| `mono.ui` | 12 / 18 | 400 | 0 | Ids, scores, token counts, keybindings |
| `mono.chip` | 11 / 16 | 500 | 0 | Citation marks, error codes |

Numerals are tabular in every mono context and in table columns holding numbers.

### 9.4 Spacing

4px base grid. All padding, margin, and gap resolve to this scale.

| Token | Value | Typical use |
|---|---|---|
| `space.0` | 0 | — |
| `space.1` | 2px | Icon nudges, chip inner padding |
| `space.2` | 4px | Icon-to-label gap |
| `space.3` | 8px | Control inner padding, tight stacks |
| `space.4` | 12px | Table cell padding, form field gaps |
| `space.5` | 16px | Component groups, panel padding |
| `space.6` | 20px | — |
| `space.7` | 24px | Content region padding, section gaps |
| `space.8` | 32px | Major section separation |
| `space.9` | 40px | — |
| `space.10` | 48px | Empty-state vertical padding |
| `space.11` | 64px | Page-level error block padding |

**Sizing**

| Token | Value |
|---|---|
| `size.control.sm` / `.md` / `.lg` | 28 / 32 / 40px |
| `size.row.compact` / `.default` / `.relaxed` | 32 / 36 / 44px |
| `size.icon.inline` / `.default` / `.nav` / `.empty` | 14 / 16 / 20 / 24px |
| `size.rail` / `.rail.collapsed` | 240 / 48px |
| `size.topbar` / `.statusbar` | 48 / 24px |
| `size.inspector` (min/default/max) | 320 / 400 / 640px |

### 9.5 Radius

| Token | Value | Applied to |
|---|---|---|
| `radius.xs` | 2px | Chips, citation marks, score segments |
| `radius.sm` | 4px | **Default** — buttons, inputs, badges, panels, tables, drop zone |
| `radius.md` | 6px | Menus, dialogs, command palette |
| `radius.full` | 9999px | Status dots and avatars only |

Nothing in this product has a corner radius above 6px. That single constraint does more to avoid the generated-UI look than any other token here.

### 9.6 Elevation

| Token | Light | Dark | Applied to |
|---|---|---|---|
| `elevation.0` | none | none | **Everything by default** — separation is a 1px border |
| `elevation.1` | `0 1px 1px rgba(0,0,0,.06), 0 4px 12px rgba(0,0,0,.10)` | `0 0 0 1px #39424E, 0 8px 24px rgba(0,0,0,.40)` | Menus, popovers, toasts |
| `elevation.2` | `0 2px 4px rgba(0,0,0,.08), 0 12px 32px rgba(0,0,0,.14)` | `0 0 0 1px #39424E, 0 16px 48px rgba(0,0,0,.55)` | Dialogs, command palette |

Cards, panels, tables, the composer, the rail, and the inspector are all `elevation.0`. In dark theme, elevation is communicated primarily by the 1px ring — diffuse shadows are nearly invisible on `#0E1116` and only add haze.

### 9.7 Motion

| Token | Value | Use |
|---|---|---|
| `motion.instant` | 80ms | Hover, active, checkbox, focus ring |
| `motion.fast` | 120ms | Popover and menu open, tooltip |
| `motion.base` | 160ms | Panel slide, row expand, inspector dock |
| `motion.slow` | 200ms | Dialog enter — the ceiling |
| `motion.ease` | `cubic-bezier(0.2, 0, 0, 1)` | The only curve in the system |
| `motion.exit` | `cubic-bezier(0.4, 0, 1, 1)` | Dismissals |

Permitted properties: `opacity`, `transform: translate`, `background-color`, `border-color`, `height` (for row expansion). Forbidden: scale-in entrances, rotation, spring physics, staggered lists, parallax, any looping decorative animation. Under `prefers-reduced-motion`, all durations drop to 0 and the indeterminate progress bar becomes a static determinate-unknown bar.

### 9.8 Z-index

| Token | Value | Layer |
|---|---|---|
| `z.base` | 0 | Content |
| `z.sticky` | 100 | Table headers, top bar |
| `z.rail` | 200 | Nav rail, status bar |
| `z.dropdown` | 300 | Menus, popovers |
| `z.overlay` | 400 | Dialog scrim, side panels |
| `z.dialog` | 500 | Dialogs, command palette |
| `z.toast` | 600 | Toasts |
| `z.tooltip` | 700 | Tooltips |

### 9.9 Focus

A single, uniform focus treatment: 2px `focus.ring` outline at 2px offset, following the element's own radius. Never removed, never restyled per component, never replaced by a background tint. Focus is visible on mouse click for inputs and keyboard-only for buttons and rows.

---

## 10. Icons

**Library:** [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT) — Regular weight at 16px and 20px; Filled weight used **only** for the selected nav item. Microsoft-native, which is the right lineage for an Azure product, and it avoids the rounded, friendly look of most icon sets.

Rules: 16px inline and in tables · 20px in the nav rail · 24px in empty states (never larger, never illustrated) · icons always inherit `currentColor` · every icon-only control has an accessible label and a tooltip · no icon appears without a text label unless it is in the 28px icon-button size and has a tooltip.

**Inventory**

| Domain | Icons |
|---|---|
| Navigation | `chat-sparkle` (Ask), `history` (Runs), `document-multiple` (Documents), `pulse` (Health), `settings` |
| Actions | `send`, `arrow-clockwise` (re-run), `copy`, `checkmark`, `arrow-download`, `delete`, `open` (external), `more-horizontal`, `dismiss`, `add` |
| Ask domain | `search` (retrieval), `bot` (agent), `text-quote` (citation), `document-text-extract` (chunk), `data-histogram` (score), `timer` (latency), `number-symbol` (tokens) |
| Documents | `document-pdf`, `arrow-upload`, `folder-open`, `warning` (low yield) |
| Status | `checkmark-circle` (healthy), `warning` (degraded), `dismiss-circle` (failed), `subtract-circle` (disabled), `info` |
| Layout | `panel-left-contract` / `-expand`, `panel-right-contract` / `-expand`, `chevron-right` / `-down`, `arrow-sort` |
| Meta | `keyboard`, `link`, `bug`, `person-circle`, `weather-moon` / `-sunny` (theme) |

---

## 11. Empty states

**One formula, applied everywhere.** A 24px icon in `fg.subtle`, a 14/600 title, one sentence of 13px `fg.muted` body capped at 44ch, one primary action, and an optional text link to documentation. Left-aligned inside panels; centered within a 320px block inside large regions. No illustrations, no mascots, no gradient backdrops.

| Screen / region | Title | Body | Action |
|---|---|---|---|
| Ask — corpus empty | **No documents indexed** | Upload a PDF to build the corpus. Answers are only as good as what has been ingested. | `Upload a document` |
| Ask — no run yet | **Ask a question** | Retrieval runs one search and one completion. Agent decides for itself how many times to search. | — (composer is focused) |
| Ask — zero retrieval | **Nothing in the corpus covers this** | Retrieval returned no passages, so no answer was generated and the model was never called. | `Try a broader question` · `Switch to Agent` |
| Evidence — agent, 0 searches | **The agent did not search** | This answer was produced without opening the corpus. See the banner above. | `Re-run in Retrieval mode` |
| Runs — no history | **No runs yet** | Runs you make appear here with their evidence, cost, and correlation ids. | `Go to Ask` |
| Runs — filters exclude all | **No runs match these filters** | Try widening the date range or clearing the status filter. | `Clear filters` |
| Documents — empty | **The corpus is empty** | PDFs up to 20 MB. Each becomes searchable passages within a few seconds of upload. | `Upload a document` |
| Document detail — no chunks | **This document produced no passages** | Text extraction returned nothing. The file is likely a scan; Document Intelligence is not configured. | `View configuration` |
| Health — checks disabled | **Dependency checks are disabled** | Readiness probes are switched off in configuration, so this page cannot report dependency status. | `View configuration` |
| Inspector — nothing selected | **Select a passage** | Choose a row in the evidence table, or click a citation mark in the answer. | — |
| Command palette — no match | **No results** | Press Enter to ask this as a question instead. | — |

The zero-retrieval state deserves emphasis: it is a **success**, not an error, and it renders in neutral colors with no red anywhere. The API is explicit that *"retrieving nothing is a success"* — the UI must not contradict it.

---

## 12. Loading states

Four mechanisms, chosen by how much is known about what is coming.

### 12.1 The run status line — the signature loading state

An answer takes 2–15 seconds. A spinner for 15 seconds is an insult. Instead, a mono line beneath the composer narrates the pipeline, exactly as a build log would:

```
Retrieval mode
  ▸ embedding question                                    0.3 s  ✓
  ▸ searching 1,842 passages                              0.6 s  ✓
  ▸ generating answer                                     2.1 s  ⠋
```

```
Agent mode
  ▸ starting agent                                        0.2 s  ✓
  ▸ search 1 — "build log retention policy"       4 passages  ✓
  ▸ search 2 — "archival tier duration"           3 passages  ✓
  ▸ search 3                                              1.4 s  ⠋
```

13px mono, `fg.muted`, completed stages get a green check and a fixed duration, the active stage gets a 4-frame braille spinner and a live elapsed counter. A 2px indeterminate `accent` bar sits at the top of the run region. Cancel is available throughout and maps to client disconnect, which the API already honors via cancellation tokens.

For the agent, the incrementing search counter is not decoration — it is the user's only real-time signal that the corpus is actually being consulted.

### 12.2 Skeletons

Used only where the layout is known in advance: evidence table rows, document rows, run history rows, health matrix rows. A skeleton is a `canvas.inset` block at `radius.xs`, sized to the real content's typical width (60–80%, varied per row so it does not look like a barcode). Animation: opacity pulse 0.45 → 0.75 over 1.6s, `ease-in-out`. **No shimmer sweep** — the diagonal gradient sweep is the single most recognizable tell of generated UI.

Skeletons never appear for content that arrives in under ~300ms; below that threshold the region simply holds its previous height.

### 12.3 Inline control loading

Buttons: label switches to the present participle ("Ask" → "Asking…"), a 14px spinner replaces the leading icon, the button stays at its original width to prevent layout shift, and the control is disabled but remains focusable so screen readers announce the change.

### 12.4 Region progress

A 2px indeterminate bar pinned to the top edge of the region being loaded — the top bar for route transitions, the panel header for panel loads. Never a full-screen blocking overlay, never a centered spinner on an empty page.

### 12.5 Ingestion

Because `POST /api/documents` completes all five stages before responding, the stage list advances on estimated timing rather than measurement, and the panel says so in a caption. Each stage that has "completed" is shown as a filled step; the current one pulses. If elapsed time exceeds the estimate by 2×, the caption changes to "Still ingesting — large documents take longer" rather than letting a fake progress bar sit at 95%.

---

## 13. Error states

### 13.1 Three tiers

| Tier | When | Presentation |
|---|---|---|
| **Field** | Client-side or 400 validation | 12px `danger.fg` text below the control; control border turns `danger.fg`; no icon, no toast, no shake |
| **Region** | One region failed, the page is usable | Inset `Alert` (danger) with error code chip, message, `Retry`, and correlation id |
| **Page** | The route cannot render, or a dependency is down | Centered 480px block: 24px icon, 20/600 title, one sentence, error code chip, correlation id with copy, primary retry, secondary link to Health |

Errors **never** appear only as toasts. A toast disappears; a failed run is a fact that belongs in the run record.

### 13.2 The `ProblemDetails` renderer

The API returns RFC 7807 with a stable machine-readable `code`, and instructs callers to *branch on that, never on the message*. The UI does exactly that: the code selects the copy and the recovery action; the server's `detail` is shown verbatim underneath as supporting evidence, never as the headline.

```
┌─────────────────────────────────────────────────────────┐
│ ⊗  The search index is unavailable                      │
│                                                         │
│    Search.IndexUnavailable                              │
│                                                         │
│    Retrieval could not reach Azure AI Search. Your      │
│    question was not sent to the model and nothing was   │
│    charged.                                             │
│                                                         │
│    cid 4f2a91be…c81   trace 8d1f…a02        [copy both] │
│                                                         │
│    [Retry]  [View health]                               │
└─────────────────────────────────────────────────────────┘
```

### 13.3 Error code map

Every code the codebase actually produces, with its user-facing copy and recovery. Codes not in this table fall back to the generic tier-appropriate treatment with the code shown verbatim — never a bare "Something went wrong."

**Validation — field tier, red inline text**

| Code | Message shown | Where |
|---|---|---|
| `Question.Missing` | A question is required. | Under composer |
| `Question.TooLong` | Questions are limited to 2,000 characters. | Under composer; counter already warns from 1,800 |
| `Question.InvalidTopK` | Choose between 1 and 20 passages. | Under stepper |
| `Question.InvalidMaxSources` | Choose between 1 and 20 passages per search. | Under stepper |
| `Document.FileMissing` | Select a PDF to upload. | Drop zone |
| `Document.FileEmpty` | This file is empty. | Queue row |
| `Document.FileTooLarge` | This file exceeds the 20 MB limit. | Queue row, with actual size shown |
| `Document.InvalidExtension` | Only PDF files are accepted. | Queue row, with detected type shown |

Client-side constraints mirror these exactly, so the server's validation is a backstop rather than the first thing a user meets.

**Infrastructure — region or page tier**

| Code | Headline | Recovery offered |
|---|---|---|
| `Storage.UploadFailed` | The document could not be stored | Retry upload |
| `Storage.ContainerUnavailable` | Document storage is unavailable | View health |
| `Storage.AuthenticationFailed` | Not authorized to reach storage | View configuration · Contact administrator |
| `Storage.BlobAlreadyExists` | A document with this name already exists | Rename and retry |
| `Storage.BlobNotFound` / `Storage.DownloadFailed` | The source document could not be opened | Retry · Back to run |
| `Search.IndexUnavailable` / `Search.ChunkIndexUnavailable` | The search index is unavailable | Retry · View health |
| `Search.SearchFailed` | The search failed | Retry |
| `Search.IndexingFailed` / `Search.DocumentRejected` | The document was stored but not indexed | Retry ingestion — and state plainly that it is **not yet searchable** |
| `Search.AuthenticationFailed` | Not authorized to reach the search service | View configuration |
| `VectorIndex.IndexingFailed` / `.ChunksRejected` | Passages could not be indexed | Retry ingestion |
| `VectorIndex.DimensionMismatch` / `Embedding.DimensionMismatch` | Configuration mismatch between the embedding model and the index | View configuration — this needs an administrator, so no user-level retry is offered |
| `VectorIndex.IndexUnavailable` | The vector index is unavailable | View health |
| `Embedding.RateLimited` | Rate limited by Azure OpenAI | **Retry in 00:23** — a live countdown driven by `Retry-After`, with the button disabled until it reaches zero |
| `Embedding.GenerationFailed` / `.ResponseMismatch` | The question could not be embedded | Retry |
| `Ingestion.EmbeddingMissing` | The document could not be ingested | Retry upload · Report issue |

**Transport**

| Condition | Treatment |
|---|---|
| `413` | Same copy as `Document.FileTooLarge` — Kestrel's limit and the validator's must read identically to the user |
| `401` / expired session | Full-page re-authentication; the composer's draft is preserved in local storage and restored |
| `403` | Page tier: "You do not have access to this workspace," with the signed-in identity shown |
| `404` (run or document) | Page tier: "This run no longer exists," noting that run history is client-local until persistence ships (§15) |
| `5xx` unmapped | Page tier, generic headline, code shown verbatim, correlation id prominent |
| Network offline | Status bar turns amber with `Offline`; the composer disables with "Reconnecting…"; queued runs are not auto-retried without consent |
| Client-cancelled | Neutral, not an error: "Run cancelled" with a `Re-run` action |

### 13.4 The two states that are not errors

These are the most important states in the product and both are frequently mis-designed as failures.

**Zero retrieval** (`retrievedChunkCount = 0`, Retrieval mode) — neutral inset, `border.default`, no hue:

> **Nothing in the corpus covers this.**
> Retrieval returned no passages, so the model was never called and no tokens were spent.
> `Broaden the question` · `Switch to Agent`

**Ungrounded answer** (`searchCount = 0`, Agent mode) — an amber `attention` banner, sitting **above** the answer where it cannot be missed:

> ⚠ **The agent answered without searching the corpus.**
> This answer comes from the model's training data, not from your documents. It has no citations and cannot be verified against a source.
> `Re-run in Retrieval mode`

The answer text below it renders at `fg.muted` rather than `fg.default`, and the evidence table is replaced by its empty state. The visual demotion is deliberate: the codebase notes that an agent that never searched *"still produces a confident, well-written answer — from its training data, about documents it did not open."* Typography should not lend that answer authority the product cannot vouch for.

---

## 14. Keyboard model and accessibility

### Keybindings

| Keys | Action |
|---|---|
| `⌘K` / `Ctrl+K` | Command palette |
| `⌘↵` | Run the question |
| `⇧⌘↵` | Run in the other mode |
| `⌘/` | Toggle Retrieval ⇄ Agent |
| `⌘⇧C` | Copy run as Markdown with citations |
| `⌘B` / `⌘⌥B` | Toggle nav rail / inspector |
| `g` then `a` `g` `d` `h` `s` | Go to Ask · Agent · Documents · Health · Settings (`g r` for Runs is absent while that screen is) |
| `j` / `k` | Move between evidence or table rows |
| `↵` / `→` | Expand focused row · open detail |
| `o` | Open the focused row's source document |
| `c` | Copy the focused row's passage |
| `1`–`9` | Jump to citation *n* in the answer |
| `/` | Focus search in list screens |
| `Esc` | Close overlay, collapse expanded row, blur composer |
| `?` | Keyboard shortcut reference |

Chorded `g`-prefixed navigation follows GitHub; `j/k` follows both GitHub and VS Code lists. These are muscle memory for the target user, so matching them exactly matters more than internal consistency.

### Accessibility commitments

- **WCAG 2.2 AA** across both themes, verified per token pair, not per screenshot.
- Every status conveyed by **shape or text in addition to color** — dots always carry labels, chips always carry codes.
- Full keyboard operation of every flow, including evidence expansion and inspector navigation; no mouse-only affordances.
- Visible focus everywhere, never suppressed, uniform 2px ring.
- The run status line is an `aria-live="polite"` region; completion and failure are announced. The ungrounded banner is `aria-live="assertive"`.
- The evidence table is a real table with proper headers and scope; sortable columns announce their sort state.
- Citation marks are buttons with labels like "Source 3, handbook.pdf, chunk 47, relevance 0.82" — not bare `[3]`.
- Target sizes ≥ 24×24 CSS px, with 28px controls providing comfortable margin.
- Respects `prefers-reduced-motion`, `prefers-color-scheme`, and `prefers-contrast`.
- Content reflows to 320px width without horizontal scrolling on Health and run permalinks; tables scroll horizontally within their own container elsewhere.

---

## 15. API gaps this design depends on

The design is deliberately honest about what the current API cannot do. These are the changes required, ordered by what blocks the most screens.

| # | Gap | Blocks | Note |
|---|---|---|---|
| 1 | **No user authentication** in the API. `DefaultAzureCredential` authenticates the *service to Azure*, not a *user to the service*. | Sign-in, identity in the rail, per-user defaults, audit | Recommend Entra ID at the platform edge (Container Apps / App Service authentication) plus a validated bearer token, so the "no secret exists" guarantee is preserved |
| 2 | ~~**No `GET /api/documents`.**~~ **Closed.** The endpoint returns the corpus, newest first, bounded by `maxResults`. What it *cannot* return is size, content type, or chunk count: the index never stored them, so adding those columns means an index schema change and a re-ingest. | Document detail and corpus counts still | Document detail (`GET /api/documents/:id`) remains open |
| 3 | **No source document access.** The container is private and the upload response deliberately withholds the blob URI. | "Open source document" from every citation | Needs a proxied or short-lived-SAS read endpoint. **Without it, the verification journey (J3) terminates at the chunk text** |
| 4 | **`AnswerCitation.BlobUri` is returned to callers**, while `UploadDocumentResponse` deliberately withholds the URI to avoid publishing storage topology. | — | An inconsistency worth resolving before the UI is built. The design does **not** render `BlobUri`; it uses `DocumentId` + `ChunkId`, so resolving this either way requires no design change |
| 5 | **No run persistence.** | Runs history, permalinks, incident correlation | Until it exists, Runs is browser-local, capped, and the screen says so plainly in a caption. Permalinks would 404 for a colleague, so the share action is hidden rather than broken |
| 6 | **No ingestion status endpoint.** Upload is synchronous and returns only on completion. | Real progress in the upload panel | The staged narrative is honest but estimated; a status endpoint would make it measured |
| 7 | **No streaming.** | Token-by-token answer rendering | Not required — the run status line is a better fit for this product than a typing effect, and arguably the right long-term answer regardless |
| 8 | **No document deletion.** | Corpus curation, bulk selection | Bulk affordances stay hidden until the endpoint exists |

Screens 4, 5, and parts of 3 are specified in full here so the API work can be scoped against a concrete target, but they are marked P1/P2 precisely because they cannot ship first.

---

## 16. Decisions needed before implementation

1. **Default theme** — this specification proposes **dark by default** with full light parity. If enterprise procurement demos are the primary first impression, light-by-default is the safer call. One-line change either way.
2. **Scope of the first build** — the recommendation is Ask console + Upload panel + Health (screens 1, 6, 7, plus run detail as a client-local view). That is shippable against the API *as it exists today*, with no backend changes at all.
3. **API gap priority** — #2 (`GET /api/documents`) is closed; #3 (source access) now unlocks the most user value.
4. **Runs history without persistence** — ship it browser-local with an honest caption, or hold the screen until gap #5 is closed.
5. **Font licensing** — Inter is SIL OFL and self-hostable. If the organization requires Segoe UI Variable exclusively, the scale holds unchanged; only the stack order swaps.

---

*Awaiting approval. No component, token, or stylesheet has been implemented.*
