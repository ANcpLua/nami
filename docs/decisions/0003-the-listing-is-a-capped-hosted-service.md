# 0003. A directory listing, if wanted, is a capped hosted service

- Status: proposed
- Date: 2026-11-10
- Decided by: the owner (not yet; this record is the proposal)

## Question

Decision 0001 makes "each user runs their own copy" the default, so the owner pays for
nobody. Being found inside Claude needs a listing in Anthropic's directory. Do the two
fit together, and if not, which one gives way?

## What mattered

- Only a directory listing makes a connector findable and suggested inside Claude. A
  connector added by link is never suggested. (Anthropic, "Directory connectors vs
  custom connectors", read 10 Nov 2026.)
- A listing with one shared address is scanned and published by default as a Community
  connector. A listing where each user enters their own address is published only
  once it carries the Verified label. Anthropic decides that label during review;
  there is no application for it, and that review takes longer. (Anthropic, "Submit a
  connector to the directory" and "Authentication for connectors", read 10 Nov 2026.)
- A plugin cannot replace the listing for chat: a connector whose address comes from
  a per-user setting is ignored in chat. (Anthropic, "Plugin feature support across
  platforms", read 10 Nov 2026.)
- Two products in the same field are listed or connectable through one hosted
  address and keep that free with a cap that rejects new records.
- nami is still in development; nobody needs to find it today.

## Options

- **Per-user addresses only.** The owner pays for nobody. The listing depends on a
  label the owner cannot request, so nami may stay unlisted.
- **One hosted service only.** Listed by default. The owner pays for every user and
  stores their telemetry.
- **Both, in sequence.** No listing while nami is in development. When being found
  matters, one hosted service under one address, free up to a cap that rejects, is
  submitted as the listing; running your own copy stays the way for anyone who
  outgrows the cap or wants their data at home.

## Decision

Proposed: both, in sequence. Until the owner decides that being found in Claude is
worth it, there is no listing and nothing to host for others.

## Consequences

- The hosted service is only built when the listing is wanted. It then needs
  accounts, the caps of decision 0001 in the engine, a privacy policy, and care for
  other people's telemetry.
- The owner pays for hosted users, but only inside limits the owner sets: the cap per
  project and the spending limit at the host.
- The self-run way and the template do not wait for any of this.

## Reopen when

- Anthropic publishes per-user-address listings without the Verified label, or
  offers a way to request it.
- Measured cost per capped project is too high to offer the hosted tier for free.
