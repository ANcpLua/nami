---
name: fixture-lifecycle
description: Maintain Nami test workspace ownership, sealed failure evidence, bounded retention and boot expiry.
---

# Fixture lifecycle

Use the existing owners: `FixtureProject` / `PackageFeedFixture` for per-test
workspaces, `eng/scripts/run_tests.py` for a test run, and
`eng/scripts/artifact_lifetime.py` for its lease and evidence. Read
`docs/testing.md` and these files before changing their contract.

Pass `TestContext` explicitly when full fixture evidence is wanted. `FixtureDiagnostics.None`
disables evidence even with a context; an absent context does not fall back to ambient test
state. The MSTest fixture adapter supplies its invocation context explicitly. No evidence
capture means no fallback SDK binlog. This changes the amount captured, not workspace ownership
or the run's cleanup/retention rules.

Materialization compares bytes and writes only missing or changed inputs. An explicitly shared
`PackageFeedFixture` owns both package and HTTP caches; each consumer still owns its scratch and
plugin directories. Empty shared feeds may own external-package caches without advertising an
empty local package-source mapping.

Repository test entry points put mutable fixture work below the run's owned
workspace. Successful and failed runs remove payloads by default. Only an explicit
`--retain-failure-minutes N` seals failed-run evidence in `evidence.zip`; N is at
most 120 minutes from completion. Mutable work is still removed. There is no
renewal API or unlimited retention. A different boot identity expires prior runs.
Keep the small current summary separate. Never prune arbitrary build outputs.

Hold the run lease while processes can write. The repository runner passes its
lease to child commands on POSIX. A busy lease protects work only until the hard
deadline: expiry and reboot win. After an owner crash, an unleased active run is
abandoned. All entries in the dedicated scratch root are owned, including malformed
metadata; invalid records are deleted with a reason, never kept indefinitely.

Use `python3 eng/scripts/artifact_lifetime.py sweep --dry-run` to inspect owned
cleanup. Choose retention at invocation, never retroactively renew it. On this Mac,
the existing dotfiles `make prune-scratch` dispatches to this owner only when
explicitly invoked. The user disabled background cleanup. Do not recreate a
scheduler. The repository test entry point also sweeps; optionally retained files
are physically removed on the next test run or manual sweep after expiry.

Verify policy with `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s
eng/scripts/tests -p test_artifact_lifetime.py -v` and exercise C# cleanup through
`./eng/test.sh --suite Integration --filter 'FullyQualifiedName~FixtureWorkspaceTests'`.
Test exact expiry, reboot before expiry, dead/live owner, finite retention,
snapshot tampering and preservation of unowned outputs. Scope-bound disposal
does not promise cleanup after power loss; the persisted boot/lease contract does.

Direct IDE test execution still owns per-fixture disposal, but the repository
runner owns the external run-level expiry policy. Do not claim that an IDE run
has that policy unless it actually invokes this owner.

For a resource intentionally shared by multiple tests, use `SharedFixture<T>`
owned by the class/group: initialize once in `ClassInitialize`, obtain it with
`GetAsync`, dispose in `ClassCleanup`. Creation and disposal each happen once,
including parallel callers. Never make each consuming test dispose that resource.
`SharedFixtureTests` proves ten test rows reuse one physical input file. Use a
per-test fixture when changes must not leak; use an explicitly ordered scenario
when mutations are part of the tested sequence.
