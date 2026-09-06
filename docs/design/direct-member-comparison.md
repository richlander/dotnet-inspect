# Direct-member comparison

## Status and ownership

This is the Queries-owned target design for
[#5878](https://github.com/richlander/dotnet-inspect/issues/5878), historical
rank 5 and delivery step 6 of
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706).
The physical-pair adapter is implemented by `DirectMemberComparisonQuery` and
consumed by CLI `match --body` (#5967) and Browser Method Body Diff (#5990).
Workspace forwarding selection, comparison-tool adoption, and broader
retirement remain separate work.

`DotnetInspector.Queries` is the single architectural owner. Its optional
`DotnetInspector.ResearchQueries` companion is the physical dependency boundary
for Research composition, not another owner.

The claim is:

> Queries binds one caller-designated Before/After method pair to exact admitted
> source occurrences, consumes Research's designated-pair local comparison,
> and returns the shared Queries publication without reconstructing native
> evidence or claiming identity correspondence between different members.

The consumer is a caller that already knows which two methods it wants to
compare: CLI `match --body`, round-trip member/scope comparison,
authored rebuild, or the planned browser workspace comparison surface.
Matching candidates, proving runtime equivalence, and finding corresponding
members in a rebuilt artifact remain the caller's existing responsibilities.

## Basis and prerequisite boundary

The repository convention is an idless query request, owner-issued evidence,
and a presentation-neutral result. The analogous
[member source comparison query](member-source-comparison-query.md) preserves
endpoint outcomes without turning failure into empty text. Its one-member,
PDB-versus-decompilation operation is not this two-method, local C#/IL operation.
`ImplementationDiff.CompareMembers` is the behavioral baseline for explicit
pairing; its direct caller-to-Research orchestration is the retirement target,
not the native C# or IL algorithms.

The adapter exists to replace repeated caller orchestration with one exact
association and failure-preserving boundary. It does not need a second
population, receipt, session, scheduler, or result-lifecycle architecture.
The following contracts supply those responsibilities:

| Dependency | Consumed responsibility | Delivery status |
| --- | --- | --- |
| [Population boundary](inspection-layers.md#queries-to-research-population-boundary) | Sealed query occurrences and their bijective Research receipt | Landed in #5874 for #5860, tracker step 1 |
| [Workspace target composition](research-workspace-target-composition.md) | Exact root-to-terminal association when selection traverses a workspace facade | #5676 / #5699, step 4; required for that selection route, not an already-selected physical pair |
| [Research target and session contracts](implementation-diff.md#research-local-producer-session-and-completion) | Owner-issued target evidence, native producer results, and local session completion | Local session landed in #5827; designated pairing landed in #5908 for #5877, step 18 |
| [Local comparison publication](local-comparison-publication.md) | Association of the query receipt with terminal evidence for the borrowed-input profile | Implemented with the adapter and CLI cutover for #5925, the first profile of step 5 |

The publication dependency is intentional: this adapter must not invent a
temporary public result union that step 5 immediately replaces.
`LocalComparisonQueryResult` is the selected boundary for the first
borrowed-input profile. Its focused contract must lock before adapter
implementation. This design fixes how the adapter uses that boundary, not its
internal result or cleanup inventory. Queries-owned acquisition and cleanup
composition remain outside this first profile.

### Designated pairing is not strict correspondence

The existing `match` command intentionally compares differently named members.
Ordinary Research correspondence instead returns `SelectionDrift` for
different keys or roles. `ResearchProducerSession.Run` requires validated
Research resolution; changing labels or constructing a fake `Paired` outcome
does not bridge the two contracts.

[#5877](https://github.com/richlander/dotnet-inspect/issues/5877) owns
the Research designated-pair contract and runtime handoff, landed in #5908.
This document does not
choose its representation, constructors, validation, or session extension.
Queries must not implement around that missing dependency by minting Research
correspondence, relabeling different keys, or calling the legacy path as a
fallback. The physical-pair implementation consumes that handoff and the
shared publication directly.

Ordinary strict correspondence remains unchanged. A direct designation says
"compare these two methods"; it does not say they share an API identity or
establish a semantic-equivalence relation.

## Adapter contract

### Request and exact association

One invocation requests one pair and a nonempty selection from Research's
local C#/IL catalog. Its query-owned input is idless and contains an explicit
Before designation and After designation. Each binds an already-acquired
implementation source occurrence to its owner-issued physical Metadata method
address. A path, label, bare token, or signature alone is not the association.
In-memory artifact access is supported through existing source bindings;
a filesystem path is not an additional adapter requirement.

Both endpoints must designate physical methods. A missing or unresolved
designation remains typed non-success, not `SubjectAbsent`. A valid bodyless
MethodDef still goes to its native producer for endpoint classification.
One-sided member absence in assembly comparison is outside this direct-pair
request, and remains supported by the separate Research correspondence path.

The same physical method may occur on both sides. The adapter preserves two
side-local occurrences through the existing population sealer and receipt;
shared physical evidence does not merge the designations. It transports each
exact address with the source occurrence it names, not with another input
having the same token, path, or assembly display name.

For workspace-selected members, existing target composition supplies the
effective terminal participant and preserves root/forwarding evidence before
the designated-pair handoff. Its complete population and binding-policy rules
continue to apply. A physical `ExactAddress` request is never retargeted
through forwarding: the workspace contract explicitly excludes that operation.
The adapter consumes the resulting owner-issued association rather than
resolving the member again or adding a participant after sealing.
The workspace
[two-sided handoff](research-workspace-target-composition.md#two-sided-comparison-handoff)
distinguishes ordinary correspondence from explicit designation: the former
requires Research `Paired`, while this adapter requires #5877's designated-pair
association over the exact selected attempts. Neither route overrides
side-local composition failure or binding-policy validation.

### Execution and publication

Queries uses its sealed population and retained projection receipt for the
same invocation. It passes the two exact endpoint associations through the
Research-owned designated-pair boundary, then consumes the local session's
native results. C# Findings, semantic C# evidence, and IL evidence are retained
from those producers, not obtained by an extra caller inspection or recreated
from rendered lines.

The result is the shared step-5 Queries publication bound to that same query
and Research operation. Native unavailability and producer-local failure stay
visible inside their completed accounting; Research rejection, terminal
failure, and cancellation retain their meaning at the publication boundary.
Cancellation does not become an empty successful comparison.

Completion means the requested work was accounted for, not that two
implementations are equal. The adapter does not add a shortcut `IsExact` based
on empty changes, missing native results, or an inapplicable body. Consumers
retain native verdicts and distinguish unavailable evidence from exact
evidence. Native algorithms continue to own what their verdicts establish.

Input owners retain their acquired-source lifetime. Research owns its local
stages under the existing session contract. Any access owned by Queries uses
the existing realization and step-5 publication/cleanup contracts; this adapter
adds no cleanup authority and returns no live reader or borrowed callback.

### Host and rendering boundary

The adapter is presentation-neutral. CLI and browser hosts supply requests,
operation lifetime, and interaction; comparison tools supply their existing
target correspondence and independent oracle. None needs to recreate producer
endpoint classification.

Structured native results and published query associations survive to
presentation. Step 8 owns CLI lowering through the shared Markout presentation
path for Markdown/table, TSV, JSON, and JSONL; step 9 owns the browser facade
and comparison surface using that typed evidence. This design adds neither a
row schema nor a browser-specific rendering bypass. Any such bypass needs its
own host design rather than parsing the CLI's rendered output.

## Adoption and retirement ledger

### First production punch-through

The first bounded scenario is two explicitly selected physical methods in one
already-open implementation assembly. CLI `match --body` (formerly
`--implementation`) is the existing production caller. Browser adoption adds an
explicit comparison
action over its member selection and retained implementation participant;
ordinary selection does not silently become pair selection.

This route consumes the working assembly-context boundary rather than waiting
for unrelated package-role, whole-assembly, body-signal, or Source migrations.
It does not invoke root-to-terminal forwarding composition. Those scenarios
remain separate; a physical `ExactAddress` is not retargeted.

The selected route has **8 delivery milestones: 6 complete, 2 remaining** at
this update. These are outcomes, not a promise of eight PRs:

| Tracker step | Selected outcome | Status |
| --- | --- | --- |
| 1 | Population sealing and exact projection receipt | Complete: #5874 |
| 18 | Research designated-pair local session | Complete: #5908 |
| 5 | Lock and implement borrowed-input local publication | Complete: #5967 |
| 6 | Implement the physical-pair Queries adapter | Complete: #5967 |
| 8 | Cut over CLI `match --body`, including presentation and removal of its replaced dispatch/wrapper | Complete: #5967 |
| 9 | Add the Browser managed facade, explicit pair interaction, and typed result view | Complete: #5990 |
| 16, scoped | Remove unused or superseded Queries substrate established by this route's caller inventory | #6044 retires the unconsumed body-signal population profile |
| 17, scoped | Remove unused or superseded Research substrate established by this route's caller inventory | Remaining; retained tool/Source callers still constrain deletion; #5125 identity cleanup landed in #6099 |

Each host path contains **5 milestones, all complete**:
1, 18, 5, 6, then 8 or 9. The two scoped cleanups close the selected route,
not all global retirement in #4706.

Landing the scoped Queries cleanup completes seven of this route's eight
milestones. It does not complete whole-assembly or body-signal execution
migration, comparison-tool adoption, or global Queries retirement.

Keep the first publication and adapter runtime together with the CLI
adopting change; the bounded first-adopter exception permits that focused
pattern plus one adopting owner. Browser adoption follows immediately as
its own owner effort. Do not introduce another standalone substrate PR between
that query and its production caller.

The Browser currently has no direct member-comparison path to delete.
The CLI cutover replaces `MatchCommand.BuildImplementationDiffView`'s direct
`CompareMembers` call and synthetic result wrapper with the public query.
Final owner cleanups use
an actual caller inventory: preserve Source-dependent and assembly-comparison
behavior, but do not retain unused substrate merely for a future proposal.
Both bounded hosts now consume the public query. Their adoption and the
replaced CLI dispatch do not establish global Queries/Research retirement.

The post-adoption caller inventory for #6044 identifies the body-signal
population binding/request and its sealing/projection branches as unused
Queries substrate: only `QueryComparisonPopulationTests` constructed that
profile. Those shared identity, selection, and receipt cases now use the
implementation profile consumed by both hosts. Existing body-signal and
whole-assembly query input/result shapes still serve `DiffCommand` and
`DiffSections`, so they remain until their actual callers migrate.

The two `RoundTripCompilation` helpers now consume the public query and its
retained native outcomes (#6134). Their tools-owned
[consumer envelope](csharp-member-recompilation.md#comparison-query-consumption)
preserves correspondence, hashes, context checks, and independent verdicts.

`ReturnToSender.CompareMemberBodies` and `AuthoredRebuildFidelity` also consume
the public query (#6177), retaining its native outcomes under the tools-owned
[RTS consumer contract](fact-planned-compile-back-harness.md#member-comparison-query-consumption).
This is internal harness adoption, not new product UX or RTS replacement
readiness.

Research `CompareMembers` still serves the supported PDB-source composition.
Native result
translations still serve CLI or harness presentation. These are retained
caller obligations, not unused placeholders. The focused Research body-index
association cleanup #5125 landed in #6099; neither that cleanup nor this
comparison-tools migration completes broader retirement.

### Broader migration snapshot

[#4706](https://github.com/richlander/dotnet-inspect/issues/4706) is the single
counted end-to-end tracker. At this design's introduction it has **18 remaining
delivery steps**, including the separately exposed designated-pair prerequisite
at step 18. Step numbers are stable identifiers, not execution order:
step 18 precedes step 6.

The counted local host paths are **9 steps each**: 1, 2, 3, 4, 5, 18, 6, 7,
then 8 for CLI or 9 for browser/Wasm. Each Source-enabled host path adds four
steps, giving **13 each**. The direct comparison-tool path is **7 steps**:
1, 2, 4, 5, 18, 6, 10. Shared steps are counted once in the overall 18.
This is a planning snapshot; the tracker owns later count/status changes.

The table below records migration obligations, not new host, Research, Source,
or publication semantics. Each consumer's focused implementation owns its
adaptation. Refresh actual references before migration and attach the landing
and deletion commits to the tracker.

| Current consumer or surface | Adoption milestone | Replacement and retirement condition |
| --- | --- | --- |
| `MatchCommand.BuildImplementationDiffView` | CLI step 8 after adapter step 6 | Consume the public direct-member query; remove its direct `ImplementationDiff.CompareMembers` call and type-satisfying synthetic `ResearchComparison` wrapper when the presentation consumer moves. Preserve the separate structural-match verdict. |
| `RoundTripComparison.Compare` and `RoundTripScopeComparison.Compare` | Comparison tools step 10, two-helper slice implemented in #6134 | Consume the public query and retained native outcomes; remove direct Research dispatch and redundant `CSharpFindings.Inspect` calls. Preserve product-issued original/donor correspondence, hashes, and independent harness verdicts. |
| `ReturnToSender.CompareMemberBodies`, including `AuthoredRebuildFidelity` | Comparison tools step 10, RTS slice implemented in #6177 | Consume the public query and retain its native outcomes, replacing `BuildImplementationDiff` and its nullable legacy-result translation. Preserve the pathless donor, sibling resolution, target selection, compilation, and independent harness oracle. |
| Browser managed facade and explicit two-member workspace comparison | Browser step 9, coordinated with #5083 | Expose the same public query as observable browser behavior. Inventory actual routes then; this plan does not invent a currently existing legacy browser caller. Remove a replaced route if one exists. |
| `ImplementationComparisonQuery.Execute`, `DiffCommand`, and `DiffSections` assembly-wide paths | Steps 7-9; Source tail 13-15 | Separate from rank 5. Their public execution and result/output adoption precede final shared-shape retirement. |
| `ImplementationDiff.CompareMembersWithPdbSource` | Source steps 12-15 and Research retirement step 17 | Its call to `CompareMembers` is an explicit deletion blocker even if its present direct callers are tests. Migrate its supported Source composition or record an explicit owner/user decision to remove that behavior; do not keep a hidden legacy C#/IL route. |
| `ImplementationDiff.CompareMembers`, dependent legacy member-result shapes, and independent old/new query forms | Queries step 16 and Research step 17 | Delete superseded orchestration and shapes after the refreshed caller inventory, including Source and tests, has a disposition. Preserve only APIs justified by a current owner-local contract. #5125 was reconciled independently by the identity cleanup in #6099. |

Native `CSharpBodyDiff`, `IlAssemblyDiff`, Findings payloads, and useful aligned
hunks are not deletion targets merely because they are below Queries.
`ImplementationDiff.ToIlChanges` likewise needs a caller/purpose decision,
not blanket removal of its containing class.

An adapter-only runtime landing can complete step 6 but is **not production
adoption**. CLI, browser, and tool adoption close only when their actual
consumers move and their replaced dispatch is removed. Overall retirement
remains open through steps 16-17 and the Source-dependent tail. This design
does not claim that unused legacy APIs have already been removed.

## Demo and outcome gates

The CLI now exercises the adapter. A minimal demonstration uses a fixture
assembly containing `Left.Compute` and `Right.Compute`, each
returning the same constant but having different declared identities:

```text
dotnet-inspect match Left.Compute Right.Compute --library ./app.dll --body

Before adoption: host -> direct Research comparison -> host-built result wrapper
After adoption:  host -> direct-member query -> designated Research pair
                -> native C#/IL completion -> Queries publication -> presentation

Observe: both different member identities remain attached to their own bodies;
         native exact evidence is available without asserting identity matching.
```

The browser neighbor selects the same two workspace methods and consumes the
same typed publication through its managed facade, without replaying CLI text.
Same-member comparison remains valid. A valid bodyless endpoint preserves
native `NoApplicableInput`; a token taken from the wrong image cannot yield
an exact comparison. Ordinary Research strict correspondence of the
differently named pair remains `SelectionDrift`, covered by prerequisite #5877.

The Queries gates below run in `DirectMemberComparisonQueryTests` in Release.
CLI adoption is covered by `MatchCommandTests` and `MatchDiscoveryTests`.
Browser gates landed in #5990, including published-Wasm acceptance and
`BrowserMethodBodyOperationTests`. Comparison-tool adoption gates remain
**unimplemented and unverified**.
These names describe outcomes, not test seams or a source-scanning policy.

| Gate owner | Required observable outcome |
| --- | --- |
| Queries `DirectMemberComparison_PreservesDesignatedPair` | Public adapter compares differently named real MethodDefs and the same MethodDef on both sides, retaining exact source/address associations and native results. |
| Queries `DirectMemberComparison_DoesNotSubstituteEndpoint` | A wrong-image address or unavailable workspace terminal produces visible non-success; it never compares a same-token substitute. |
| Queries `DirectMemberComparison_RetainsNativeNonSuccess` | Bodyless and failed producer cases retain native classification; missing evidence does not report exactness. |
| Queries `DirectMemberComparison_UsesSharedPublication` | Public result preserves its population association, terminal cancellation, and owned-cleanup outcome through the step-5 boundary; no parallel adapter result route is needed. |
| CLI and browser adoption gates in steps 8-9 | Real host invocations show the designated pair and visible unavailable/failure evidence using the public query, including ordinary output selection and the browser facade. |
| Comparison-tool adoption gates in step 10 | Existing member, scope, and authored-rebuild scenarios consume the public query without changing correspondence or oracle meaning. |

Deletion evidence is the actual migration/removal diff and a refreshed caller
inventory attached to the tracker. This design adds no universal repository
API-absence claim or source-policing gate.

## Non-goals

No general assembly comparison, candidate discovery, Source/PDB/network work,
new Research correspondence currency, native producer algorithm, body-signal
migration, general publication schema, output format, browser UI contract,
cross-component budget, or new lifetime protocol is defined here.
The adapter reuses existing sequential execution; it introduces no concurrent
state machine needing a separate model.
