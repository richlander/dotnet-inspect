# CLI Platform type routing

## Owner and claim

This document owns the desktop CLI composition that adapts one versionless
bare type, member, or namespace target to the target-bound Workspace
declaration locator.

> The CLI selects one explicit dotnet hive, authorizes the named runtime
> family-default policy, transfers the complete PlatformHouse reference
> population into one short-lived Workspace, evaluates all Router probes in one
> shared locator envelope, closes the Workspace, and projects only detached
> structured Content to an explicit downstream command route.

[PlatformHouse reference processing](platform-house-reference-processing.md)
owns target selection, source settlement, complete reference-population
realization, and correspondence to that population.
[Workspace Live Locator](workspace-live-locator.md) owns admission of the
transferred Library population and its Workspace-governed lifetime.
[Reverse Type-Declaration Locator](reverse-type-declaration-locator.md) owns
matching, complete candidate vectors, MVID-bearing observation evidence, and
the common `InspectionEnvelope<T>` boundary. Router-specific Type/member
preference and command-token rewriting remain owned here.

## Input and route result

The path accepts:

- one bare type-or-member token;
- the caller's configured package-source options;
- one CLI command context; and
- the command cancellation token.

It runs only when the caller supplied no explicit package, platform, project,
library, or framework source. Explicit `--framework runtime@version` remains
an exact route and bypasses the versionless policy in this slice.

The host adapter returns one resource-free locator outcome:

- `Completed`, retaining the exact completed locator envelope;
- `Unavailable`, when no usable desktop dotnet hive exists;
- `Rejected`, when the House, Workspace admission, or locator rejects the
  request;
- `Incomplete`, when finite work prevents a complete result; or
- `Failed`, when source work or Workspace-governed retirement fails.

The host creates the full-Type pattern, every right-to-left top-level Type
prefix, and the exact namespace request before one
`TypeDeclarationLocatorInspection.ExecuteAsync` call. The completed
`InspectionEnvelope<TypeDeclarationLocatorSectionResult>` retains those
requests, complete candidate vectors, selected target context, structured
declarations, exact assembly identities, and MVIDs as primary Content. This
correspondence is not a diagnostic or Debug-only supplemental evidence.

A resolved Content value projects:

- the declaration's structured type name;
- the exact managed assembly identity carried by its API content; and
- the PlatformHouse-settled runtime version.

The downstream route is therefore explicit:

```text
type|member <structured type> --platform <assembly> \
  --framework runtime@<settled version>
```

No assembly display-name prefix is treated as identity. Ambiguous, rejected,
or incomplete locator outcomes fail visibly. `Missing` is valid only after the
complete admitted population was evaluated; it permits the router to continue
ordinary non-runtime classification without invoking the superseded Services
reverse scan.

Ordinary CLI output does not render successful route provenance and does not
write a success note to stderr. Ambiguous, rejected, population-realization,
locator, and cleanup failures retain their visible failure presentation.

## Desktop source composition

The CLI chooses at most one installed dotnet hive. `DOTNET_ROOT` wins when it
contains a `packs` directory. Otherwise the CLI derives the hive containing
the current CoreCLR runtime and accepts it only when that hive contains
`packs`. The installed source receives that exact path and performs no ambient
root search.

When an installed hive exists, the family-default policy uses:

1. installed all-framework target discovery as the preferred stage; and
2. configured Package Source discovery for exact `net10.0` as fallback.

When no installed hive exists, the policy omits the preferred stage and uses
the same package-backed fallback directly. The immutable policy:

- selects `DotNetRuntime`;
- uses stable `10.0.1` as the minimum installed version;
- accepts installed previews and release candidates at or above that SemVer
  floor; and
- selects only the latest stable `10.0.x` package-backed target.

The reference source plan uses installed realization before package-backed
realization. Package discovery and acquisition are lazy: an installed success
does not invoke either package operation. Configured source options are passed
unchanged to the package authorization owner.

## Finite work and cancellation

The CLI grants explicit finite ceilings for source operations, target
candidates and comparisons, assemblies, aggregate bytes, retained type
declarations, and total duration. PlatformHouse and each direct-Library
declaration inventory enforce those limits; the router adds only a bounded
right-to-left scan of possible type/member boundaries.

Cancellation remains `OperationCanceledException`. It is observed by target
selection, source realization, Workspace admission, declaration inventory,
locator evaluation, and authority retirement.

## Ownership and terminal precedence

Completed population realization transfers one Library owner per member and
one adjacent Artifact session. The CLI:

1. atomically transfers that exact session and owner batch into a Workspace;
2. admits the exact Platform population as one declaration context without
   reacquisition or image copying;
3. evaluates every Type/member/namespace request in one locator envelope;
4. closes the Workspace, which retires every Library owner and then the
   Artifact session; and
5. publishes only detached route evidence after cleanup succeeds.

All authorities are attempted even when one retirement fails. Cleanup failure
prevents publication of an otherwise successful locator result and remains a
visible CLI failure. House, admission, or locator non-success remains primary
when cleanup succeeds. No returned route result retains a stream, lease,
owner, callback, opener, package payload, or disposable authority.

## Router policy

The router evaluates the full token as a Type first. On a miss, it tests the
already-returned top-level dot-boundary answers from right to left and accepts
the first non-missing Type prefix as the member owner. This chooses the longest
structured Type without interpreting dots inside generic arguments. Candidate
discovery remains shared; exact-name preference, explicit generic-arity
behavior, top-level preference, definition preference, and ambiguity are
Router-owned policy.

The same envelope also carries compatibility suffix requests derived from
directly recognized assembly prefixes that belong to the selected Platform
target, including runtime-only assemblies such as `System.Private.CoreLib`.
The Router consults those answers only after direct assembly-prefix
classification cannot prove an exact Type. This preserves resolved and
ambiguous qualified-name behavior without intercepting another framework
family's fallback, reopening the runtime catalog, or repeating a reverse
declaration scan.

After a complete locator miss reaches a Type-command fallback, Router marks the
internal handoff so neither source resolution nor Type's later exact
find-if-miss can reopen runtime-wide discovery. Existing narrow-source failure
and wide prefix-browse behavior remain available through the Type command; the
handoff is capability-guarded and is not a user-facing option.

The locator projects all declarations for Type/member parity with the replaced
catalog. Namespace routing applies Metadata's `IsPublicSurface` fact before
namesake-Library selection, matching the prior namespace contract without
discarding the complete inventory evidence.

- A resolved full token routes to `type`, or to `member` when the caller
  explicitly selected member mode.
- A resolved prefix routes to `member` with the remaining suffix as its member
  selector.
- Ambiguity or rejection at the longest viable boundary is terminal.
- When every Type boundary misses, the Router checks the exact namespace answer
  and selects the longest namesake-Library prefix, preserving Platform
  population order within a namesake tier.
- A complete miss returns control to existing package, non-runtime Platform,
  project, and direct-library classification without a second Platform reverse
  scan.

Explicit-source and acquisition-free structural routes run before this path
and remain unchanged.

## Platform compatibility

This owner is deliberately desktop-CLI-specific and supports Windows, Linux,
and macOS. It uses the installed source only when a local dotnet hive is
available and otherwise uses the package-backed source. Browser/Wasm does not
reference this owner or the installed adapter; its separately reviewed
PlatformHouse adoption supplies package-backed capabilities directly.

## Evidence gates

Release tests prove:

- real `System.Text.Json.JsonSerializer` and
  `System.Collections.Generic.List<T>.Add` routing through selected locator
  candidates and exact assembly identities, with the original request, target,
  MVID, and member split retained in envelope Content after Workspace closure;
- generic `System.Collections.Generic.List<T>.Add` member routing through the
  same locator path;
- exact `System.Text.Json.Nodes` namespace routing through the same envelope;
- installed completion without package discovery;
- resource-free envelope use after every population authority retires;
- typed ambiguity, rejection, and true missing outcomes;
- successful short-name routes producing no stderr note;
- visible ambiguity from the production router;
- a true locator miss returning control to ordinary router classification; and
- explicit `runtime@version` bypassing the family-default path.

Lower owner suites continue to prove selection of installed prereleases,
stable `10.0.x` package fallback, source correspondence, all-or-nothing
population transfer, complete declaration evaluation, and locator vector
semantics.

## Production adoption and retirement

This slice adopts the CLI half of PlatformHouse step 9. Browser/Wasm adoption
remains a separate host-owner slice. Services-era `PlatformResolver`,
`PlatformPackService`, and `PlatformTypeCatalog` remain for explicit routes,
Find namespace compatibility, and Spotlight until their remaining consumers
migrate. The default bare Router path no longer invokes their duplicate
reverse scan after a complete locator miss.

The CLI route is a direct host-specific command projection and does not use
Markout. It adds no rendered section or output schema; the existing `type` and
`member` commands continue to own presentation.

## Non-goals

- Browser/Wasm routing.
- ASP.NET Core family-default selection.
- Exact-demand PlatformHouse execution.
- Metadata binding or implementation-population realization.
- Services-era resolver or catalog removal beyond the replaced bare Router
  reverse scan.
- New CLI options, sections, or output formats.
- Windows Metadata support.
