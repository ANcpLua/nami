# 0003. nami is listed in Claude's directory through one capped hosted service

- Status: accepted
- Date: 2026-11-10
- Decided by: the owner

## Question

Decision 0001 makes "each user runs their own copy" the default, so the owner pays for
nobody. Being found inside Claude needs a listing in Anthropic's directory. How does
nami get listed without the owner paying for every user?

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

## Options

- **Per-user addresses only.** The owner pays for nobody. The listing depends on a
  label the owner cannot request, so nami may stay unlisted.
- **One hosted service only.** Listed by default. The owner pays for every user and
  stores their telemetry.
- **Both.** One hosted service under one address, free up to a cap that rejects, is
  the listing; running your own copy stays the way for anyone who outgrows the cap
  or wants their data at home.

## Decision

Both. nami is listed in Anthropic's directory as one hosted service under one
address. It is free up to a cap that rejects new records and has one paid plan above
the cap. It is built after sign-in, the owner's private connector and the template
(the order in decision 0001), and it is submitted as an MCP connector together with
the skills-only plugin.

## Consequences

- The hosted service needs accounts, the caps of decision 0001 in the engine, a
  privacy policy, a support contact, and care for other people's telemetry.
- The caps and the price of the paid plan are set from measured cost per project
  before launch. The reference points are 30,000 to 50,000 records a month, 14 to 30
  days of retention and one project on the free tier, and 25 to 29 USD a month for
  the paid plan.
- The owner pays for hosted users, but only inside limits the owner sets: the cap per
  project and the spending limit at the host.
- The self-run way and the template come first and do not wait for the hosted
  service.
