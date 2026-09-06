# Local comparison publication

## Status and owner

This is the Queries-owned target contract for
[#5925](https://github.com/richlander/dotnet-inspect/issues/5925), the bounded
first profile of publication step 5 in
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706).
The borrowed-input profile is implemented by `DirectMemberComparisonQuery`
in `DotnetInspector.ResearchQueries` and consumed by CLI `match --body`.
The Release gates below cover this profile; Browser adoption remains separate.

`DotnetInspector.Queries` is the sole architectural owner. The optional
`DotnetInspector.ResearchQueries` companion supplies the physical dependency
boundary, not another owner.

The claim is:

> A local comparison result associates one Queries invocation with its exact
> Research terminal evidence, preserving query-origin non-success and native
> endpoint outcomes instead of manufacturing a successful comparison.

## Selected consumer and bounds

The immediate consumer is the
[direct-member adapter](direct-member-comparison.md) for two explicitly
selected methods in one already-open implementation assembly. CLI
`match --body` serves this scenario (formerly `--implementation`). Inspect Web
will expose
an explicit comparison action over its existing member selection and retained
implementation participant.

The same method on both sides is valid. Different member names do not imply
identity correspondence. A valid bodyless MethodDef remains an endpoint for
native classification; it is not missing input.

The first profile borrows an already-acquired assembly context. It does not
realize package roles, select a forwarding destination, compare whole
assemblies, or acquire Source. The existing context owner retains its lifetime.
Publication is not a new acquisition or cleanup coordinator.

## Result contract

`LocalComparisonQueryResult` is the shared public result boundary for this
profile. Its implementation belongs in the Queries companion. It distinguishes
query-origin non-success from publication of a Research terminal outcome.
Neither category is an implicit assertion that two implementations are equal.

Before Research execution, an invalid or unavailable physical designation,
rejected input access, query failure, or cancellation remains typed
non-success. It identifies the affected side when one is implicated and
retains the applicable owner-issued cause. It does not fabricate a Research outcome, endpoint
absence, or comparison over empty data. If sealing has not occurred, the
result has no invented population identity; after sealing, the exact Queries
identity remains associated with the result.

Once the
[population boundary](inspection-layers.md#queries-to-research-population-boundary)
has projected a population, Queries retains its complete receipt for the
invocation. Research execution must consume that exact admitted population.
Publication binds the returned outcome to the captured Queries operation,
question, and side-local input identities through that receipt.

The receipt remains companion-internal. Public consumers receive the
Queries-owned identity information and typed result evidence, not a request
to reconstruct the internal map. Publication must not join by position,
assembly display name, token alone, or rendered text.

All four existing Research terminal outcomes retain their meaning:

- `Completed` retains the original completion, work-item associations, native
  C#/IL results, Findings, and Research cleanup outcomes.
- `Rejected` retains the Research rejection.
- `Failed` retains the Research diagnostic and cleanup outcomes.
- `Cancelled` retains the Research cancellation and cleanup outcomes.

The association belongs to the Queries invocation, not to incidental fields
in a terminal payload. A rejected or cancelled Research outcome can have no
operation identifier or an empty cleanup list; it still belongs to the
invocation that made the call. Queries captures that association before
invocation and consumes its returned outcome directly. This is not a public
import facility for attaching arbitrary results to receipts.

Research alone validates its population, selected work basis, session,
producer results, and completion. Publication does not reproduce that
validator or manufacture a Research identity.

`Completed` means Research accounted for the requested work. An unavailable
or failed work item remains such inside completed accounting. A missing native
body diff, `NoApplicableInput`, or an empty change list is not a Queries
shortcut for exactness. The original native verdicts remain authoritative.

## Lifetime and cancellation

Queries uses the existing assembly-context access and cancellation contracts.
It finishes its borrowed access scopes before exposing the result. Research
retains ownership of its local stages and their terminal cleanup evidence;
Queries does not close those stages a second time.

Query-origin cancellation is distinct from an unavailable endpoint. An
already-selected failure or cancellation is not rewritten as completion while
leaving an access scope. Research cancellation is preserved as Research
terminal evidence, not converted to an empty successful comparison.

The published result remains usable after the query's borrowed input scopes
have ended. It retains the population boundary's identity currency and
Research's existing terminal evidence contracts. This design adds no
cross-owner release protocol, runtime replay mechanism, or global stage
catalog.

## Presentation boundary

Publication retains structured native results and their endpoint associations.
It does not rerun a producer, normalize generated C#, compute another diff,
or rebuild native evidence from display lines.

CLI lowering uses Markout and preserves the separate structural-match verdict.
Browser lowering uses typed facade data and its existing managed-operation
lifetime. Neither host uses a synthetic `ResearchComparison` to make one
result look like a different comparison operation.

This document defines no row schema, JavaScript DTO, selection interaction,
or host cancellation authority. Those remain adopting-owner work.

## Demo and gates

The CLI invocation exercises the public query; the before/after path is:

```text
match Left.Compute Right.Compute --library app.dll --body

Before: CLI -> legacy CompareMembers -> synthetic assembly-result wrapper
After:  CLI -> direct-member query -> exact designated Research pair
            -> local session -> Queries publication -> Markout

Neighbor: the same MethodDef on both sides retains two side-local occurrences.
Neighbor: a bodyless endpoint retains native NoApplicableInput, not exactness.
```

The Browser adoption must demonstrate the same selected-method scenario
through an explicit user action. Ordinary single-member navigation remains
ordinary navigation. A component test or file-based application is useful
evidence, but is not production adoption in either host.

The Release gates in `DirectMemberComparisonQueryTests` are:

| Gate | Required observation |
| --- | --- |
| `LocalComparisonPublication_RetainsExactInvocation` | Separate real invocations and repeated physical inputs retain their exact query/population associations. |
| `LocalComparisonPublication_PreservesTerminalEvidence` | Real Research completion, rejection, failure, and cancellation retain their original typed payloads, including empty-cleanup cases. |
| `LocalComparisonPublication_PreservesQueryNonSuccess` | Wrong-image or missing physical designation, rejected access, and query-origin cancellation do not issue successful comparison evidence. |
| `LocalComparisonPublication_RemainsUsableAfterInputScopeCloses` | After borrowed input scopes end, consumers can read the retained identities, native verdicts, and failure evidence. |
| `DirectMemberComparison_PreservesGenericDeclaringTypes` | Generic and nested-generic declaring types retain their exact physical addresses and native body evidence. |
| `DirectMemberComparison_PreservesCompilerGeneratedMethods` | Explicitly selected lambda and local-function MethodDefs reach the native producers with their original addresses. |

Use compiled methods and product-issued evidence. Existing Research gates
continue to establish its own association and completion invariants. These new
gates establish the Queries handoff, not a second proof of native comparison.
These gates exercise real fixture images and Research-issued outcomes, including
cleanup failure and cancellation before a Research stage exists.

CLI gates in `MatchCommandTests` cover the actual `--body` entry point, native
JSON payloads and physical addresses, private raw-token and getter selections,
same-method comparison, bodyless availability, cancellation, and removal of the
former option. A compiler-generated seed and a discovery-returned token also
retain both physical endpoints through `--body`. `MatchDiscoveryTests` covers
neighboring discovery and pairwise selection behavior.
`ApiSurfaceExtractorTests.Extract_CompilerGeneratedMethodsRequireOptIn` covers
the existing explicit opt-in while keeping generated methods out of ordinary
API views.

The baseline is the existing population receipt and Research terminal union;
the member source-comparison query is analogous evidence for preserving
endpoint availability. A small immutable association is sufficient. There is
no new stateful protocol requiring a separate TLA+ model.

## Adoption and retirement

The counted first-consumer route is recorded in
[the adapter's adoption ledger](direct-member-comparison.md#first-production-punch-through)
under #4706 and the production-first direction in #5865.

The first runtime implementation is delivered with the adapter and CLI adopter,
using the bounded
first-adopter exception in
[design scope](../design-scope.md#stage-implementation-after-locking-the-design).
Browser adoption follows as its own immediate owner effort, not after a new
unconsumed infrastructure chain.

The CLI now consumes the public query instead of its former direct
`ImplementationDiff.CompareMembers` dispatch and synthetic `ResearchComparison`
wrapper. After both hosts work, focused Queries and Research cleanups remove
the unused remainder established by the actual caller inventory. Existing
Source and assembly-comparison behavior must remain supported; a future issue
alone does not justify retaining otherwise unused substrate.
