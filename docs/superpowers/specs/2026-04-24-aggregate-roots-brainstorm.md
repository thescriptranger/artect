# Aggregate Roots / Selective Endpoint Generation — Brainstorm (in progress)

**Status:** Brainstorming. Awaiting user clarification before design.

**Date opened:** 2026-04-24

**Topic:** Let the user control which database tables get full API endpoint generation versus which exist only as embedded child types inside another entity's response.

---

## Motivating example (from the user)

> "Assume a database `CustomerDB` with 40 tables. Many sub-tables (`CustomerComment`, `CustomerPurchase`, …) have one-to-many relationships back to `Customer`. Today, when I select 'include one-to-many relationships' in the wizard, I get endpoints for `Customer` with the child records embedded — good. But I also get endpoints for `CustomerComment`, and the response shows the parent FK. I might want `Customer` and its embedded children, but no standalone `CustomerComment` endpoints (or at least no parent data on them). I want this to work reliably for any database."

---

## Current state of the system (as of commit `7efad9b`)

- `--child-collections true` controls the `IncludeChildCollectionsInResponses` flag.
- When the flag is on, every non-join-table entity with a PK gets:
  - A DTO + Response with embedded child-collection properties (only for one-to-many "down" relationships).
  - A DataAccess class with `GetPagedAsync` / `GetByIdAsync` projecting children inline (commit `7efad9b` fixed the empty-array bug).
- **Reference navigations (parent refs like `CustomerComment.Customer`) are NOT currently emitted to DTOs or Responses.** Only the FK *column* (e.g. `customerId: <guid>`) appears, because it's a regular table column.
- Every entity gets the same shape: DTO + Response + Validators + Endpoints + DataAccess + Feature-folder abstractions. There is no concept of "this entity is a child-only type."
- Join tables (`IsJoinTable == true`) are already filtered out of all emitters — that's a separate, narrower exclusion.

---

## Conceptual framing — Aggregate Roots vs. Child Entities

Domain-Driven Design gives us a vocabulary for what the user is describing:

- **Aggregate root** — an entity that's directly addressable from the API. Has its own endpoints, validators, commands, queries.
- **Child entity / value type within an aggregate** — exists only as part of an aggregate root's payload. Reachable only via the root. No endpoints of its own.

In the user's example: `Customer` is the aggregate root; `CustomerComment`, `CustomerPurchase`, etc. are child entities within the Customer aggregate.

Today, Artect treats every table as an aggregate root by default. The proposed change introduces a notion of "child-only" entities.

---

## Open question for the user

What is the user actually trying to limit? Four possibilities:

- **A.** Don't generate endpoints for certain tables at all. `CustomerComment` should never get `GET /api/customer-comments`. Customer is the only addressable thing; comments are reachable only via `GET /api/customers/{id}`.
- **B.** Endpoints are fine, but FK columns on a child look like leaked parent identity. Hide `customerId` on the `CustomerComment` row.
- **C.** Both A and B — control which tables get endpoints, AND clean up what appears on rows that do.
- **D.** Something else.

The answer determines the design space:
- (A) is the classic DDD "aggregate root" problem. Big design surface. Affects DTO / Response / Endpoint / DataAccess / Validator / DI / EndpointRegistration emitters.
- (B) is a much smaller per-property concern. Likely a column-suppression flag.
- (C) combines both.

---

## Sketch of approaches if the answer is (A) / (C)

These are not yet recommendations — just a starting point to refine after the user picks A/B/C/D.

### Approach 1 — Per-entity wizard checklist
After schema introspection, the wizard shows all introspected tables and asks which should be aggregate roots. Unchecked tables get DTO + Response only (so they can be embedded), but no endpoints / DataAccess / Validators / Features folder.

- Pro: Explicit. Predictable. Works for any schema.
- Con: 40-table databases mean a long checklist. Tedious if no defaults.

### Approach 2 — Heuristic auto-classification with override
After introspection, classify tables using rules like:
- A table whose only incoming reference is a single parent and which has no further children → likely a child of that parent.
- A table whose name starts with another table's name (`CustomerX`) → likely a child of `Customer`.
- A table with composite PK that includes an FK → likely a junction.

Show the proposed classification; let the user override individually.

- Pro: Sensible defaults; less typing.
- Con: Heuristics fail on unusual schemas. User needs to verify the classification anyway.

### Approach 3 — YAML aggregate-root manifest
Add a section to `artect.yml`:
```
aggregates:
  Customer:
    children: [CustomerComment, CustomerPurchase, CustomerProfile]
  Membership: {}
```
Anything in a `children` list is suppressed from endpoint generation. Anything not mentioned is treated as a standalone root. The wizard pre-fills this from heuristics; the user edits the file directly for fine control.

- Pro: Versionable. Re-runnable. Easy to share between teammates. Decouples classification from the interactive wizard.
- Con: One more file. User has to know the schema to fill it in.

### Approach 4 — Hybrid (Approach 2 + Approach 3)
Heuristics propose defaults. Defaults are written to `artect.yml` on first run. User edits the file or re-runs the wizard with `--review-aggregates`.

This is likely the right answer if the user picks A or C, but it's worth confirming.

---

## What I'm waiting on

The user is testing the current `--child-collections true` build (commit `7efad9b`) to confirm what they actually see on a child entity's GET response today. Once they confirm whether the issue is "endpoints exist at all" (A) vs. "FK column looks bad" (B) vs. both (C), I'll narrow the brainstorm and propose a concrete design.

---

## Pointers

- `src/Artect.Generation/Emitters/ResponseEmitter.cs` — emits Response types; today reads `Table.Columns` + optional `CollectionNavigations`. No reference navigations.
- `src/Artect.Generation/Emitters/EntityDtoEmitter.cs` — same shape on the DTO side.
- `src/Artect.Generation/Emitters/DataAccessEmitter.cs` — emits DataAccess classes; child-collection projection added in commit `7efad9b`.
- `src/Artect.Generation/EmitterRegistry.cs` — central list of emitters that would be filtered by an "aggregate root" classification.
- `src/Artect.Console/WizardRunner.cs` — current 14-question interactive wizard; new prompt would land here.
- `src/Artect.Config/ArtectConfig.cs` — record where a new `AggregateRoots: IReadOnlyList<string>` (or similar) field would live.
- `src/Artect.Config/YamlReader.cs` / `YamlWriter.cs` — would need to read/write the new structure.
