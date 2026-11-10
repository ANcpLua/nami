# 0001. nami reaches Claude as a remote connector, and each user runs their own copy by default

- Status: accepted
- Date: 2026-11-10
- Decided by: the owner

## Question

nami replaces its predecessor and drops the human-facing part; the product is a Claude
connector. How does it reach Claude in chat on the web, in the desktop app and on the
phone, and who pays for hosting if many people use it? The owner pays personally and
has no revenue from nami.

## What mattered

- Claude chat on the web, in the desktop app and on the phone ignores a local MCP
  server in a plugin and reaches only a remote one. Cowork and Claude Code reach
  both. (Anthropic, "Plugin feature support across platforms", read 10 Nov 2026.)
- A listed connector must use OAuth 2.0 and must work for whoever connects; a key in
  a request header is not accepted. (Anthropic's submission pages and Software
  Directory Policy, read 10 Nov 2026.)
- Hosting other people's telemetry brings cost that grows with use, and duties for
  their data. Memory is the cost driver at the tested host, and a tool call copies a
  whole project today.
- Two products in the same field offer a free tier that cannot bill its provider: the
  cap rejects new records. (telemetry.dev and Langfuse pricing pages, read 10 Nov
  2026.)
- Railway pays template authors a share of what their deployments earn. That is an
  incentive to push one host, and readers have to be told about it.
- nami is still in development, and nothing binds it to a host or a vendor.

## Options

- **Local only, as a plugin with a bundled server.** Free for everyone and no data
  duties. It does not work in chat on the web, in the desktop app or on the phone, so
  it misses the product's target.
- **One hosted service for everyone.** Works everywhere with no setup for the user.
  The owner pays for every user and stores their telemetry.
- **Remote connector that each user runs, with an optional capped hosted service.**
  Works everywhere. A user pays their own host or runs it on their own computer; the
  owner pays only for a hosted tier whose cap rejects instead of billing.

## Decision

The third option. nami is one container with one volume, served over HTTPS with
sign-in. By default each user runs their own copy: locally for Claude Code and
Cowork, through a tunnel, or at a host from a one-click template. A hosted service
follows (decision 0003), free up to a cap that rejects new records, with one paid
plan above it.

Order of work: sign-in on `/mcp` (decision 0002); the owner's private custom
connector, which needs no review; the template for copies other people run; the
hosted service and its directory listing (decision 0003); a plugin bundle with
skills only, pointing at the same server, with the listing.

The public page puts the free local way first and states Railway's share in plain
words under the deploy button. Its wording and editing rules are in
[distribution.md](../distribution.md).

## Consequences

- Sign-in (OAuth) on `/mcp` is the gate for every surface except local use.
- The engine needs caps before any hosted tier: a per-project monthly count that
  rejects above the limit, retention per project, a project limit per account.
- The per-call copy of a whole project has to go before other people's data is
  hosted.
- The container and the template must deploy without any account, project or id of
  the owner, and sign-in for a copy that a user deploys cannot depend on the owner's
  tenant.
- The owner's own bill is limited by a hard spending limit at the host (20 USD on
  Railway, set 10 Nov 2026). At that amount the owner's services there go offline.
- Income from Railway's share is a bonus, not a plan: its own report of 5 Dec 2025
  names two authors above 100,000 USD, six above 10,000 and thirty above 1,000.
