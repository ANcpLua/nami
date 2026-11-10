# Decisions

The permanent, long-term decisions that shape nami: one file per decision, saying
what was decided and why. An accepted record binds all work in this repository, by
people and by agents. The layout follows the decision records of
microsoft/agent-framework (`docs/decisions`), without their sign-off roles and review
process: nami has one owner.

- **Name:** `NNNN-short-title.md`, with the next free number.
- **Status:** `accepted` or `superseded by NNNN`. A record is written when the owner
  has decided, not before. An accepted record is not rewritten. A decision changes
  only through a new record; the old one then gets the superseded status and one
  line on what went wrong.
- **Length:** one page. A longer investigation goes into its own document, and the
  record links to it.

## Template

```markdown
# NNNN. Short title of the problem and the answer

- Status: accepted
- Date: YYYY-MM-DD
- Decided by: the owner

## Question

Two or three sentences: what had to be decided, and why now.

## What mattered

- The facts and limits that decided it, each with its source.

## Options

- Option A: what it is, what speaks for it, what speaks against it.
- Option B: the same.

## Decision

The chosen option and the reason in one or two sentences.

## Consequences

- What now has to be built, changed or given up.
```

## Records

| No. | Decision | Status |
| --- | --- | --- |
| [0001](0001-remote-connector-and-who-pays.md) | nami reaches Claude as a remote connector; each user runs their own copy by default | accepted |
| [0002](0002-sign-in-is-built-into-nami.md) | Sign-in is built into nami; no identity vendor | accepted |
| [0003](0003-the-listing-is-a-capped-hosted-service.md) | nami is listed in Claude's directory through one capped hosted service | accepted |
