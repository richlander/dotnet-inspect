# Resource ownership and borrowing

## Status and approved scope

This document is the normative owner for the cross-cutting resource ownership
and borrowing pattern. It is tracked by
[#6544](https://github.com/richlander/dotnet-inspect/issues/6544).

The user approved the paired goal:

1. define one coherent ownership and borrowing contract; and
2. ensure dotnet-inspect resource-lifecycle analysis can discover violations.

The pattern defines the shared lifecycle vocabulary and declaration boundary.
It does not migrate any existing owner in this document. Analysis is the first
adopter through a separate focused effort, using the existing ArrayPool
ownership flow and Resource Triage product path as its implementation and
corpus baseline. Artifact, Library, PackageHouse, PlatformHouse, SourceHouse,
DocumentationHouse, Workspace, CLI, and Browser/Wasm adoption remain
independently reviewed steps in the tracker.

The first product architecture waiting on the pattern is the content-backed
Library handoff used by SourceHouse. PackageHouse, PlatformHouse, and
direct-library or Workspace adapters need to transfer retained assembly and
companion-content ownership without SourceHouse reacquiring the same content.
SourceHouse and other Library consumers then borrow that content under the
transferred issuer-provided lifetime.

This is a focused cross-cutting pattern under
[Design Scope](../design-scope.md#stage-implementation-after-locking-the-design).
It owns no resource-specific acquisition, cleanup, House, or Analysis
algorithm. Every existing owner adopts the pattern separately.

## Authority and exact claim

**Resource Ownership and Borrowing** owns:

> Given one declared resource contract and metadata-visible acquisition,
> transfer, borrow, and release effects, define the shared meanings of owner,
> authorization, lease, borrow, reference, receipt, transfer, and release so
> current C# can express each lifecycle explicitly, future C# ownership can
> adopt the same contract without semantic inversion, and Analysis can report
> supported violations without inferring ownership from names.

The owner defines:

- the distinction between authorization, ownership, leasing, borrowing,
  referencing, receipts, and scenario settlement;
- the single-owner and explicit-transfer lifecycle;
- the requirement that a lease be named for its resource and issued by its
  focused resource issuer,
  never by a House or consumer;
- the requirement that references and receipts contain no hidden live lease;
- the aggregate adoption, transfer, and release protocol;
- synchronous read-only and mutable borrowing through lifetime-bounded views;
- mutable-borrowed, read-only-borrowed, and consuming receiver effects;
- the current C# lowering to `IDisposable`, `IAsyncDisposable`, `ref struct`,
  `scoped`, `ReadOnlySpan<T>`, and `Span<T>`;
- the enforcement ladder from current compiler prevention through runtime
  rejection, Analysis detection, and future compiler ownership;
- the explicit residual risk when current C# cannot prevent a violation;
- the boundary between synchronous release and required asynchronous
  settlement;
- the owner-neutral declaration sources normalized for Analysis;
- the minimum ownership effects every declaration must expose;
- the requirement that supported violations and incomplete analysis remain
  visible; and
- the correspondence to future compiler-supported `IResource`, `Drop`,
  `Borrow<T>`, and `ReadOnlyBorrow<T>`.

It does not define:

- any resource's identity, construction, authorization, acquisition, content,
  correspondence, revocation, quiescence, or cleanup algorithm;
- the concrete Library lease or borrowed Library view;
- PackageHouse, PlatformHouse, SourceHouse, DocumentationHouse, Workspace, or
  artifact behavior;
- which package-source contract replaces `PackageHouseSourceLease`;
- Analysis IL decoding, aliasing, control-flow, interprocedural, confidence,
  Finding, or presentation algorithms;
- CLI options, configuration-file syntax, browser controls, rendering, or
  serialized transport;
- a repository replacement for future compiler intrinsic types; or
- a claim that current C# enforces uniqueness or borrowing without Analysis.

Those owners consume this vocabulary and declare their own effects. Analysis
interprets the declarations and owns every conclusion about inspected code.

## Design basis

The pattern starts from three compatible precedents.

### Rust ownership and borrowing

Rust's useful distinction is not syntax but responsibility:

- one value has one owner;
- leaving the owner's scope releases the value;
- ownership moves rather than becoming another owning alias; and
- temporary references borrow without acquiring the release obligation.

The pattern adopts that conceptual split. It does not claim current C# has
Rust's compiler enforcement.

### Proposed C# ownership

The pinned
[C# ownership proposal](https://github.com/agocke/csharplang/blob/e6c81ce6df194d571e5f61969da87a15e075d3a6/proposals/ownership.md)
provides the intended future language correspondence:

- `IResource` marks an owned resource;
- owning values move instead of copy;
- compiler-invoked `Drop` runs once at lexical scope exit;
- ordinary resource parameters consume ownership;
- instance receivers borrow;
- `Borrow<T>` and `ReadOnlyBorrow<T>` carry owner-derived lifetimes; and
- a span returned from a resource instance member cannot outlive the borrowed
  receiver.

Its `RentedArray` example is particularly relevant: the owned value hides the
pooled array, exposes only `Span<T>`, and returns the array from `Drop`.

dotnet-inspect deliberately does not introduce convention-only versions of
`IResource`, `Owned<T>`, `Borrow<T>`, or `ReadOnlyBorrow<T>`. A repository
interface cannot make values move-only, invalidate a prior owner, invoke
transitive cleanup, or make a borrow lifetime compiler-enforced.

### Existing dotnet-inspect evidence

The artifact layer already demonstrates the strongest current-C# synchronous
borrow shape:

- `ArtifactAdmissionContentView` and `ArtifactQueryContentView` are
  `readonly ref struct` values;
- their content is `ReadOnlySpan<byte>`;
- callbacks receive the views through `scoped` parameters; and
- the retained owner validates the caller's lease before issuing the view.

Analysis already demonstrates the detector path:

- `ArrayPoolOwnershipFlow` records return, store, caller-transfer, and
  forwarding effects;
- `ResourceLifecycleAnalysis` publishes
  `analysis.resource-lifecycle` Findings;
- incomplete decode, resolution, metadata, body, or control-flow evidence is a
  failed inspection rather than a clean result; and
- the Resource Triage corpus has historically confirmed all nine
  untrusted-actionable exception-path pool-retention candidates.

These are evidence that the pattern can lower to current C# and to a useful
product analysis. They are not declarations that the existing repository
already conforms to the target contract.

## Current repository lifetime model

The repository does not currently have one ownership model. It has several
useful mechanisms with different meanings and enforcement. Representative
shapes are:

| Current shape | Current strength | Current overhang |
| --- | --- | --- |
| `ArtifactAdmissionLease` and `ArtifactQueryLease` | The issuer validates generation, authorization, revocation, and disposal on every access. | A normal class reference can still be copied, transferred without invalidating the source variable, or disposed through more than one alias. |
| `ArtifactAdmissionContentView` and `ArtifactQueryContentView` | `readonly ref struct`, `scoped`, and `ReadOnlySpan<byte>` prevent supported synchronous borrow escape. | The shape does not cover heap-escapable class borrows or work that crosses `await`. |
| `IArtifactAcquisitionLease` and artifact-session disposal | `IAsyncDisposable` exposes required asynchronous cleanup and quiescence. | Current C# does not prevent dropping the returned awaitable or treating retirement as completed settlement. |
| `AssemblyContextGroup` owned-resource registration | One aggregate tracks child `IDisposable` values, releases them before snapshots, and preserves cleanup failures. | Registration, transfer, release ordering, and transitive child cleanup are manually maintained. `IDisposable` supplies no ownership metadata. |
| `ArtifactContentReference` and assembly openers | Identity, registration, provenance, and usable retained content remain associated. | Some heap-escapable references and delegates close over live access authority, so identity and ownership are not consistently separate. |
| `PackageHouseSourceLease` | Disposal visibly retires future package settlement. | The House issues a hybrid capability named for its consumer scenario rather than a focused resource, and disposal does not represent ownership of the underlying source clients. |
| `ArrayPoolOwnershipFlow` and Resource Triage | Analysis already follows return, storage, caller transfer, forwarding, and exception-path leakage with explicit incompleteness. | The model is API-specific and cannot yet consume repository resource declarations. |

The target does not merely rename these values. It simplifies their shared
accounting:

- one lease denotes one explicit release or settlement obligation;
- one current owner holds that obligation until transfer or release;
- resource issuers validate leases, while Houses only hold, borrow, transfer,
  or release them;
- references and receipts contain identity and evidence, never hidden
  ownership;
- owned child readers and streams declare their parent-retention obligation;
- aggregate owners expose one declared child-adoption and ownership-transfer
  boundary even when current fields, collections, and cleanup loops remain;
- operation leases carry authority across asynchronous work, while scoped
  views provide synchronous byte access; and
- one normalized Analysis contract replaces API-specific inference for
  adopted resources.

This simplification is primarily semantic and source-level. Current C# may
still lower several owning scopes to several exception-handling regions, and
resource-specific owners retain their quiescence and cleanup algorithms. The
pattern removes ambiguous responsibility and makes supported bookkeeping
analyzable; future compiler ownership can later remove more of the remaining
manual mechanics.

## Vocabulary

| Term | Meaning | May carry live authority? |
| --- | --- | --- |
| Resource | A finite or exclusive value whose use has a terminal obligation. | Yes |
| Resource issuer | The focused service, pool, store, session, or equivalent component that creates and validates the resource lifecycle. | Yes |
| Owner | The current holder responsible for transferring or releasing one obligation. | Yes |
| Authorization | Issuer-provided permission to request or issue a lease. It is not itself temporary use of the resource. | Yes |
| Lease | The uniquely owned value carrying temporary authority and the obligation to release or settle it. | Yes |
| Borrow | Temporary non-owning access whose lifetime is bounded by a live owner or lease. | Yes, but never independently |
| Reference | Resource identity and correspondence used to address a resource under separately supplied authority. | No |
| Receipt | Durable evidence of a completed decision, transfer, or settlement. | No |
| Transfer | Movement of the release obligation from one owner to another. | Yes |
| Release | The terminal synchronous or asynchronous operation that ends ownership. | Ends authority |
| Settlement | A House or operation result over a scenario. Settlement may transfer or release leases but is not itself a lease kind. | Only through explicit transferred leases |

An issuer's authorization may be revocable and may issue multiple independent
leases when the resource-specific issuer permits that shape. This pattern does
not require every underlying resource to have globally unique access. It
requires each release obligation to have one current owner.

## Normative ownership lifecycle

One declared release obligation follows this state machine:

```text
acquired
   |
   v
owned --borrow--> temporarily used --end borrow--> owned
   |
   +--transfer--> owned by recipient
   |
   +--synchronous release-------------------------> ended
   |
   +--invoke asynchronous release--> settling
                                      |
                                      +--observed success--> ended
                                      |
                                      +--fault, cancellation,
                                         dropped observation,
                                         or unsupported flow
                                           --> visible failure or incomplete
```

The following rules are normative.

1. Acquisition creates one owner for every release obligation.
2. A transfer consumes the source ownership and creates ownership for the
   recipient. The source cannot release, borrow, or transfer the resource
   afterward.
3. A borrow does not transfer or duplicate the release obligation.
4. The owner cannot use the resource directly, transfer it, or release it
   while a borrow is live. Further use occurs through compatible borrows.
5. Mutable and read-only borrowing follow the future C# rule: at one time the
   resource has either one mutable borrow or any number of read-only borrows.
6. Every terminal path transfers or releases every owned obligation.
7. Cancellation and failure are terminal paths for ownership accounting.
   They do not imply that release occurred.
8. A release happens at most once. Owner-specific idempotent cleanup may make
   repeated calls operationally harmless, but it does not create multiple
   valid release obligations.
9. A reference or receipt cannot keep the resource alive, authorize access, or
   become the place where release responsibility is hidden.
10. An aggregate owner may own child leases, but the ownership chain and every
    transfer or release remain metadata-visible.
11. Invoking asynchronous release consumes ordinary use of the lease and
    creates one settlement-observation obligation bound to the returned
    awaitable. That obligation is awaited or explicitly transferred, never
    copied or dropped.
12. Only observed successful completion establishes `ended`.
13. Faulted or canceled settlement has the issuer-declared post-failure state.
    Without that declaration, the state is indeterminate and cannot authorize
    reuse, another release, or a clean analysis result.

Current C# permits aliases that violate these rules. Until compiler ownership
exists, API shape, focused tests, and Analysis jointly enforce the supported
subset. An unsupported aliasing or dispatch shape produces incomplete
analysis, not proof of correctness.

## Resource issuers, services, and Houses

Only the focused resource issuer issues its lease. The issuer may be exposed
as a service, pool, store, session, or another resource-specific API. The
lease is named for the resource or capability it owns:

```text
PackageSourceLease
ArtifactContentLease
LibraryLease
```

The concrete names above are illustrative, not decisions for those owners.
The binding naming rules are:

- no lease name contains `House`;
- no lease is named for the consumer that happens to hold it;
- no House mints a lease for a resource defined by an adjacent architectural
  owner; and
- changing the consumer does not rename or change the lease semantics.

A House remains governed by its own composition design. The cross-cutting rule
is holder-neutral: whenever a House owns a release obligation, every terminal
outcome explicitly transfers that obligation to the selected result or
releases it. Scenario authorization, acquisition order, product policy,
selection, and settlement sequencing remain House-owned.

This separates scenario settlement from resource lifetime. A House may return
an aggregate produced by a Library or Workspace architectural owner, but it
does not hide service leases inside a House-named capability.

`PackageHouseSourceLease` is a current migration subject because its name and
issuer combine House settlement with package-source authority. This pattern
does not choose whether the package owner should expose a
`PackageSourceLease`, a settlement session, or another focused contract. The
Package owner decides that in its adoption step.

## References, leases, and explicit borrowing

A reusable reference has stable identity and correspondence but no captured
lease. Access combines the reference with an explicit live lease and produces
one of two lifetime shapes:

- a scoped borrow that cannot escape the call; or
- an explicitly owned child resource whose own release keeps the required
  parent lifetime live.

The target separation is:

```text
ArtifactContentReference
  = artifact identity + registration/correspondence

ArtifactContentLease
  = live access authority + release obligation

borrow(reference, lease)
  = scoped read-only or mutable view

open(reference, lease)
  = owned reader or stream + child release obligation
```

The names are illustrative. The separation is normative.

`ArtifactContentReference` currently captures an `ArtifactQueryLease` and
revalidates that hidden lease for registration, role, digest, and stream-open
operations. That implementation is valuable migration evidence, but the target
pattern does not treat a heap-escapable reference with hidden caller-owned
authority as a resource-free reference.

This distinction answers the Library handoff question:

- an artifact reference is sufficient for identity and retained-content
  correspondence;
- a transferred issuer-provided lease is required for continued access;
- the Library owner associates assembly and companion-content roles and
  transfers their lifetime as its separately designed aggregate; and
- SourceHouse owns an operation-scoped Library lease across asynchronous work,
  requests scoped content borrows within synchronous segments, and does not
  receive a consumer-specific "source-ready" wrapper.

The Library owner, not this pattern, decides whether that aggregate is one
`LibraryLease`, multiple child leases held by a Library owner, or another
equivalent resource shape. A non-materialized SourceHouse result that requires
continued content access receives an explicit transferred ownership-bearing
aggregate; its receipt remains resource-free.

`ArtifactContentReference.OpenRead()` currently returns a heap-escapable
`Stream`. That stream is an owned child resource, not a scoped borrow. The
Artifact adoption must declare the stream-producing acquisition, its parent
retention, and its synchronous or asynchronous release effects.

## Current C# lowering

### Owned values

A current-C# owned value:

- implements `IDisposable` when terminal release is synchronous;
- implements `IAsyncDisposable` when required settlement is asynchronous;
- keeps child ownership in explicit fields;
- explicitly releases or transfers every child on success, failure, and
  cancellation; and
- never relies on a finalizer or silent fallback to satisfy the normal
  lifecycle.

`using` and `await using` express lexical ownership where the value remains
local. Explicit transfer methods are required when ownership moves into a
longer-lived aggregate.

### Aggregate ownership

Current C# aggregate ownership uses an explicit protocol:

1. A successful `Adopt`-equivalent operation consumes one child obligation and
   records it in the aggregate.
2. A failed adoption leaves ownership with the caller.
3. A successful detach or transfer operation removes the child from the
   aggregate and returns ownership to the recipient.
4. Aggregate release synchronously or asynchronously settles every child still
   owned by the aggregate.
5. The resource issuer declares any correctness-sensitive child release order.
   In the absence of such a declaration, collection order is not semantic
   evidence.
6. Duplicate adoption, release after transfer, and unowned removal are
   lifecycle violations even when current runtime cleanup is idempotent.

The current implementation may use fields, lists, sets, registration methods,
and explicit disposal loops. The simplification is one declared ownership
protocol across those forms, not a requirement to replace all dynamic
collections with one helper. Analysis returns incomplete when it cannot model a
dynamic adoption, transfer, iteration, or release path.

Future transitive `Drop` can remove manual cleanup for compiler-supported owned
fields. It does not automatically solve dynamically registered child
resources; those remain an explicit aggregate resource unless a future
resource-aware collection supplies the same contract.

### Receiver and parameter effects

Ownership behavior belongs to the receiver or parameter, not to whether the
method uses instance or static syntax.

For an adopted resource:

- an ordinary instance receiver is a mutable borrow;
- a recognized read-only receiver is a read-only borrow;
- an explicitly consuming receiver transfers ownership into the method;
- an ordinary resource-valued parameter consumes ownership; and
- a static or extension helper that must not consume uses an explicitly
  declared borrow or a scoped view such as `ReadOnlySpan<T>`.

This permits ordinary `.Count`, `.Contains(...)`, and mutation operations to
borrow while a destructive conversion such as `MoveToImmutable()` may consume
the receiver and invalidate the caller's ownership.

The pinned C# proposal does not currently define a consuming instance
receiver. It makes every resource instance receiver a borrow and expresses a
consuming conversion as a static method with an ordinary resource parameter.
An extension method with `this R` has the same static consuming-parameter
semantics while retaining dot-call syntax.

The consuming-receiver effect is a deliberate dotnet-inspect extension,
informed by follow-up ownership-design discussion about inverting
`BorrowedReceiver` for resource types. Its proposal-compatible lowering is a
static or extension method with an ordinary resource parameter. In current C#,
that consumption is still Analysis-declared rather than compiler-enforced. A
configured method attribute may describe the intended instance spelling to
Analysis, but it cannot prevent the source program from using the moved value.
If a future C# design adds compiler-enforced consuming receivers, its metadata
lowers to the same normalized effect; this specification does not assume that
outcome.

Current C# likewise has no compiler-enforced heap-class `Borrow<T>`.
Analysis-recognized declarations can detect supported violations but do not
provide source-language prevention.

### Synchronous borrows

Owner-retained bytes and similar data use the existing artifact pattern:

- a `readonly ref struct` for read-only state;
- a `ref struct` for mutable state when required;
- `ReadOnlySpan<T>` or `Span<T>` for the actual buffer;
- a `scoped` callback parameter; and
- owner validation before the callback begins.

The borrow ends when the callback returns. The callback cannot retain the view
on the heap or carry it across `await`.

`Memory<T>` is not an ownership or borrowing contract. It can escape
independently of the value responsible for retaining or releasing its backing
resource and therefore does not establish owner-bounded lifetime.

### Work that crosses an async boundary

A span or ref-struct borrow does not cross an async suspension. Work requiring
live authority across `await` owns an operation-scoped lease for that duration.
Within each synchronous portion of the operation, the lease holder may request
scoped borrows from the issuer.

This is ownership, not a heap-escapable borrow approximation:

```text
acquire operation lease
  -> await operation work
     -> issue scoped byte borrows as needed
  -> await lease settlement when required
```

The operation lease remains explicit so cancellation, quiescence, and cleanup
failures cannot disappear behind a captured reference.

## Enforcement ladder and current overhang

The pattern separates **prevention**, **runtime rejection**, **detection**, and
**evidence**. An attribute is a declaration for Analysis; it is not compiler
enforcement and does not make the annotated code safe by itself.

| Contract property | Available now | Planned before compiler ownership | Future compiler role | Current overhang |
| --- | --- | --- | --- | --- |
| A scoped span or ref-struct borrow does not escape | C# ref-safety, `scoped`, and the type system | Preserve these shapes as complete borrow evidence. | Generalize owner-derived lifetimes to `Borrow<T>` and `ReadOnlyBorrow<T>`. | Heap-class borrows and async-spanning use need another shape. |
| A lexical owner releases on normal and exceptional exits | `using` or `await using` lowering when the author uses it | Generalized Analysis detects supported missing terminal release. | Invoke `Drop` automatically for synchronous resources. | Current C# does not require an owning value to use either construct. |
| An issuer rejects access after disposal, revocation, or generation change | Resource-specific runtime validation | Analysis associates supported invalid use with the declared lifecycle. | Preserve owner validation; compiler ownership addresses earlier misuse. | Runtime rejection occurs after an invalid operation was attempted and does not prove unique ownership. |
| Ownership moves rather than copies | API-specific ArrayPool transfer evidence only | Generalized Analysis detects declared use after transfer and unsupported aliases remain incomplete. | Make resource values move-only and invalidate the prior owner. | Current class references can be freely aliased. |
| A borrowed or consuming receiver has the declared effect | Scoped-view types; no general receiver analysis | Normalize configured and external receiver effects and detect supported misuse. | Enforce borrow effects if adopted by the language; consuming receivers remain a proposed extension. | An attribute or manifest cannot invalidate the source variable. |
| Aggregate cleanup is transitive | Manual child retention and release | Declare adoption, transfer, release, and correctness-sensitive order; analyze the supported aggregate flow. | Invoke transitive `Drop` for compiler-supported owned fields. | Dynamic collections and unsupported cleanup loops remain manual and incomplete. |
| Asynchronous settlement is observed | `await using` when used correctly | Detect supported unobserved or incorrectly substituted settlement. | No correspondence is claimed until the language defines asynchronous ownership. | `DisposeAsync()` can otherwise be called and its awaitable dropped, copied, or forwarded beyond supported analysis. |
| Every supported exceptional exit transfers or releases ownership | Explicit `using`/`finally` lowering; ArrayPool-specific Resource Triage | Generalized Analysis evaluates declared resources over supported control flow. | Automatic `Drop` covers synchronous owning scopes. | Unsupported alias, dispatch, state-machine, unsafe, or interop flow remains incomplete. |

At the current repository head, only compiler ref safety, explicit
`using`/`await using`, resource-specific runtime checks, and API-specific
Resource Triage are implemented. The generalized declaration-driven Analysis
column is the planned result of tracker steps 2 through 5, not a current
guarantee.

The current enforcement plan is therefore layered:

1. use current compiler-enforced ref safety for synchronous borrows;
2. use explicit `IDisposable` or `IAsyncDisposable` ownership and
   resource-specific runtime validation;
3. declare ownership effects in metadata or external models;
4. generalize Resource Lifecycle Analysis to detect supported leaks, invalid
   transfers, borrow violations, and unobserved settlement;
5. preserve incomplete analysis instead of issuing false confidence; and
6. adopt future compiler ownership metadata as another declaration source,
   replacing current conventions where it provides stronger prevention.

Every method that borrows a resource does **not** need cleanup machinery. An
owning scope that spans potentially throwing work must transfer or release its
obligation on every exit. `using` and future compiler `Drop` can express that
without handwritten `try` statements, although their IL lowering may contain
one or more exception-handling regions. Existing ArrayPool Resource Triage and
the planned generalized Analysis reason over lowered control flow; neither
searches for source-level `try` syntax.

The residual risk is explicit:

- after generalized declaration-driven Analysis lands, a complete result means
  the declared lifecycle was satisfied within that analyzer's supported flow
  set;
- an incomplete result means the analyzer could not establish the lifecycle;
- no attribute, passing test, or idempotent `Dispose` upgrades unsupported
  flow into compiler-enforced ownership; and
- only future language support can prevent every otherwise legal source-level
  copy, use after transfer, or receiver escape covered by that language model.

## Synchronous release and asynchronous settlement

The proposed C# `Drop` is synchronous, compiler-invoked, and expected not to
throw. It cannot flush asynchronous work or await dependent quiescence.

Therefore:

- a synchronous `IDisposable` lease may correspond to future `IResource` when
  its complete terminal obligation fits `Drop`;
- an `IAsyncDisposable` lease does not claim that correspondence;
- `Dispose` is not a substitute for required `DisposeAsync`;
- invoking `DisposeAsync` starts settlement and consumes further ordinary use;
- the returned awaitable carries the obligation to observe settlement;
- forwarding or returning that awaitable transfers the observation obligation;
- only observed successful completion establishes ended ownership;
- a dropped, unobserved, faulted, or canceled settlement remains visible and
  follows the issuer-declared post-failure state;
- retirement that merely rejects new work is not complete settlement when
  dependent work must quiesce;
- cleanup failures remain visible through the resource issuer's typed or
  exceptional contract; and
- a future compiler feature must explicitly cover asynchronous resources
  before those owners migrate from `IAsyncDisposable`.

`IArtifactAcquisitionLease` and artifact-session shutdown are current examples
of required awaited cleanup. Their exact quiescence and stream-survival rules
remain Artifact-owned.

## Declarative ownership contract

Analysis consumes one normalized ownership contract independent of how the
contract was declared.

### Declaration sources

The initial architecture supports four source families:

1. configured fully qualified ownership attribute names, initially including
   a resource-type marker and a consuming-receiver method marker;
2. future canonical compiler/runtime ownership metadata, including resource
   interfaces and receiver effects;
3. built-in models for framework APIs such as `ArrayPool<T>`; and
4. external contract manifests for APIs that cannot carry an attribute or
   need effects beyond the marker defaults.

All four lower to the same semantic model before ownership-flow analysis.
Analysis never branches its lifecycle rules by declaration source.

The configured attribute mechanism matches metadata names and assigns each
name one declared role. It does not require the inspected assembly to reference
a dotnet-inspect contracts assembly and it does not require dotnet-inspect to
load inspected code.

The initial attribute vocabulary is deliberately small:

- a resource marker opts a type into the proposal-compatible defaults; and
- a consuming-receiver marker changes one instance receiver from its default
  borrow into ownership transfer.

The consuming-receiver marker is Analysis metadata for a deliberate extension,
not a claim that the pinned C# proposal or current compiler recognizes that
receiver effect.

APIs that need other non-default parameter, factory, release, wrapper, or
borrow effects use a built-in model or external manifest until another
metadata role is separately justified.

### Marker defaults

For a marker-only resource type, normalization uses proposal-compatible
defaults:

- direct construction produces ownership;
- a direct resource return transfers ownership to the caller;
- an ordinary resource parameter consumes ownership;
- an ordinary instance receiver is a mutable borrow;
- a receiver carrying recognized compiler or explicit-model read-only-borrow
  metadata is a read-only borrow;
- a receiver carrying the configured consuming-receiver marker consumes
  ownership;
- current CLR `in`, `ref`, `out`, `ref` return, and `ref readonly` return shapes
  require an explicit built-in, manifest, or future compiler model because the
  ref kind alone does not establish class-target mutability, ownership
  replacement, transfer-on-success, or the owner-derived lifetime;
- `Dispose` is the release when the type implements only `IDisposable`;
- `DisposeAsync` is the required release when the type implements
  `IAsyncDisposable`;
- implementing both disposal interfaces is ambiguous until an explicit model
  declares whether either terminal is sufficient or async settlement is
  required; and
- a span or ref-like value returned from a borrowed receiver inherits that
  receiver's borrow lifetime.

If the marker defaults cannot identify a terminal release, identify an
acquisition, or resolve an ambiguous effect, the declaration is incomplete.
Analysis does not treat the type as clean.

The marker name is configuration, not trust. An analyzed assembly can make an
incorrect claim; the declaration must still be structurally valid and its IL
must still satisfy the normalized lifecycle.

### Normalized effects

The declaration layer supplies Analysis with:

- exact resource type identity;
- acquisition operations;
- ownership-bearing return and field shapes;
- mutable-borrowed, read-only-borrowed, and consuming receivers;
- consuming parameters and transfer operations;
- borrowed parameters;
- borrowed or owner-derived returns;
- synchronous and asynchronous release operations;
- wrapper ownership propagation;
- aggregate adoption, detachment, transfer, release, and
  correctness-sensitive order; and
- declaration failures or unsupported effects.

Analysis owns how those effects are represented internally and how far it can
prove them.

No declaration source infers ownership from type names such as `Lease`,
`Owner`, or `Resource`, or from method names such as `Rent`, `Return`,
`Acquire`, `Release`, `Open`, or `Close`.

## Detectability obligations

A resource contract is adoptable only when its supported lifecycle is visible
in metadata and IL. The first Analysis adoption must be able to classify:

- acquired ownership not released or transferred on every supported terminal
  path;
- release more than once;
- use, borrow, release, or transfer after release;
- use, borrow, release, or transfer after ownership moved;
- an owning copy or alias where the declaration requires uniqueness;
- a child obligation lost, duplicated, or released incorrectly during
  aggregate adoption or transfer;
- a borrow escaping its owner-supported lifetime;
- owner transfer or release while a borrow remains live;
- synchronous release substituted for required asynchronous settlement;
- asynchronous settlement invoked but not successfully observed;
- malformed, contradictory, unresolved, or unsupported ownership evidence.

The detector need not claim support for every CLR aliasing, dispatch, async
state-machine, reflection, unsafe-code, or interop shape in its first slice.
It must state its supported set and return an incomplete or failed outcome for
an affected resource flow outside that set. Unsupported evidence never becomes
a complete empty Finding census.

The declaration is policy-free lifecycle evidence. Resource Triage or another
Analysis-owned consumer may assess actionability, trust, impact, or confidence
without changing the underlying occurrence.

## Pathological cases

### Exception path loses the only owner

A service returns a lease, then a later operation throws before the House
transfers the lease into its selected result. The House must release the lease
on that path. Analysis reports the supported missing release; it does not
accept a successful-path `Dispose` as proof for the exception path. Source may
express the lifetime through `using`, `await using`, or explicit `finally`;
Analysis reasons over the lowered control flow rather than requiring
handwritten `try` syntax.

### A reference captures a caller-owned lease

A reusable reference closes over a lease and can be copied independently of
the variable responsible for disposal. The target contract splits the
reference from the lease. Callers pass the explicit lease or ask its owner for
a scoped borrow, making the ownership obligation visible.

### Borrowed bytes escape

A callback receives a scoped read-only content view and attempts to store it or
carry it across `await`. Current C# rejects the ref-struct escape. A future
class borrow carries the same owner-derived lifetime; Analysis reports an
escape when compiler enforcement is absent and the effect is in its supported
set.

### Ownership moves into Workspace

Package or platform realization transfers a Library-owned aggregate into
Workspace. The producer cannot continue borrowing or release the transferred
lease. Workspace becomes responsible for later settlement, and the durable
House receipt retains only evidence of the transfer.

### Retirement is not asynchronous settlement

An owner rejects new work while existing operations or streams remain active.
If the resource contract requires quiescence, synchronous retirement does not
satisfy release. The current owner awaits settlement and reports cleanup
failures. Future synchronous `Drop` is not claimed to cover this lifecycle.

## Non-claims

This pattern does not claim:

- that the repository currently has one coherent lease implementation;
- that every existing `*Lease` type is ownership-bearing or correctly named;
- that `ArtifactQueryLease` and `PackageHouseSourceLease` have equivalent
  semantics;
- that an artifact reference alone retains content;
- that one Library lease shape is already selected;
- that all borrowing can be represented by spans;
- that `Memory<T>` proves owner retention;
- that current-C# adoption eliminates every `using`, registration collection,
  disposal loop, or exception-handling region;
- that current C# prevents copies, use after transfer, or double release;
- that a marker attribute proves its own correctness;
- that an Analysis result proves behavior outside its declared supported flow
  set;
- that synchronous `Drop` can replace awaited cleanup; or
- that generalized ownership analysis is already implemented.

The pattern PR makes no repository-wide absence claim about hidden leases,
misnamed leases, or lifetime violations. Those are measured in the dogfood and
owner-adoption steps.

## Production adoption

[#6544](https://github.com/richlander/dotnet-inspect/issues/6544) is the
end-to-end tracker. Its current total is 16 steps:

1. lock this focused ownership, borrowing, and declaration pattern;
2. define the machine-readable resource, consuming-receiver, and external
   contract model;
3. normalize ArrayPool, declared-resource, and future compiler-issued
   ownership metadata into one Analysis input contract;
4. generalize Resource Lifecycle Analysis while preserving explicit
   incompleteness;
5. dogfood the contract against current repository leases and retain useful
   corpus sensors;
6. expose generalized Resource Triage through the CLI;
7. expose the same typed Resource Triage contract through Inspect Web
   Browser/Wasm;
8. adopt the pattern in artifact acquisition, access, and scoped content
   borrowing;
9. define the shared Library ownership and borrowing contract used by
   PackageHouse, PlatformHouse, direct-library adapters, Workspace, and
   Library consumers;
10. adopt the pattern in the package-source owner and issue a resource-named
    package-source lease;
11. adopt that package-source lease in PackageHouse and retire
    `PackageHouseSourceLease`;
12. adopt the Library ownership contract in PackageHouse;
13. adopt the Library ownership contract in PlatformHouse;
14. adopt the Library ownership contract in Workspace and its Workspace-owned
    direct-library adapter;
15. adopt Library and companion-content ownership and borrowing in
    SourceHouse; and
16. adopt Library and companion-content ownership and borrowing in
    DocumentationHouse.

Each implementation step changes one owner. Step 9 replaces the
consumer-specific "source-ready library representation" direction in
SourceHouse step 2; the SourceHouse tracker and owner document are corrected
in that focused adoption effort rather than normatively changed here.

The dogfood step may discover another independently owned public lease
contract. Adding its focused adoption requires an explicit tracker and count
update; it is not folded into a catch-all migration step.

A change to the count must preserve:

- one separately reviewed Analysis first adoption;
- CLI and Browser/Wasm product paths;
- artifact and Library owner adoption;
- SourceHouse and DocumentationHouse adoption; and
- retirement of `PackageHouseSourceLease` and conflicting hidden-lease shapes.

## Evidence plan

This specification is design-only. Its behavioral properties remain
**unverified** until the named adoption steps add Release gates.

The declaration and Analysis steps must gate:

- exact configured resource and consuming-receiver metadata-name matching
  without inspected-assembly loading;
- equivalent normalization from attributes, built-in models, external
  manifests, and future compiler metadata;
- every supported acquisition, transfer, mutable and read-only borrow, child
  resource, consuming receiver, release, and asynchronous settlement effect;
- aggregate adoption failure retaining caller ownership, successful adoption
  consuming it, transfer removing it, and declared release ordering;
- leak, double-release, use-after-release, use-after-transfer, and borrow
  lifetime fixtures;
- required async settlement not being accepted as synchronous release;
- unobserved, faulted, and canceled asynchronous settlement;
- malformed and contradictory declarations;
- incomplete decode, resolution, dispatch, alias, body, and control-flow
  evidence remaining visible;
- compatibility with the existing ArrayPool corpus and Finding identities;
- repository dogfood that distinguishes complete violations, complete clean
  lifecycles, and unsupported or incomplete ownership flow; and
- equivalent typed outcomes in CLI and Browser/Wasm consumers.

Each resource-issuer adoption must gate:

- one owner for each release obligation;
- transfer invalidating the prior owner in the supported Analysis model;
- scoped borrows not escaping;
- release on success, failure, and cancellation;
- required asynchronous quiescence being awaited;
- references and receipts carrying no hidden lease; and
- issuer-specific stream or active-operation survival semantics.

The repository dogfood step records findings and analysis limits; it does not
turn the target naming or ownership rules into a source-policing absence gate.
