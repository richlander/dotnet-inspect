# Workspace registration revisions

## Owner and claim

[Inert registration adoption](../../workspace-scope-and-expansion.md#inert-registration-adoption)
owns this model for issue #6577. For an exact open Workspace, registration
construction and replacement retain one complete ordered revision. Replacement
either publishes the entire valid request against its exact current revision,
returns the same revision for a no-op, or leaves the revision unchanged.

`WorkspaceRegistrationRevisions.tla` models the actual join currency:
`[workspace, identity, registrations]`. A client retains that entire immutable
revision, not a free-standing epoch or display-derived identifier. The foreign
Workspace deliberately uses the same initial revision number and payload.
Revision numbers are opaque equality tokens to clients; bounded allocation uses
the history length solely to provide fresh tokens, never to order requests.

This is independent evidence for registrations, **not** an import of the
Package Scope/Artifact physical-publication model.

## Assumptions and abstractions

- One mutable Workspace and one immutable foreign Workspace revision suffice
  for ownership rejection. Client reads and replacements are indivisible
  transitions under the existing runtime gate. A read and its later replacement
  can be interleaved with another client or close.
- `Open -> Closing -> Closed` is an **environmental availability assumption**.
  This model neither redefines nor proves resource-release internals, cleanup,
  draining, or eventual close. No adjacent owner module is extracted or copied.
- Both Sync and Async lifetime modes run the same in-memory transitions; mode
  does not affect registration behavior.
- The eight registration values retain exact-library source coordinates, literal
  prefix values, and lower ecosystem instance identities. Ecosystem A and B have
  identical canonical IDs and displayed fields but different instance identities.
  The other ecosystem has the same display name and a different canonical ID.
  Canonicalization and source-owner equality are inputs, not algorithms modeled
  here. Prefix and ecosystem keys include their union arm.
- Invalid inputs represent default arrays, null entries, an unknown arm, null
  expected revisions, and malformed expected revisions. Revisions otherwise come
  only from the subject or foreign owner. Explicit construction rejects the
  whole invalid/duplicate set before exposing a Workspace.
- No acquisition, Package publication, participant mutation, or eviction action
  exists here. `RegistrationOnly` checks a passive adjacent-state projection is
  unchanged by modeled actions; this is not implementation evidence for those
  non-interference claims. Package operations themselves remain outside scope.
- No fairness or liveness claim is made. Terminal requests, rejected construction,
  and an inert Workspace may stutter; deadlock checking is deliberately disabled.

## Tested bounds and gates

`Safety.cfg` selects twelve narrowly scripted scenarios rather than multiplying
every candidate, lifecycle phase, and client request into a Cartesian product:

| Scenario | Cases |
| --- | --- |
| Values | Equal no-op, reorder, new same-ID ecosystem instance, clear, re-add |
| Prefix / Library | Literal prefix case change and exact source version change |
| ArmKeys / Display | Same key across arms; distinct ecosystem IDs with same name |
| Duplicates | Repeated exact library, repeated prefix, two same-ID ecosystem instances |
| Malformed | Default array, null entry, unknown arm |
| Expected | Null/malformed, foreign, stale, invalid-candidate precedence, stale no-op |
| Race | Two readers/writers, including both reading the same old revision |
| Empty | Empty construction, empty-to-empty no-op, complete replacement |
| InitialDuplicate / InitialInvalid | All three duplicate arms and malformed initial sets |

Each scenario runs in both lifetime modes, with zero or one close sequence at
every enabled point. There are two clients in Race and one active client
elsewhere, at most seven replacements, five retained subject revisions, eight
registration values, and three entries in a valid registration sequence.
`TypeOK` allows six history entries so a no-op-churn mutation reaches a semantic
contract violation rather than merely exhausting a bound.

`Safety` checks complete construction; read availability and exact historical
revision retention; fresh, associated revision identities; typed outcome and
validation precedence; atomic full publication or unchanged state; immutable
snapshots; one commit per expected revision; and the passive non-interference
projection. Closing/closed reads return a historical revision marked Unavailable.

The manifest `eng/tla-expected-exit-codes.txt` enforces these semantic verdicts:

| Configuration | Required TLC exit | Purpose |
| --- | --- | --- |
| Safety | 0 | All bounded safety scenarios |
| BrokenStaleRevision | 12 | A second writer incorrectly commits from the old revision |
| BrokenForeignWorkspace | 12 | Matching revision number cannot replace Workspace ownership |
| BrokenCloseBeforePublication | 12 | Checking availability at read misses intervening close |
| BrokenNoOpChurn | 12 | An equal request incorrectly creates a fresh revision |
| BrokenEcosystemInstanceEquality | 12 | Canonical ID cannot replace instance-value equality |
| BrokenDuplicateCandidate | 12 | Duplicate identities cannot enter the published sequence |

Exit 12 is an expected invariant counterexample, not a successful safety proof.
The mutation configurations change one policy each and retain the same safety
checks. They establish that the checked predicates detect the targeted defects.

## Running the focused checks

Use the repository-pinned `tla2tools.jar` and Java. Keep runner scratch files
inside this model directory, including Java's extracted standard modules. Set
`TLA_TOOLS_JAR` to the pinned jar already available on the machine:

```sh
model=docs/design/models/workspace-registration-revisions
mkdir -p "$model/.scratch"
TMPDIR="$PWD/$model/.scratch" \
  JAVA_TOOL_OPTIONS="-XX:ActiveProcessorCount=2 -Djava.io.tmpdir=$PWD/$model/.scratch" \
  bash eng/run-tla-checks.sh "$model"
npx --no-install markdownlint-cli "$model/README.md"
```

The runner parses the module with SANY and checks only this model directory.
Its exit 0 means all exact outcomes matched, including the intended exit-12
negative controls. No repository-wide sweep is needed.

The focused run uses TLA Tools `2026.08.11.125311`, repository-pinned SHA-256
`ab323b79802aedc3203b3f9af37c6aca3ed43f4e0225b36f2aa77b26de46c05f`.

### Observed results

The focused runner parsed one module and matched all seven exact outcomes on
2026-09-12. With two TLC workers, the observed generated/distinct state counts
were:

| Configuration | Exit | Generated | Distinct |
| --- | --- | --- | --- |
| Safety | 0 | 3,674 | 3,648 |
| BrokenCloseBeforePublication | 12 | 26 | 26 |
| BrokenDuplicateCandidate | 12 | 16 | 16 |
| BrokenEcosystemInstanceEquality | 12 | 128 | 128 |
| BrokenForeignWorkspace | 12 | 128 | 128 |
| BrokenNoOpChurn | 12 | 16 | 16 |
| BrokenStaleRevision | 12 | 132 | 120 |

Safety exhausted its queue at depth 18. Negative controls stop at their first
counterexample, so worker scheduling can change their reported state counts.
The README also passed `npx --no-install markdownlint-cli`.

## Runtime conformance

These are bounded abstract model claims, not C# conformance or unbounded proofs.
The product must separately establish the modeled gate, immutable snapshots,
source equality, and absence of side effects. The owning design names Release
`WorkspaceRegistrationTests` and registration/Package interaction cases in
`WorkspaceScopeTests` as those runtime gates. They are implemented and validated
independently; this model's checks do not report them as run.
