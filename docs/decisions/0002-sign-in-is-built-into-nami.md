# 0002. Sign-in is built into nami

- Status: accepted
- Date: 2026-11-10
- Decided by: the owner

## Question

Claude gives a connector data only after a sign-in step (OAuth). Decision 0001 makes
"each user runs their own copy" the default. Whose authorization server signs the
user in when the copy runs in the user's own account, on their own computer or at
their own host?

## What mattered

- Both reference products run their own authorization server; neither depends on an
  identity vendor.
  - telemetry.dev (hosted): OAuth 2.1 authorization code with PKCE S256, client
    metadata documents and dynamic client registration, refresh tokens, access
    tokens of about an hour, two scopes (read, and payloads behind a consent box).
  - SkyWalking Horizon UI (self-hosted): its own authorization server inside the
    product, off by default, switched on with a public issuer address and a signing
    key; client metadata documents and dynamic registration, PKCE S256, access tokens
    of 60 minutes, refresh tokens of 30 days. An administrator can also issue a
    bearer token.
  (Both read on 10 Nov 2026 from the vendors' pages, metadata documents and source.)
- Claude needs: a `401` with a pointer to protected-resource metadata whose `resource`
  equals the connector address; authorization-server metadata; PKCE S256; its fixed
  callback `https://claude.ai/api/mcp/auth_callback` for the hosted apps and a
  loopback callback for Claude Code; a client identity through client metadata
  documents or dynamic registration; refresh-token rotation for public clients.
- The owner's free Auth0 tenant can front one server at one address. It cannot issue
  tokens for addresses it does not know, allows ten applications in total, and a copy
  in someone else's account must not depend on the owner's tenant at all.
- nami today accepts only a key in the `x-otlp-api-key` header on `/mcp`.

## Options

- **The owner's Auth0 tenant for everything.** Works for the owner's single hosted
  copy. Does not work for copies other people run.
- **Each user brings an identity provider.** Works in principle. A one-click
  deployment that first needs an account and a configured application elsewhere is
  no longer one click.
- **A proxy in front that signs in** (for example Cloudflare Access). Free for small
  use, but adds an account, a domain and a product to every copy, and its pairing
  with Claude is documented only for one setup.
- **One sign-in service run by the owner for all copies.** One thing to build, but
  every copy then depends on a service the owner must keep running and pay for, and
  the owner controls access to other people's data.
- **Sign-in inside nami.** Each copy is its own authorization server, as in Horizon.
  The person who deployed it proves ownership with a secret set at deployment, typed
  into nami's consent page when Claude connects. No third party, no dependency on the
  owner, and the same mechanism serves the owner's hosted copy, a template copy and a
  local copy behind a tunnel.

## Decision

Sign-in is inside nami. nami uses no identity vendor: not for the owner's copy, not
for copies other people run, not for the hosted service. The Auth0 tenant of the
predecessor is not part of nami.

## Consequences

- nami gains an authorization server: metadata documents, an authorization endpoint
  with a consent page, a token endpoint with PKCE and rotating refresh tokens, client
  identification by metadata document and by dynamic registration, token storage on
  the volume, and limits against guessing the owner secret.
- The deployment template generates the owner secret; the page tells the user where
  to find it.
- The hosted service of decision 0003 adds accounts on top of this (who may sign in
  to which project).
- First task: read what the MCP server SDK in use (2.4.0) provides for the
  authorization-server side, then size the work.
- Done means: the built-in server passes against Claude on the web, in the desktop
  app, on the phone and in Claude Code.
