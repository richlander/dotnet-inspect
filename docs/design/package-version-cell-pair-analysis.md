# Package version-cell pair Analysis

## Status, owner, and claim

Status: **implemented** by
[#7248](https://github.com/richlander/dotnet-inspect/issues/7248).

The **Package version-cell pair Analysis** owner defines one
`DotnetInspector.PackageQueries` operation family:

> Execute either one exact PackageHouse version-population cell for a baseline
> or one exact source/destination pair for a checkpoint, bind one
> Diff-History-issued exact-Member receipt without replaying its selector,
> compose strict API correspondence with matched implementation Analysis in one
> bounded ephemeral Workspace, and return only detached resource-free
> evaluations after awaited Workspace close.

This is a focused package-aware L1 composition owner. It does not choose the
History seed, define receipt meaning, perform version discovery, redefine API
correspondence or Analysis, or introduce a host terminal.

## Motivation and real assets

Diff History needs one reusable evaluator for the Analysis producers named by
[Diff History](diff-history.md):

- `analysis.allocation`;
- `analysis.call-site`; and
- `analysis.unsafety`.

The evaluator must preserve the distinction between a source declaration,
the destination declaration related to it, and the implementation body
analyzed for that destination. Replaying a textual selector in each Version
would instead create unrelated local selections that only happen to share
display text.

The motivating packages are:

- `System.Text.Json@8.0.6`, `9.0.0`, and `10.0.0`, which provide an ordinary
  multi-Version package population for stable exact-Member rebinding; and
- `Avalonia@11.3.14` and `12.1.2`, where public API ownership may move through
  a forwarding route while matched Analysis must still inspect the terminal
  implementation body.

Pinned package fixtures preserve those real shapes. Independently compiled
fixtures provide otherwise unreachable ambiguity, refusal, malformed-image,
and cleanup boundaries.

## Design basis

There is no general .NET API that combines package-population identity,
cross-Version declaration correspondence, implementation-body realization,
and detached Finding production. This owner therefore composes existing
repository contracts rather than inventing a second matching or loading
algorithm:

| Concern | Owner | Consumed contract |
| --- | --- | --- |
| Seed choice and stable receipt meaning | [Diff History](diff-history.md) | First-Version selection, one stable `FindingSubject`, and direct-from-seed checkpoint policy |
| Exact cell execution | [PackageHouse](package-house.md) | Prepared reporter-bound cell execution and accepted settlement |
| Root construction and bounded lifetime | [Package version-cell Metadata inspection](package-version-cell-metadata-inspection.md), [Package Root realization](artifact-acquisition-and-workspaces.md#package-root-realization), and [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Existing House-to-Root adapter, finite realization limits, one ephemeral Workspace, scoped borrowing, and awaited close |
| Source selection | `ApiCoordinateSourceSelectionQuery` | One baseline-only exact Member selection and its owner-issued detached projection |
| Declaration relationship | `ApiCoordinateCorrespondenceQuery` and its detached evidence | Strict exact, absent, ambiguous, refused, and failed outcomes |
| Implementation Analysis | [Matched API Member Analysis](matched-api-member-analysis.md) | Exact declaration-to-implementation resolution and native allocation, call-site, and unsafety Findings |
| Finding state | [Finding adoption](finding-adoption.md) | Native complete, subject-absent, no-applicable-input, and failed inspection meanings |

The operation owns only the sequencing, receipt binding, bounded composition,
Finding projection for non-exact relationships, and detached terminal result.

## Exact source receipt

Baseline selection issues one immutable resource-free source receipt. Its
public identity atomically retains:

- canonical package ID, normalized Version, producer, framework, and runtime
  identifier;
- the exact selected compile asset and assembly reference identity;
- the exact Type, Member anchor, and declaration kind; and
- the caller-supplied stable `FindingSubject`.

The receipt also retains a private association to the exact
PackageHouse-issued source cell. That association validates later source-cell
binding without retaining the cell itself or exposing a display ordinal.

The receipt retains no:

- population cell or prepared House request;
- Workspace, Root, package content, image, stream, lease, or opening callback;
- live Type or Member subject;
- destination Version;
- selector text as execution authority;
- Workspace-local registration identity; or
- metadata token as cross-Version identity.

The public declaration coordinate is descriptive evidence. Its producer field
is the package-source `PackageProducerIdentity`, not the selected Analysis
producer. The private source-cell association is binding evidence. Neither
grants authority to open the package or reconstruct a Workspace.

The `FindingSubject` is supplied once by the Diff History caller and retained
unchanged. This owner does not derive it from display text, metadata tokens,
destination declarations, or Finding output.

## Requests and finite bounds

One endpoint request carries:

- one exact `PackageHouseVersionPopulationCell`;
- one caller-supplied `Realize` operation; and
- one package target context.

Construction prepares the exact House execution once. A baseline request
contains one endpoint, one exact Type/Member selector with a required Member,
one stable `FindingSubject`, finite realization limits, and one absolute Workspace
deadline.

A checkpoint request contains:

- the original source endpoint;
- one destination endpoint;
- the unchanged source receipt;
- the same finite realization limits; and
- one absolute Workspace deadline.

The checkpoint request carries no Member selector. Its source endpoint must
match the receipt's private cell association at request construction. Source
and destination endpoints must belong to the exact same settled population and
must name different cells. Association mismatch, population mismatch, and a
same-cell checkpoint are invalid requests, not evaluated Finding failures.

The finite limits are the shared Package version-cell Workspace limits:

- positive assembly count per role;
- positive selected-entry byte limit; and
- positive aggregate retained-image byte limit.

Package Root realization and Workspace admission remain the enforcement
owners. Caller cancellation is supplied only to execution.

## Baseline contract

For one baseline request, the operation:

1. executes the exact prepared source cell once;
2. adapts its accepted settlement to one package Root contribution;
3. admits that Root to one bounded ephemeral Workspace;
4. creates the live source package observation required by Queries;
5. resolves the Member selector once through the existing source-selection
   query;
6. detaches the owner-issued source-selection evidence while the observation
   remains live;
7. when selection is one exact Member, issues the source receipt;
8. validates that declaration through strict source self-correspondence;
9. enters a fresh scoped source-Root borrow and invokes the selected matched
   Analysis producer with the receipt's unchanged
   `FindingSubject` only when validation is exact; and
10. detaches correspondence evidence and awaits Workspace close before
    publishing the detached result.

Exact source selection produces a valid receipt even if self-correspondence or
matched Analysis later fails. The baseline result publishes the validation
evidence but no same-Version declaration edge.

Absent, ambiguous, refused, or failed source selection produces no receipt and
invokes no matched Analysis. Queries owns and issues the detached
`ApiCoordinateSourceSelectionEvidence`, including portable Type candidates,
Member anchors, inspection failures, and typed failure evidence. This owner
does not retain or reinterpret live `StructuralSubjectIdentity` candidates
after close.

An exact source declaration whose self-correspondence is not exact is not
`SubjectAbsent`: source selection already established the declaration.
The operation projects that non-success to a failed `FindingInspection<T>`
while retaining the receipt and detached validation evidence.

## Checkpoint contract

For one checkpoint request, the operation:

1. executes exactly the prepared source and destination cells;
2. adapts both accepted settlements to package Root contributions;
3. admits both Roots atomically to one bounded ephemeral Workspace;
4. creates the live source and destination package observations required by
   Queries;
5. exact-binds the receipt's portable declaration coordinate in that source
   Root without invoking selector-based source selection;
6. invokes strict source-to-destination correspondence while both observations
   remain live;
7. enters a fresh scoped destination-Root borrow and invokes matched Analysis
   only for an exact destination Member while the live correspondence result
   remains available;
8. detaches correspondence evidence; and
9. awaits Workspace close before publishing the detached result.

Source binding is strict across package coordinate, selected asset, assembly
reference identity, Type, Member anchor, and declaration kind. A same-named
declaration in another source cell, asset, assembly, Type, overload, or
declaration kind is not the receipt source.

Binding evidence distinguishes Library absence or ambiguity from declaration
absence, ambiguity, refusal, or failure. `Exact` means both layers bound.

Checkpoint outcomes map declaration relationship to Finding state as follows:

| Relationship | Finding state |
| --- | --- |
| Exact | Native matched Analysis result |
| Complete destination absence | `Absent(SubjectAbsent)` |
| Ambiguous | Failed |
| Refused | Failed |
| Correspondence failure | Failed |
| Runtime source binding non-success | Failed |

The operation does not convert Analysis `NoApplicableInput` or Analysis failure
into another state. A non-exact checkpoint remains an evaluated gap and does
not prevent a later checkpoint from binding directly to the original receipt.

## Detached evaluation

The public entry points are producer-specific:

- allocation returns `FindingInspection<AllocationOccurrence>`;
- call-site returns `FindingInspection<DirectCall>`; and
- unsafety returns `FindingInspection<UnsafetyOccurrence>`.

The selected entry point fixes an explicit
`PackageVersionCellAnalysisProducerKind` in the result. A request cannot pair
one Analysis producer with another producer's Finding type or descriptor.

The common terminal operation outcome retains ordered detached execution
evidence for the one baseline endpoint or the source/destination checkpoint
endpoints. It distinguishes:

- **Available** — the baseline or checkpoint evaluation;
- **No contribution** — one or more exact House settlements that could not
  contribute a package Root;
- **Workspace failure** — Scope admission or source/destination Root
  observation could not complete under an owner-typed failure; and
- **Cleanup failure** — bounded cleanup evidence after a provisional
  successful evaluation.

An available baseline evaluation retains:

- the source receipt;
- detached source self-correspondence evidence; and
- the selected producer's `FindingInspection<T>` plus any matched-body
  resolution evidence.

An available checkpoint evaluation retains:

- the unchanged source receipt;
- detached source-to-destination correspondence evidence; and
- the selected producer's `FindingInspection<T>` plus any matched-body
  resolution evidence.

Source-selection non-success is an available baseline observation because it
is the complete answer for the requested source cell. It retains no receipt or
Finding inspection.

Runtime source-binding, correspondence, and matched Analysis non-success are
completed checkpoint evaluations, not Workspace failures. They retain their
owner-issued evidence and the corresponding native or projected Finding state.

Every public result is descriptive and resource-free. It retains no cell,
prepared request, package content, Root binding, Workspace, live API subject,
assembly context, image, stream, lease, callback, cancellation token, or close
exception.

## Terminal precedence and cleanup

Both forms preserve the existing Package version-cell cleanup precedence:

1. caller cancellation remains cancellation with the caller's token;
2. an unexpected exception remains the exact primary exception;
3. an owner-typed House or Workspace failure remains primary;
4. awaited Workspace close always runs after Workspace creation;
5. bounded cleanup evidence attaches to a propagated exception or
   cancellation;
6. cleanup failure replaces only a provisional available evaluation; and
7. cleanup evidence remains secondary beside a typed Workspace failure.

Source-selection, source-binding, correspondence, and Analysis non-success are
inside an available evaluation. A cleanup failure therefore replaces that
whole provisional evaluation rather than publishing a completed query result
from a Workspace that did not close cleanly.

Cleanup evidence contains only stage and count. It does not retain exception
instances, messages, paths, streams, or resource handles.

## Developer evidence and host boundary

This operation is an L1 producer, not a terminal
`InspectionEnvelope<TContent>` owner. It therefore does not add a Debug host
gesture or return a second evidence envelope.

Its detached owner-issued facts are the reusable input to a later Diff History
`TEvidence` document. That Debug-only document can answer questions such as:

- Were the exact requested cells executed and admitted?
- Did the checkpoint bind the original source receipt rather than replay a
  selector?
- Which ordered forwarding route reached the terminal defining Library?
- Which implementation MVID and MethodDef token supplied the analyzed body?
- Did the selected Finding producer run, and what native state did it return?

A forwarding-hop count is meaningful only with its ordered route, source and
destination population, and completion state. Required facts for interpreting
the user-visible History result remain in ordinary Content or Diagnostics.
The final shared Diff History terminal owns any
`EvidenceInspectionEnvelope<TContent, TEvidence>` adoption, and complete
evidence delivery remains a Debug-only host capability.

## Pathological case

The source receipt names an exact public Member in `Avalonia.Markup.dll`.
One checkpoint forwards that declaration to `Avalonia.Base.dll`; a later
checkpoint has complete destination absence; a still later checkpoint again
resolves exactly.

Each checkpoint binds directly to the original source receipt. The first and
last exact checkpoints analyze their own terminal implementation bodies. The
middle checkpoint publishes `SubjectAbsent`. No checkpoint becomes the source
for another, no missing declaration ends the series, and no display name,
ordinal, or Finding key bridges the gap.

## Pair execution order

Checkpoint host execution is deterministic and source-first. The operation
executes each prepared endpoint exactly once and retains role-tagged execution
evidence. A source no-contribution result does not suppress destination
execution; after both settlements, every no-contribution reason is retained
and no Workspace is constructed.

An exception, caller cancellation, or invalid exact-settlement correspondence
during source execution is terminal and prevents destination execution because
there is no valid source settlement to compose. Those states are not
success-shaped pair outcomes.

## Production path

This operation is step 6 of the temporal ownership path:

1. PackageHouse version-population settlement — complete in #7133.
2. Package version-cell Metadata and explicit API Finding evidence — complete
   in #7146 and #7280.
3. Exact matched API Member to implementation Analysis — complete in #7303.
4. Diff History baseline-required receipt and direct source-to-checkpoint
   policy — complete in #7315.
5. Queries-owned detached coordinate-correspondence evidence — complete in
   #7337.
6. Bounded PackageHouse baseline/checkpoint Analysis — this owner.
7. Shared Diff History and metadata-only version-count terminals, followed by
   top-level Diff and subject-section adoption with `timeline` removal.
8. Browser Compare and version-count adoption.

The later Diff History terminal supplies the population order and stable
`FindingSubject`, invokes this operation independently for each checkpoint,
and joins the detached evaluations. This operation does not construct the
History graph or choose a replacement seed.

## Evidence

The Release gate is the package version-cell Analysis test family:

| Claim | Release evidence |
| --- | --- |
| Exact baseline selection and receipt | `BaselineIssuesReceiptAndAnalyzesExactMember` and `PinnedMarkoutBaselineIssuesReceipt` |
| Producer-typed entry points | `BaselineProducerEntrypointsPreserveFindingTypes` |
| No baseline self-edge | `BaselineIssuesReceiptAndAnalyzesExactMember` |
| Exact receipt binding | `CheckpointBindsReceiptWithoutReplayingDestinationOrdinal` and `PinnedSystemTextJsonCheckpointBindsReceipt` |
| Direct sparse checkpoints | `CheckpointAbsenceDoesNotPreventLaterExactEvaluation` |
| Strict source identity | `CheckpointRequestRejectsForeignReceiptBeforeExecution` and `RuntimeSourceLibraryMismatchIsFailedEvaluation` |
| Forwarded destination Analysis | `PinnedAvaloniaMoveRetainsRouteAndAnalyzesTerminalBody` |
| No-contribution pair evidence | `PairNoContributionExecutesAndRetainsBothEndpoints` |
| Cancellation | `CancellationAtAnalysisStagePublishesNoOutcome` |
| Cleanup precedence | `AnalysisCleanupPrecedenceMatchesSharedCellLifecycle` |
| Full resource-free closure | `AnalysisOutcomePublicAndRuntimeClosureIsResourceFree` |

Targeted tests preserve real `System.Text.Json`, `Markout`, and `Avalonia`
package evidence where those packages exhibit the contract. Synthetic fixtures
cover only states that cannot be reached reliably from immutable public
packages.
