# How nami reaches Claude, and who pays

Written on 10 November 2026. This file holds the wording for the public page and
what a directory listing needs, so none of it depends on anyone's memory. Nothing here
is built yet except running nami locally; each part of the page lists what must be
true before it is published.

The decision behind this page, with its reasons and consequences, is
[decision 0001](decisions/0001-remote-connector-and-who-pays.md).

## Rules for whoever edits the page

1. **Free first.** The local way comes first, is documented in full, and is never
   made worse to push a paid way. Caps exist only on the service the owner pays for.
2. **Say who pays whom.** The sentence about Railway's share stays directly under the
   deploy button, in normal text size.
3. **Stay host-neutral.** The page gives generic instructions that work at any
   container host. Another host is named only after it has been tested.
4. **Recommend by the reader's situation.** The table sends someone who only uses
   Claude Code to the free local way. It never sends everyone to the button.
5. **No number without a measurement.** The monthly cost and the caps are filled in
   from measured values, with the date.

## The page: "Ways to run nami"

nami is free to run yourself. Where it has to run depends on where you use Claude.

| You use Claude in | Run nami | Cost to you |
| --- | --- | --- |
| Claude Code or Cowork, on your own computer | On that computer. Claude starts it for you. | Free |
| Claude chat on the web, in the desktop app or on your phone, and your computer is usually on | On your computer, reachable through a tunnel | Free. It works while the computer is on. |
| Claude chat anywhere, at any time | Your own copy at a host | What the host charges: about `<measured amount>` a month for a small always-on service |
| Anywhere, with no setup | The hosted service | Free up to `<cap>` records a month |

Claude chat on the web, in the desktop app and on the phone can only reach a server
on the internet, never a program on your computer. That is how Claude works, and it
is the only reason the last three ways exist.

### On your own computer

Claude Code starts nami and talks to it directly:

```bash
claude mcp add --env NAMI_OTLP_AUTH_MODE=Unsecured --transport stdio nami -- bun "$PWD/artifacts/publish/mcp/main.js" --engine "$PWD/artifacts/publish/nami" --stdio
```

Your telemetry stays in a file on your disk. Nothing leaves the machine.

*Before publishing:* true today for Claude Code. Check Cowork once, and replace the
build paths with the install command when a packaged release exists.

### Your computer, through a tunnel

nami runs on your computer and a tunnel gives it an address on the internet, so
Claude chat can reach it. You sign in before Claude gets any data. It stops working
when the computer sleeps or is off.

`<steps: start nami with its public address set, start the tunnel, add the address in Claude under Customize, Connectors>`

Send telemetry to nami over OTLP/HTTP on port 4318 on the same computer. The tunnel
carries only the connection to Claude.

*Before publishing:* sign-in exists on `/mcp`; the steps were run end to end with
one named tunnel product from Claude on the web and on a phone; the page says which
tunnel product was tested.

### Your own copy at a host

nami is one container. It needs one volume for its data and these ports: 5100 for
Claude, 4318 for telemetry over HTTP, 4317 for telemetry over gRPC. Any host that
runs a container with a volume works:

`<generic run command with the image name, the volume and the public address>`

We test on Railway and keep a one-click template there:

`<Deploy on Railway button>`

If you deploy with this button, Railway pays us a share of what you pay them. It
costs you nothing extra and helps fund nami. Deploying the same container yourself,
there or anywhere else, works exactly the same.

Your telemetry stays in your own account at the host. We cannot see it.

*Before publishing:* a public container image exists; the template deploys with no
account, project or id of the owner in it; sign-in works on the deployed copy; the
monthly amount in the table comes from a week of measured use on the smallest
setting that works; the template is registered in Railway's kickback program, and
the sentence above is checked against the program's current terms.

### The hosted service

We run nami for you. It is free up to `<cap>` records a month, keeps data for
`<days>` days and holds one project. Above the cap nami stops accepting new records
until the next month; it never bills you by surprise.
`<paid plan, if one exists: price, records, days>`

Your telemetry is stored by us, in `<region>`. `<link to the privacy policy>`

*Before publishing:* the caps are enforced in the engine (a per-project monthly
count that rejects above the limit, retention per project, a project limit per
account); a tool call no longer copies a whole project; a privacy policy and a
contact address exist; the numbers are set from measured cost per project.

## What a directory listing needs

From Anthropic's submission pages, read 10 November 2026:

- Sign-in with OAuth 2.0; a key in a request header is not accepted for a listing.
  One client registered for Claude at the authorization server and entered in the
  developer portal is an accepted way ("Anthropic-held client credentials"); the
  redirect address is `https://claude.ai/api/mcp/auth_callback`. Claude Code does not
  use that client and needs client metadata documents or dynamic registration.
- Every tool carries a `title` and a read-only or destructive hint. The six tools
  have the hint and no title today.
- Test credentials for a fully populated account, a privacy policy, public
  documentation, a support contact and an icon.
- A whole tool result stays under about 150,000 characters in chat and 25,000 tokens
  in Claude Code. nami sends the result twice today and allows `max_bytes` up to
  262,144.
- A listing whose users each enter their own server address is possible, and is
  published only once the listing is Verified.

## Where the numbers and claims come from

- Local servers are ignored in chat and loaded in Cowork and Claude Code: Anthropic,
  "Plugin feature support across platforms".
- Listing requirements: Anthropic, "Publish to the directory", "Authentication for
  connectors", "Connector pre-submission checklist", "Submit a connector to the
  directory", and the Software Directory Policy.
- Railway pays template authors up to 25% of what it earns from deployments of their
  template (15% for usage, 10% for answering support questions), with cash-out from
  100 USD. Its post of 5 December 2025 reports almost 1 million USD paid in total,
  two authors above 100,000, six above 10,000 and thirty above 1,000. It is a bonus,
  not a plan for income.
- The cap that rejects instead of billing is how two products in the same field do
  it: telemetry.dev (free: 30,000 records a month, rejected above 36,000, 14 days,
  one project; first paid plan 25 USD) and Langfuse (free: 50,000 a month, 30 days,
  "Additional usage is not available"; first paid plan 29 USD; self-hosting free).
- The cost of a small always-on service at Railway was estimated at about 3 USD a
  month from its published rates. It has not been measured.

## Open

- Whether the hosted service is offered at all, and with which caps and price.
- The exact wording of the sentence under the deploy button: in the EU a commercial
  tie has to be recognizable to the reader; the wording has not been checked by a
  lawyer.
- How a copy that someone else runs signs its owner in. Proposed in
  [decision 0002](decisions/0002-sign-in-is-built-into-nami.md): sign-in inside nami,
  as both reference products do. It decides how much work the "each user runs their
  own copy" default is, and its effort is not estimated yet.
