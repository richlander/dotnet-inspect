# Matched API Member Analysis

## Status, owner, and claim

Status: implementing for [#7303](https://github.com/richlander/dotnet-inspect/issues/7303).

This document owns one Queries composition: given one exact successful API
Member correspondence and the live package realization containing its
destination, resolve that API declaration to the owner-paired implementation
MethodDef and publish one requested native Analysis Finding census.

It does not select or match the API Member, acquire a package, define package
roles, choose a Diff History seed, evaluate a Version population, or join
History edges.

## Motivation and demo

API correspondence and method-body Analysis use different physical subjects.
An exact API destination may be declared by a reference assembly while its IL
body is supplied by a paired implementation assembly. MethodDef row numbers are
module-local, so applying the API declaration token to the implementation image
can silently analyze an unrelated neighboring method.

The production case is the `Avalonia` package, whose compile surface and
implementation are distinct `ref` and `lib` images. The pathological
fixture uses two independently compiled images with the same assembly identity:

- the surface image declares `Widget.Transform(int)` at one MethodDef row;
- the implementation image inserts and reorders neighboring methods; and
- the implementation `Widget.Transform(int)` body has a different token.

Given an exact API match for `Widget.Transform(int)`, the query returns Findings
from the implementation method selected by strict structural correspondence.
It never applies the surface token to the implementation image. A neighboring
Property match is `NoApplicableInput`; a missing implementation role or
non-exact method correspondence is a visible failed census.

## Basis and owner map

This composition consumes the following owner-issued claims without redefining
them:

- [Forwarded API coordinate correspondence](forwarded-api-coordinate-correspondence.md)
  supplies an exact destination Member, its actual defining surface Library,
  and strict declaration evidence.
- `PackageAssemblyContextRealization` supplies the exact surface participant and
  its zero-or-one implementation-role participant. A package may use distinct
  `ref` and `lib` groups, one shared compile-fallback group, or no paired
  implementation.
- Metadata's `MethodCorrespondenceResolver` supplies exact, absent, ambiguous,
  and failed cross-reader MethodDef correspondence. Tokens and display text are
  not cross-module identity.
- `AssemblyContextMethodAnalysisQuery` supplies body-scoped Analysis evidence
  while Queries owns the retained implementation image.
- `AnalysisFindings` supplies producer-issued allocation, call-site, and
  unsafety Finding descriptors, keys, ordering, and payloads.
- `FindingInspection<T>` supplies complete, absent, and failed census
  semantics.

The composition belongs in `DotnetInspector.Queries`: its public contract is a
product query over an already realized package role and several IL inspection
results, not a new Metadata or Analysis fact.

## Input association

The operation accepts:

- one exact `ApiCoordinateCorrespondenceResult` whose source and destination
  are Members;
- the live destination `PackageAssemblyContextRealization`;
- a caller-issued `FindingSubject`; and
- one explicit allocation, call-site, or unsafety entry point.

The correspondence result remains input evidence, not retained output. The
operation validates that:

1. the composite and strict declaration correspondence are exact;
2. the strict target is a Member declaration and equals the composite
   destination Type and `MemberAnchor`;
3. the target endpoint was minted for exactly one surface participant in the
   supplied realization; and
4. the target MVID and MethodDef address agree with the opened surface image.

A foreign, stale, or internally inconsistent association is a typed failed
census. It does not authorize a Library-name, assembly-name, MVID-only, path,
or Member-display search.

## Implementation MethodDef resolution

For a non-Method API declaration, no MethodDef body is applicable and the
requested inspection returns `NoApplicableInput`.

For a Method declaration:

1. obtain the exact implementation participant paired with the defining
   surface participant;
2. if no implementation participant exists, fail because the package role
   cannot establish which physical body supplies this API;
3. if both roles identify the same exact participant, validate and retain the
   API MethodDef address without making a cross-image correspondence claim;
4. otherwise open both exact retained images and invoke
   `MethodCorrespondenceResolver` with the API MethodDef address;
5. preserve exact, absent, ambiguous, and failed method correspondence as typed
   resolution evidence; and
6. inspect the exact implementation MethodDef for an IL body before invoking
   Analysis.

An exact implementation MethodDef with no managed body returns
`NoApplicableInput`. Absent, ambiguous, or failed method correspondence does
not prove bodylessness and therefore returns a failed census.

The operation does not resolve Property or Event accessors. Accessor selection
is a separate source-selection contract and remains outside this slice.

## Finding result

Each producer entry point returns:

```csharp
MatchedApiMemberAnalysisResult<T>
```

The result contains detached `MatchedApiMemberAnalysisEvidence` and the native
`FindingInspection<T>`. Evidence records:

- the API declaration kind, Type name, and destination `MemberAnchor`;
- surface and optional implementation assembly identities, asset paths, MVIDs,
  and physical MethodDef tokens;
- whether the exact body used the same participant or strict cross-image method
  correspondence;
- native method-correspondence status and bounded candidate addresses; and
- the failed or inapplicable stage and detail when Analysis does not run.

These fields are descriptive evidence scoped to their recorded modules. They
are not package, Workspace, image, or generation reopening authority.

For an exact body with no Analysis diagnostic, the producer entry point returns
`FindingInspection<T>.Complete`; an empty Finding array is a complete empty
census. Any diagnostic for the requested method makes that producer census
`Failed`, rather than successful and empty.

Image rejection, invalid association, unavailable implementation pairing,
method correspondence non-success, Analysis image failure, malformed Metadata,
and Analysis diagnostics remain visible `Failed` inspections. Cancellation
is observed during body Analysis and again before result publication,
propagates with the caller token, and is not an outcome arm.

## Lifetime and closure

The operation borrows the realization only during execution. It may open the
surface and implementation snapshots concurrently for strict correspondence,
then releases them before method Analysis returns. Analysis retains only its
existing body-scoped snapshot and index lifetime.

The public result closure retains no:

- Workspace, Root, package content, realization, participant, or group;
- acquisition registration, image, session, reader, stream, lease, or opener;
- callback, delegate, exception, or disposable resource; or
- generation-bound execution or reopening authority.

The full transitive closure gate traverses generic carrier members, nested
outcome arms, collections, union `object` payloads, ignored properties, public
and private instance fields, and representative runtime values. JSON omission
is not closure evidence.

## Failure precedence

Caller-contract null and invalid producer arguments remain ordinary argument
exceptions. Once valid exact correspondence input is accepted, operational
non-success is represented in the returned evidence and
`FindingInspection<T>`.

The order is:

1. validate correspondence and destination-realization association;
2. establish the paired implementation participant;
3. resolve and validate the implementation MethodDef;
4. execute body Analysis;
5. reject an incomplete Analysis result carrying diagnostics; and
6. project the requested native Findings.

No later stage relabels an earlier failure as absence. The operation has no
cleanup protocol of its own; the owning Workspace and package-role operation
retain their existing terminal cleanup precedence.

## Production path

This operation is the package-neutral prerequisite for the corrected
[#7248](https://github.com/richlander/dotnet-inspect/issues/7248) cell-pair
owner:

1. this owner resolves one exact matched destination API Member to native
   implementation Analysis Findings;
2. [Diff History inspection](diff-history.md) resolves the baseline source
   Member once and issues its detached exact declaration, kind, and stable
   `FindingSubject` receipt;
3. #7248 invokes this query first through internal exact same-cell
   correspondence for the detached baseline observation, then through one
   bounded reporter-bound source/destination PackageHouse cell pair per later
   checkpoint after exact-binding that same receipt in the pair-local
   Workspace; every invocation reuses the receipt's `FindingSubject`;
4. Diff History evaluates its chosen checkpoints serially and joins detached
   sparse node and correspondence-edge evidence;
5. the subject CLI and Browser/Wasm consume the same shared
   `InspectionEnvelope<TContent>` terminal; and
6. the standalone `timeline` implementation is removed without compatibility.

Diff History separately owns the now-locked baseline-required receipt, direct
seed-to-checkpoint correspondence across gaps, transition classification,
Count, and detached output retention or streaming. The Workspace-local source
Member produced by each exact pair binding does not survive that pair, while
the baseline-issued `FindingSubject` remains unchanged across observations.
That owner invokes this query only for exact correspondence; it separately
projects strict declaration absence to `SubjectAbsent` and binding,
correspondence, or operational non-success to Finding failure.

## Evidence

Focused gates run in `DotnetInspector.Queries.Tests` in Release:

| Claim | Release evidence |
| --- | --- |
| Reordered physical methods | `ReorderedMethodDefsAnalyzeExactImplementationTarget` proves the implementation token differs from the API token and the neighboring same-row method is not analyzed. |
| Same-image compile fallback | `SharedCompileFallbackUsesExactApiMethod` proves one shared role uses the exact validated MethodDef without a cross-image claim. |
| Inapplicable declarations | `NonMethodAndBodylessTargetsAreNoApplicableInput` distinguishes Property and bodyless Method inputs. |
| Missing implementation | `ReferenceOnlySurfaceFailsWithoutEmptyCensus` preserves the missing owner pairing as failure. |
| Native producers | `NativeFindingProducersPreserveKeysAndEmptyCensuses` exercises allocation, call-site, and unsafety projections. |
| Strict non-success | `MethodCorrespondenceNonSuccessRemainsFailed` preserves native absent, ambiguous, and failed status. |
| Real package shape | `AvaloniaRefLibMatchUsesImplementationRole` exercises the pinned Avalonia 11.3.14 to 12.1.2 forwarded `MultiBinding` constructor through the actual defining `Avalonia.Base` `ref`/`lib` pair. |
| Analysis cancellation | `MethodAnalysis_CancellationDuringAnalysisPropagatesCallerToken` blocks inside Analysis binding resolution, cancels the caller token, and proves the same token escapes rather than becoming an artifact failure or Finding outcome. |
| Detached lifetime | `ResultRemainsUsableAfterWorkspaceClose` reads the complete result after awaited Workspace close. |
| Full result closure | `PublicResultClosureIsResourceFree` traverses the complete public and representative runtime result graph and rejects every forbidden resource family named above. |

Analysis image rejection and diagnostic projection reuse
`AssemblyContextMethodAnalysisQuery`'s existing focused gates. A composed
malformed implementation image that first passes package-role realization is
**unverified** because role construction already rejects malformed selected
images before this query can run.

## Non-claims

This owner does not define or perform:

- source Member selection or cross-Version API correspondence;
- Library pairing, type forwarding, or API declaration identity;
- package acquisition, PackageHouse execution, Root admission, or Workspace
  lifetime;
- package-role selection or surface-to-implementation pairing;
- new Metadata method identity or Analysis producer semantics;
- Property/Event accessor selection;
- Diff History seed, checkpoint, gap, graph, transition, Count, storage, or
  streaming policy;
- Finding comparison, rendering, sections, Markout lowering, Share, CLI
  binding, or Browser controls; or
- concurrent or population-wide Analysis.
