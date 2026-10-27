# Finding Diff Any

## Status and owner

This document owns **Finding Diff Any**: one pass over a Library image pair that
decides, for every Type, whether its API and its implementation differ, with
the same equality the complete diffs use, and the narrower
[Type and Member levels](#levels) beneath it. It is tracked by
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716) and
supersedes the earlier body-free, presence-only design proposed in #9717.

The producer is `FindingDiffAny.Compare` in `ILInspector.Metadata`. It depends
only on System.Reflection.Metadata, so it runs unchanged under NativeAOT and
Browser/Wasm.

Since [Finding census](finding-census.md#adoption) step 4, this document owns
**Exists at Library, Type, and Member scope** over the census: the axes, the
pair partition, the API axis surface, the folds of a Type's rows, and their
gates, at the [three levels](#levels). Finding census owns the reading of one
row on one kind beneath them, and the agreement of that reading with
Exhaustive. Exists at Member scope is a Member's row of the [Type
level](#type), its API and Body states, which adoption step 7 will land; the
Member level's complete diff stays with its owners.

Two provisions answer the rows this document folds. Each is bound to the
cursor's equality by Finding census's [provision
rule](finding-census.md#provisions) and kept for its time alone:

- **The paired Body walk** decides a paired method's IL. Finding census step 2b
  ran lockstep over two IL cursors through the walk's [body reader
  seam](#axes), at Library and Type scope, and kept the walk ([what remains of
  the gap](finding-census.md#what-remains-of-the-gap)).
- **The census slot comparison** decides one unit's census facts on each axis,
  with the API axis surface deciding a census that differs only in uncertain
  facts. Finding census step 3 reads the same slots through the declaration
  cursor and keeps the comparison ([declaration
  cursor](finding-census.md#adoption)).

Either provision is deleted only when lockstep over the cursors meets Diff
capabilities' [latency bar](diff-capabilities.md#latency-bar) and matches or
beats the provision on every number Finding census's [Cost
gate](finding-census.md#gates) reports; both must hold. The change that deletes
the walk owns the IL kind's Allocation gate and the Browser/Wasm measurement,
and the change that deletes the slot comparison owns the declaration kind's
allocation debt that step 3 records.

The name was Fast Diff until 2026-10-21
([#10430](https://github.com/richlander/dotnet-inspect/issues/10430)). "Fast"
named a cost, and the design says the cost is not its identity; `Any` names the
question it answers, as `Enumerable.Any` and `Task.WhenAny` do: whether any
Finding differs, decided at the first one. Library Compare calls it **Any
Diff**. The Exists contract of
[Diff capabilities](diff-capabilities.md#the-exists-contract) keeps its name;
`Any` is the operation that implements it.

## Why

Library Compare needs to show which Types changed so a user can choose where
to look. A complete Library diff answers far more than that and costs far
more. Most Types in a version pair are unchanged, and proving that is the
dominant cost, so Finding Diff Any makes the unchanged case cheap and stops each
changed axis at its first difference.

A second definition of "changed" drifts from the complete diffs and points to
views that show nothing. Finding Diff Any therefore has no equality of its own:
each axis answers with its complete diff's equality, read as **Exists**, and
only the cost of reaching the answer differs. The reading of one row to its
first difference, and its agreement with the exhaustive reading the complete
diffs make, is owned by [Finding census](finding-census.md); this document owns
the axes, the partition, and the folds above the row.

## Contract

> **Claim.** For one Library image pair, Finding Diff Any returns, for every
> Type, an **API** state and a **Body** state, each `Unchanged`, `Changed`, or
> `Indeterminate`; when a caller asks for the API axis alone, every Body state
> is `NotCompared`. The API state is `Changed` exactly when the complete Public
> API comparison of the [API axis population](diff-capabilities.md#axes) reports
> a change to the Type or one of its Members. The Body state is `Changed`
> exactly when the Type is present on one side only, a method of it with a body
> is present on one side only, a paired method has a body on one side only, a
> paired body is not `Exact` under [canonical IL
> comparison](il-diff-canonicalization.md#comparison-outcomes) with the [Body
> policy](#axes), or a [declaration fact](#axes) on the Body axis differs. A row
> or body that cannot be decoded or resolved makes the unsettled axes of its
> Type `Indeterminate`, never `Unchanged`.

The two axes are independent searches. Each stops at its own first
difference, and a host derives **Any** as either axis `Changed`. A Type that
changed its API still walks its bodies until the first body difference, so the
worst case for a changed Type is the cost of an unchanged one.

Types are named by their `ApiType.FullName` spelling: `.` between nested names
and the metadata backtick arity.

### Axes

The **API** axis is decided on the public, protected, and protected internal
Members of a public Type. It compares the Public API facts of a Type and its
members:

- Type flags (except `beforefieldinit`), base Type, interfaces, generic
  parameters and constraints, custom attributes, layout, and the effective
  nullable context, which a nested Type inherits from its declaring Type;
- public, protected, and protected internal member signatures and flags,
  parameter names, flags, and defaults, constants, and custom attributes,
  including compiler-emitted attributes such as nullable annotations that
  shape rendered signatures; and
- explicit interface implementations.

That is the API axis population of [Diff
capabilities](diff-capabilities.md#axes): `private protected` Members belong to
Body, and hidden Types and Members, such as ones marked
`EditorBrowsable(Never)`, are compared like any other; their status is the API
status analysis's, not Finding Diff Any's. A Type is public when its own row and
every enclosing Type's row is public, nested public, protected, or protected
internal, matching the API axis surface (`ApiSurfaceExtractionScope.ApiAxis`); a
public nested Type of a non-public Type is not. Non-public Types have no API
facts.

These facts are a census, and the census decides `Unchanged` alone: equal
census facts mean the complete comparison reports no change. Each fact kind is
either **presented** or **uncertain**:

- A **presented** fact differs only when the complete comparison reports a
  change, so its first difference decides `Changed` and stops the axis.
  Examples are a Member, parameter, or Type present on one side only, a
  parameter name, the virtual shape of a method, interfaces, generic
  parameters and constraints, and explicit implementations.
- An **uncertain** fact can differ while the complete comparison reports
  nothing, such as a raw attribute blob, nullable context, or a flag that no
  view presents. A Type whose census differs only in uncertain facts is
  decided by building the API axis surface of that Type alone on each side
  and comparing it as the complete comparison does.

A fact is presented only where the API axis surface admits its Type and
Member. A hidden Type or Member is admitted, so a difference confined to it is
decided like any other API fact, not made uncertain. A fact kind is
presented only when a gate shows it (see [Gates](#gates)); every other kind
is uncertain, which costs time, never correctness.

The **Body** axis is decided at every accessibility. It compares the IL of
every method, public or not, with the equality of canonical IL comparison
under one named policy, the **Body
policy**: `IlBodyDiffNormalization.NormalizeAssemblyScope`
([#10205](https://github.com/richlander/dotnet-inspect/issues/10205)), so
every Type and member operand compares by name and signature without its
defining or referenced assembly. Dependency versions are not an input to Body
change: a Library rebuilt only because a dependency's version moved is Body
`Unchanged`, and for an app whose two dependencies each changed one method,
the Body diffs of those dependencies find those two methods and the app's
finds nothing. Package and Library versions are compared as candidates in
their own right, not as Type changes.

Beside IL, the Body axis compares **declaration facts**: the row, signature,
flags, constants, import, generic parameters, and attributes of every Member
the API axis surface does not admit, at every accessibility, and the
declaration of a Type the surface admits on neither side. A public Type's
private, internal, and `private protected` Members are compared this way, so
an added private field is a Body change, as is an attribute added to an
internal method. That is the Body axis of
[Diff capabilities](diff-capabilities.md#axes): every subject at every
accessibility. A Body decided on public Members alone would report a public
method `Unchanged` while the private method it calls changed, which would be
false; the Body axis compares every body and every declaration instead, and a
consumer that wants a narrower view filters what it is given. Declaration
facts compare raw: a kind the complete comparison does not present is an
uncertain Body fact, and the uncertain-kind census of Diff capabilities
settles each kind in its adoption step 2. `beforefieldinit` is a Body fact;
the implementation flags and compiler-emitted attributes of a Member the
surface admits are uncertain API facts until that step moves them to Body.

Under that policy, `ldc.i4` forms and short branches fold to one family, branch
and switch targets compare by aligned instruction, metadata tokens compare by
symbolic name, and user strings by value. Other opcodes keep their encoding and
slot operands, so `ldloc.s` and `ldloc`, or `ldloc.0` and `ldloc.s 0`, differ,
as the IL view shows them. Exception clauses compare as canonical comparison's
clause atoms compare: clause by clause in declaration order, by kind, by catch
Type as a symbolic name, and by each extent boundary as the instruction it
starts, so a changed catch Type or a moved `try` boundary is a Body difference
and a re-encoded branch before the `try` is not. As in canonical comparison,
body size, the local signature, stack size, and `init locals` are not compared,
and stand-alone signatures compare as canonical comparison compares them. The
walk is allocation-free in lockstep and stops at the first difference; a
retargeted branch or a moved clause extent is found once the body's instruction
map is complete. It reads IL through the shared IL byte layer, `ILReader` and
the opcode tables in `ILInspector.MetadataPrimitives` ([Instruction
substrate](instruction-substrate.md#layering)), building no instruction, so the
walk and the decoder behind the census cursor read one opcode table and differ
only in what each builds above it. The walk is a provision behind one seam: a
caller may supply a `FindingDiffAnyBodyReader`, which receives the two images
and the two method rows the comparison paired and answers as Exists answers,
and the comparison then folds its answer as it folds the walk's, an undecided
pair as a body it could not read. Finding census step 2b ran lockstep over two
IL cursors through that seam, beside the walk, and kept the walk ([Finding
census](finding-census.md#what-remains-of-the-gap)).

A paired method whose body is present on one side only, such as an `abstract`
method made `virtual` or an interface member given a default implementation,
is a Body difference, as canonical comparison's endpoint topology reports it
(`NoApplicableInput`). A body that cannot be decoded or an operand that cannot
be resolved, before the walk decides a difference, is `Indeterminate`, never a
difference or equality.

An opcode that ECMA-335 does not define cannot be stepped over safely, so it
makes the Body axis `Indeterminate` rather than comparing as equal. The walk
stops at the first difference it decides, so content after that point is never
read: a body whose walk decides a difference before it reaches an undefined
opcode is `Changed`, and its view is the complete diff's visible decode
failure, as [Diff capabilities](diff-capabilities.md#the-exists-contract)
states. A branch or switch target is decided only once the body's instruction
map is complete, so a body whose only differences before an undefined opcode
are targets is `Indeterminate`.

Neither axis's facts are configurable. A caller can ask for the API axis alone;
every Body state is then `NotCompared` and no IL is read. A method with a body
present on one side only, public or not, is a Body difference without reading
its IL; a public one is an API difference too. The presence of a method with a
body is a change in its own right, independent of a body to compare with, and
is the basis a later **Moved** state refines. A Type public
on either side partitions its facts as public on both, so a visibility change is
an API fact only.

A Type present in one image only is reported whole by a presence rule: its API
is `Changed` when the API axis surface admits it and `Unchanged` otherwise,
and its Body is `Changed`, whether or not it declares a body, or
`NotCompared` when only the API axis is asked for. A malformed
one-sided Type is `Indeterminate` on each axis compared.

### Steps

`FindingDiffAnyComparison` advances one comparison in bounded steps for a
single-threaded host. Each step takes readers over the same two images, makes
progress on at least one Type row or compared Type, and stops at the first such
boundary after its budget elapses. The retained state holds handles and strings,
never a reader, so a host re-enters its own image callbacks for each step; a
reader over a different image is rejected by module version ID. Stepping reaches
exactly the result of one uninterrupted `FindingDiffAny.Compare`. Cancellation
is observed at the same boundaries.

### Identity

Metadata tokens, in IL operands and in signatures, compare by symbolic name.
A Type-level row's name is resolved once per side, memoized for the
comparison, and interned in the one table both sides share, so a member
spelling, fact, or composite names a Type definition, reference, or primitive
by the `Ref` of its shared id rather than by its bytes, while a Type
specification's composite embeds as the same `Key` part an inline composite
does ([Symbol Key interning](symbol-key.md#interning)); a member row's is
resolved once per unit that refers to it, and a pair of operand rows once
proven equal is remembered for the comparison, so the comparison retains the
pairing rather than member spellings. They never
compare by token number or
by defining or referenced assembly identity, as the [Body policy](#axes)
states, so a renumbered or retargeted reference, or a dependency's version
change, in an operand or signature is not a difference. Custom attribute and constant
values are compared as raw blob bytes (see [Observability](#observability)).

Symbolic names are injective by construction, as containment is for
`InertString`. A key or fact is a byte string that only its builder can make,
and every part the builder writes is self-delimiting: a one-byte marker, a
tagged integer, or a tagged and length-prefixed name, nested key, or blob. Two
keys are therefore equal exactly when they were built from the same parts, and
no name is scanned or escaped. A top-level Type named `A/B`, a Type `B` nested
in `A`, a Type `A.B` in namespace `N`, and a Type `B` in namespace `N.A` never
share a key, and neither do member names that contain any punctuation. A name is
the metadata's own UTF-8, copied from the `#Strings` heap without decoding or
Windows Runtime projection, so member names whose bytes differ only in
ill-formed sequences, which both decode to U+FFFD, stay distinct. A Type whose
namespace or names are not well-formed UTF-8 has no decodable name to identify
it by, so, like a malformed row, it is `Indeterminate` and identified by its row
token. Each key shape has its own marker: a primitive Type and a global Type
named `Int32` stay distinct, a signature's return Type is marked apart from its
parameters so `delegate*<void*>` and `delegate*<void>*` are distinct, and a
field-like event and its backing field, which share a name and Type, never share
a key. A method signature key keeps its full header and marks a vararg sentinel,
so call sites of two vararg overloads that pass the same argument Types stay
distinct. A Type reference and a Type definition of one name share a key, so
they compare by name rather than by assembly. Rows that still spell one key are
never told apart by guessing. Two Type rows of one name, which valid metadata
does not allow, and two method rows of one Type and its generated code, are
`Indeterminate` rather than one replacing the other, and so is any fact that
refers to one of those Type rows or to a compiler-controlled (`PrivateScope`)
method or field, which ECMA-335 lets share a name and signature. Other member
rows of one Type that share a name and signature are invalid metadata outside
Roslyn fidelity. A reference to them compares by the shared key, a contained
wrong answer rather than a decoded one.

Compiler-generated nested Types (state machines, closures, local-function
holders, and Types nested beneath them) belong to their nearest declared
Type. A change confined to an async method, lambda, iterator, or local
function therefore changes the Body of the Type that declares it. Types
beneath a top-level compiler-generated Type, such as
`<PrivateImplementationDetails>`, are not reported; a body that references
their content-named members still compares those names.

### Observability

Finding Diff Any adopts [Diff observability](diff-observability.md). The API
axis targets the semantic level of the Public API diff, and the Body axis
targets the symbolic IL level of the body diff for IL and the declaration view's
level for declaration facts, so `Changed` should point to something a user can
see. IL operand and signature keys never compare token numbers or assembly
identity, so a renumbered token or a dependency version change is not a Body
change. The IL view of Member Body Diff requests the Body policy
(`IlBodyDiffPolicy.Body`), so a Body cue and the view it opens agree, and a
dependency version change shows no row there either. Implementation Diff
evidence and the CLI `match` command's body comparison keep comparing with no
normalization.

Because each axis reads its complete diff's equality, neither axis
over-reports against that diff. The divergences the earlier census reported
are settled as follows:

- **Encoded IL.** Short and long branch forms, `ldc.i4` forms, branch
  offsets, clause offsets, body size, and stack size are no longer compared,
  so a re-encoded branch is `Unchanged`, as canonical comparison treats it.
  Local and argument macro forms and slots remain compared, as the IL view
  shows them. A changed catch Type or clause extent is an exception clause
  row of canonical comparison
  ([#9942](https://github.com/richlander/dotnet-inspect/issues/9942)), so
  the Body axis reports it `Changed` and the IL view shows the clause.
- **Type and Member flags no view presents, nullable context, and attribute
  blobs.** These are uncertain facts. A Type that differs only in them is
  decided by its Public API surface, so a framework major-version move that
  rewrites only a Type-valued attribute argument is `Unchanged`. Comparing
  those arguments by symbolic name
  ([#9802](https://github.com/richlander/dotnet-inspect/issues/9802)) would
  make them presented and cheaper, not more exact.
- **Declaration and implementation facts.** The declaration facts of Members
  the API axis surface does not admit, the declaration of a Type it admits on
  neither side, and `beforefieldinit` are Body facts. Canonical body comparison does
  not show them, so a Body cue decided by one of them alone opens a view with
  no IL change today. That is a known divergence under rule 4 of Diff
  observability until the [declaration view](declaration-view.md) presents
  them ([#10347](https://github.com/richlander/dotnet-inspect/issues/10347));
  the Body axis keeps comparing them, since an `Unchanged` that omitted them
  would be false.

A renumbered compiler-generated name, such as a closure or state machine
renumbered by a new member earlier in the Type, remains a reported Body
difference by design: the IL view shows it.

Canonical IL comparison is read under rule 6 of Diff observability: it
reports three System.Private.CoreLib 10 to 11 `calli` sites whose stand-alone
signatures differ only in token numbers. The Body axis uses canonical equality
there too, so it reports them `Changed`. Decoding stand-alone signatures
symbolically belongs to canonical comparison
([#9801](https://github.com/richlander/dotnet-inspect/issues/9801)), and
Finding Diff Any follows it; the axis never keeps a private exception.

## Gates

`FindingDiffAnyTests` gates:

- exact API and Body states for one targeted change per Type in the
  `ILInspector.Metadata.FindingDiffAny` fixture pair, including lambda-, async-,
  and local-function-only bodies, constraint Types, constants, defaults,
  parameter names, attributes, interfaces, visibility, and added and removed
  Types;
- API exactness, in both directions: every Type's API state equals whether the
  complete Public API comparison of the API axis surface reports a change to it
  or one of its Members, over three fixture pairs. A changed non-enum constant
  is the one known exception: Finding Diff Any reports it, and the complete
  comparison does not yet compare constant values;
- accessibility: a Member or nested Type whose accessibility moves between
  API axis buckets is API `Changed`, and the complete comparison of the API
  axis surface reports `MemberAccessibilityChanged` or
  `TypeAccessibilityChanged`, breaking when external reach narrows (`public`
  to `protected` or `protected internal`), additive when it widens, and
  non-breaking between `protected internal` and `protected`; the ordinary Public surface reports a move across the
  `public` boundary as a removal or an addition, and an accessor's
  accessibility as a signature change; the include-all surface reports every
  move, including one that leaves or enters the API axis, breaking when
  external reach narrows, additive when it widens, and non-breaking when it
  is unchanged (`internal` to `private`), and classifies a change wholly
  outside the API axis as non-breaking;
- presented facts: for every fact kind treated as presented, a fixture case
  in which that fact alone differs, and the complete comparison reports the
  change; a kind without such a case is uncertain;
- hidden subjects: a difference confined to a hidden Member, such as one
  marked `EditorBrowsable(Never)`, is API `Changed`, and the complete
  comparison of the API axis surface reports it with the Member marked
  hidden; the ordinary Public surface omits it. A changed Member of a hidden
  Type is the same: API `Changed`, reported against the API axis surface with
  the Type marked hidden, and omitted from the ordinary Public surface;
- `private protected`: a `private protected` Member added is not API
  `Changed` and is absent from the API axis surface;
- every accessibility on Body: a private field added, a private method
  added, a `private protected` Member added, and a private method whose body
  alone changed under an unchanged public caller each make their Type Body
  `Changed` and API `Unchanged`;
- Body exactness: every method pair's Body verdict equals canonical IL
  comparison's endpoint verdict under the Body policy over the same fixture
  pairs: `Exact` is `Unchanged`, a body on one side only or any other verdict
  is `Changed`, and a decode or resolution failure before the walk decides a difference
  is `Indeterminate`, with fixture cases for an `abstract` method made
  `virtual` and a default interface implementation added;
- dependency versions: a generated image pair whose only difference is the
  version of a non-platform referenced assembly is Body `Unchanged` for every
  Type; and
- canonical encodings: a generated image pair whose bodies differ only in
  `br.s` against `br`, `leave.s` against `leave`, the `ldc.i4` forms, and
  shifted branch and switch offsets is Body `Unchanged`, and one with a
  changed constant, a retargeted branch, a switch target count change, a
  changed call or string, or an inserted instruction is Body `Changed`.

`FindingDiffAnyIdentityTests` gates identity and decoding by outcome, over
generated image pairs compared whole and stepped: a nested Type's body change
beside a top-level Type whose name spells the same path, a namespace and a
dotted Type name that display alike, a caller switching between two callees
whose names joined by punctuation would collide, two method names, and two Type
names, whose stored bytes differ but decode alike, a primitive and a global Type
of one name, a function pointer returning a pointer and a pointer to a function
pointer, an attribute moved from a backing field to its event, a switch between
external vararg overloads, duplicate Type names and duplicate generated Type
names, a caller switching between two compiler-controlled methods or between
methods of two Type rows of one name, undefined one-byte and two-byte opcodes,
and an undefined opcode after a difference the walk decides first, which is
`Changed`. The same class checks that the stored UTF-8 of every
System.Private.CoreLib Type, method, and field name equals the encoding of its
decoded name.

Stepping and the API axis are gated by outcome: a comparison stepped at every
row and Type boundary, with fresh readers per step, equals the whole comparison
over the fixture pair and every malformed image in
`FindingDiffAnyMetadataSafetyTests`; a reader over a different image is
rejected; and API-only states equal the API states of the full comparison with
no body compared.

`eng/measure-finding-diff-any.cs` runs the same checks over a real image pair
and reports NativeAOT timing, API-only and stepped timing, and every
disagreement with the complete comparisons.

The shared-equality design was measured by a spike
([`exp/staged-diff-spike`](https://github.com/richlander/dotnet-inspect/tree/exp/staged-diff-spike)
at `87f53a7cd`) over six real pairs. Medians cover both sides, every Type,
API and bodies, on NativeAOT on one Apple silicon development machine.
**Exists** is this design; **Complete** runs the same equality over every Type
and explains only what it finds changed, against today's complete API and IL
diffs:

| Pair | Earlier census | Exists | Models built | Complete | Today's complete |
| --- | ---: | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 to 13.6.1 | 90 ms | 53 ms | 0 | 61 ms | 2,056 ms |
| System.Text.Json 9.0.0 to 10.0.0 | 12 ms | 10 ms | 0 | 76 ms | 335 ms |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 15 ms | 26 ms | 36 | 72 ms | 507 ms |
| Newtonsoft.Json 11.0.2 to 13.0.4 | 13 ms | 35 ms | 85 | 205 ms | 475 ms |
| System.Private.Xml 10 to 11 | 63 ms | 46 ms | 3 | 150 ms | 2,211 ms |
| System.Private.CoreLib 10 to 11 | 186 ms | 263 ms | 31 | 851 ms | 3,622 ms |

On every pair the API verdict per Type equalled the complete Public API
comparison, every Type decided by a presented fact was changed in it, every
one of 79,489 method pairs' Body verdict equalled canonical IL comparison, and
Complete's output equalled today's complete diffs line for line. Per-Member
API agreement was measured in an earlier round that confirmed every census
difference with the Public API surface. The earlier census over-reported the API of 3, 25, 3, and 24 Types on
the two Newtonsoft.Json pairs, System.Private.Xml, and CoreLib; Exists reports
none of them. The remaining cost is the Public API surface built for Types
whose census differs only in uncertain facts, about 50 ms on CoreLib, and the
spike's fact classification, which allocates where the census need not.

The earlier census is therefore a low-fidelity baseline, not a target: where
it is faster, it is faster by leaving uncertain differences unverified, which
over-reports. The operator accepted the cost of exactness on these pairs. Its
budget, checked by every NativeAOT measurement of these pairs:

- **Exists** is no slower than the earlier census on a pair for which it builds
  no Public API surface;
- on every pair, **Exists** is no slower than its time in the table above,
  which is the ceiling a later slice must meet, and each measurement reports
  the surfaces built and the time spent building them beside the total; and
- the remaining cost of fact classification, about 27 ms on CoreLib, is
  overhead to remove, and each uncertain fact kind that a gate shows
  presented, such as attribute arguments compared by symbolic name
  ([#9802](https://github.com/richlander/dotnet-inspect/issues/9802)), removes
  surface builds rather than exactness.

Canonical IL comparison's failure count for CoreLib (5,454) counts methods or
bodies present on one side only, not bodies it could not decode; neither
comparison failed to decode a body in these pairs. The spike compared with
`NormalizeCurrentAssemblyScope | NormalizePlatformAssemblyScope`. No pair
changed a non-platform reference's identity, so its verdicts equal the Body
policy's on these pairs; the dependency-versions gate covers that case.

### Malformed metadata

Every walk over artifact-derived metadata is bounded. Declaring chains and
Type reference resolution scopes use the shared `MetadataRelationshipTraversal`
primitives, and every signature decode passes `SignatureBlobGuard`, with Type
specifications under `TypeSpecGuard`. A rejected walk or decode makes the
affected Type `Indeterminate` on both axes, and so does a Type row whose name
cannot be read; other Types keep their states, and no metadata shape
terminates the process. `FindingDiffAnyMetadataSafetyTests` gates cyclic and
100,000-deep Type reference chains, nested Type chains, a self-referential
Type specification, a 100,000-deep signature blob, and top-level and nested
Type names outside the string heap in a child process.

## Browser export

`MetadataExports.QueryLibraryFindingDiffAny` (Worker operation
`queryLibraryFindingDiffAny`, request schema 2) runs
`AssemblyContextFindingDiffAnyQuery` over the implementation assemblies of one
exact compile asset in two package versions. It accepts the Library API Diff
endpoint coordinates and `axes`, `ApiAndBody` or `Api`. It returns only Types
with an axis that is `Changed` or `Indeterminate`, plus the compared Type count;
absence means every requested axis is `Unchanged`. Each Type carries
`identifier`, the escaped definition name (`Outer+Inner`) that Library
navigation joins on, and `fullName` for display. Malformed rows, and Types whose
namespace or names are not well-formed UTF-8, have no decodable name; they
receive a row-token identifier and never join a navigation entry.

The Worker is single-threaded, so the operation runs the comparison in
[steps](#steps) with an 8 ms budget each and yields to the Worker event loop
between them. A step ends at the first Type row or compared Type after its
budget, so one large Type can extend it. Foreground operations and cancellation
run between steps; one Finding Diff Any never holds the Worker for its whole
duration.

Firefox, published Release site, warm calls through the production Worker
(interpreted Wasm). Foreground is a cheap Worker call issued every 25 ms while
one `ApiAndBody` comparison runs; it takes 1 ms when the Worker is idle:

| Pair | Types | API and Body | API only | Foreground median (max) |
| --- | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 → 13.6.1 | 1,144 | 1,561 ms | 225 ms | 40 ms (69 ms) |
| System.Text.Json 9.0.0 → 10.0.0 | 320 | 303 ms | 91 ms | 40 ms (54 ms) |
| Newtonsoft.Json 13.0.3 → 13.0.4 | 300 | 348 ms | 104 ms | 39 ms (45 ms) |

Before stepping, one Aspire.Hosting comparison held the Worker for 1,490 ms.

The export returns the result once, when the last step completes. Streaming
each step's reported Types through the operation's event channel is the
deferred option for time to first result, if an explicit Compare list on a
large Library needs it.
The comparison itself accounts for more than 99% of each call; the interpreter
costs roughly 15x relative to NativeAOT.

## Levels

Finding Diff Any answers at three levels, each scoped to what its view shows.
Each level is sound for the level above it, so a cue never points to a view that
shows nothing.

| Level | Question | Answer per row |
| --- | --- | --- |
| Library | Which Types differ? | API exists, Body exists |
| Type | Which Members of this Type differ? | API (signature diff), Body exists |
| Member | How does this Member differ? | Complete API and body diff |

### Library

The [contract](#contract) above: every Type, with each axis stopped at its
first difference.

### Type

> **Claim.** For one Type of one Library image pair, the Type level returns,
> for every Member declared on either side, an **API** state and a **Body**
> state, and one **residual** state per axis for the facts left over once
> every Member has taken its own, such as the Type's own declaration and any
> body no Member owns. The Member states and the residual partition the Type's
> facts, so a Type axis is `Unchanged` at the Library level exactly when that
> axis is `Unchanged` for every Member and for the residual. A Type present in
> one image only has the same rows in its **one-sided form**: every fact it
> has is present on one side, so each owner is `Changed` on each axis where it
> owns facts, and the residual owns the Type's presence.

A change the complete comparison reports to the declaration of a Type the
API axis surface admits, such as an interface removed or a generic constraint
added or removed, is a residual API change, and it is reported the same way
whether or not any Member changed with it. A Type whose only change is in its
declaration has a `Changed` residual and Members all `Unchanged`; a Type whose
declaration and some Members changed has a `Changed` residual and those
Members `Changed` as well. A consumer reads a declaration change from the
residual and never infers it from the Members. Rule 2 owns the exceptions: a
visibility change that hides the Type on one side also marks every public
Member, and a declaration that differs only in uncertain facts is decided by
the Type's API axis surface. A declaration change on a Type the surface admits
on neither side is a residual Body change, since that Type's declaration
facts are Body facts.

A Type present in one image only has no counterpart to compare Members
against, so its one-sided form compares nothing. The present side's Members
are partitioned by the ownership rules below, and every fact an owner holds is
a fact present on one side only, which rule 2 counts as a difference. A Member
is therefore `Changed` on each axis where it owns facts and `Unchanged` where
it owns none, exactly as a Member declared on one side of a paired Type is: a
private field, or a private method with no body, is returned Body `Changed`
and API `Unchanged`, since the surface does not admit it and its declaration
facts are Body facts; a public field is API `Changed` and Body `Unchanged`. The
residual owns the Type's presence as the Library level's
[presence rule](#axes) states it: its Body is `Changed` whether or not any
body exists, or `NotCompared` when only the API axis is asked for, as every
Body state is then, and its API is `Changed` exactly when the API axis surface
admits the Type. So an added internal enum has Member rows all Body `Changed`
and API `Unchanged`, a `Changed` residual Body, and the partition claim holds. A malformed
one-sided Type follows the rule below for a census that cannot be completed:
the residual stays `Changed` on each axis where presence makes it `Changed`,
and every owner not `Changed` on an axis, the residual included, is
`Indeterminate` there. No IL is read, since a body present on one side
only is a Body difference without reading it, at either level, and no metadata
is compared, since nothing is paired. Each row is reported by the fingerprint
of its present side. A version window compares
each adjacent pair this way, so a Type added partway through the window has
Member rows for every pair that contains it on either side
([#9920](https://github.com/richlander/dotnet-inspect/issues/9920)), one-sided
for the pair that added it
([#10224](https://github.com/richlander/dotnet-inspect/issues/10224)).

The partition is defined by ownership of the Library level's own census
facts, not by a separate list, so the two levels cannot disagree. Every fact
except the Type's visibility marker, which the residual owns, carries the key
of the row it describes as a nested [`SymbolKey`](#identity) part, so the row a
fact describes is read from the fact's structure, never from its spelling:

1. **Every census fact has one owner.** A fact belongs to the Member whose
   declaration row it describes: the method, field, property, or event row,
   and that row's parameters, constants, imports, generic parameters, and
   attributes. Two kinds of row are not Members in their own right:
   - **Un-raised rows** encode a source construct in metadata. They belong
     to the construct the API surface raises them to: accessors, an
     auto-property's backing field, and a field-like event's backing field
     to their property or event, and an enum's `value__` field, which
     carries the underlying Type, to the residual. A row the API surface
     does not list as a Member and that is not generated code belongs to
     the residual.
   - **Generated code** is a lifted body (a lambda, local function,
     iterator, or async state machine) with its methods, fields, and
     nested Types. It belongs to its owner, as below.

   Every other fact (the Type's own row and visibility, base Type,
   interfaces, generic parameters, attributes, layout, nullable context,
   and MethodImpl rows) belongs to the residual, and so does the IL of a
   method no Member owns, such as a static constructor.
2. **A fact keeps the axis the census gives it.** A Member's or the
   residual's state on an axis is `Changed` when any fact it owns on that
   axis differs, including a fact present on one side only. The API axis is
   decided on the public, protected, and protected internal Members of a
   public Type: the census decides that visibility, which includes explicit
   interface implementations, and partitions a Type public on either side as
   public on both. A public Member of such a Type has API facts. The Body
   axis is decided at every accessibility: every Member's declaration facts
   that are not API facts, and the IL of every method it owns, are its Body
   facts, so a private, internal, or `private protected` Member's row,
   signature, flags, constants, import, and attributes are Body facts, and an
   added private field is Body `Changed`. Every row is seen and keyed: a
   Member that owns no API facts is API `Unchanged`, never omitted, and a
   consumer that wants only the public view filters the rows it is given.
   Where the Type is hidden on one side, the
   census records only its visibility there, so that change makes the
   residual and every public Member API `Changed`. Every Member of a Type
   hidden on both sides owns no API facts, so its API state is `Unchanged`
   and only its Body state can differ; the Type's own declaration is then a
   residual Body fact. A Member or the residual
   whose facts differ only in uncertain facts is decided as the
   [API axis](#axes) decides a Type, by the API axis surface of that
   Member or of the Type's own declaration.
3. **IL compares where the Library level compares it.** A Member's Body is
   the IL of its methods present on both sides and of the generated code it
   owns, to the first difference, with canonical equality under the Body
   policy. A method with a body present on one side only, or a paired method
   with a body on one side only, makes its owner's Body `Changed`, at either
   level, without reading its IL.

A Member **owns facts** on an axis when the census records on that axis at
least one fact whose owner under rule 1 is that Member: API facts exist only
where the API axis surface admits the Member, and Body facts are the
declaration facts of a Member the surface does not admit and the IL of each
method the Member owns that has a body on that side. Member API compares
completely, so every changed public Member is marked; Member Body is an
existence check. A Member declared on one side only is therefore `Changed` on
each axis where it owns facts: a private field on Body, a public field on
API, and a public method with a body on both.

An owner is never `Unchanged` by omission. A fact or body that cannot be
decided, such as IL whose walk reaches an undefined opcode before it decides a
difference, or a reference to a compiler-
controlled method, makes the axis of the Member that owns it `Indeterminate`,
where the owner is the row the census was describing, raised under rule 1; when
that row has no single Member owner, the residual's axis is `Indeterminate`.
When the census of the Type cannot be completed on an axis, every Member and the
residual not already `Changed` on that axis are `Indeterminate`. A Type whose
rows cannot be told apart, because two rows spell one name, and a Type
identified by its row token have no Type-level rows; their view is the Library
level's `Indeterminate` state. So a Library axis that is `Indeterminate` is
never shown as `Unchanged` for every owner.

Generated code belongs to a Member only when the shared lifted-owner
resolution in `ILInspector.Analysis` names exactly one owner, as it does for
the targeted walk
([#9745](https://github.com/richlander/dotnet-inspect/pull/9745)). A change
in generated code shared by several Members, or whose owner is ambiguous, is
a residual Body change. It is never assigned to a guessed Member. A
generated Type follows its owner as a whole: its row, fields, properties and
their accessors, interfaces, and MethodImpl rows belong to the one Member
that owns all of its methods, and otherwise to the residual. That resolver
is internal to `ILInspector.Analysis` today; step 7 adds a public entry
point that resolves the owners of one Type's generated methods.

Each Member is reported by the fingerprint of its After-side `MemberAnchor`, the
identity the Members list already carries for every accessibility, and a removed
Member by its Before-side fingerprint. A Member present on both sides, whose
census key is the same in both images, is also reported by its Before-side
fingerprint, so a consumer can join two pairs that share an image by the
fingerprints both read from that image, as a version window
([#9920](https://github.com/richlander/dotnet-inspect/issues/9920)) does. Its
two fingerprints differ when an annotation the anchor includes, such as
nullability, changes while the census key does not. As
[API qualified anchor](api-qualified-anchor.md#version-pair-correspondence)
states, anchor inequality is not absence: the Type level reports states per
declared Member and does not classify a Member as renamed, added, or
removed. The query resolves anchors over the selected Type's declarations
only, through `MemberTargetResolver`, as exact Member lookup does
([#9739](https://github.com/richlander/dotnet-inspect/pull/9739)); it never
builds the Library's API surface.

The query runs in [steps](#steps) that end at Member boundaries. It lives in
`DotnetInspector.Queries`, beside `AssemblyContextFindingDiffAnyQuery`, because
generated-code owner resolution comes from `ILInspector.Analysis`; anchors and
`MemberTargetResolver` are in `ILInspector.Metadata`, and the per-Member fact
partition itself stays in `FindingDiffAny`.

Gates: for the fixture pairs, the Type-level states agree with the Library level
per the partition claim, including an added public method, an added public
property with a private setter, a public Type made internal, an enum
underlying-type change, a `[field:]` attribute on a field-like event, a base
Type change on an internal Type, a marker interface removed from a public Type,
which makes the residual API `Changed` and every Member `Unchanged`, and a
nested Type whose inherited nullable context changed; an added and a removed
public Type, the added one with Members and the removed one with none, are
reported whole by the Library level and have one-sided Type-level rows whose
keys equal the keys of the same Type's two-sided rows against itself, in which a
public method with a body is `Changed` on both axes, an internal method with a
body, a private field, and a bodyless private `extern` method are Body `Changed`
only, the residual is `Changed` on both axes, and no IL is read; an added
internal enum has Member rows all Body `Changed` and API `Unchanged`, and a
residual API `Unchanged` and Body `Changed`; an undefined opcode reached before
a difference in one method makes that Member Body `Indeterminate` and no owner
`Unchanged` by omission; two Type rows of one name have no Type-level rows; a
Member present on both sides carries both fingerprints, which differ after a
nullable annotation change; an attribute added to an internal method and a
private field added to a public Type are each Body `Changed` for that Member and
no API change; a Member is API `Changed` exactly when the complete Public API
diff reports it changed, or reports its Type added or removed and the API axis
surface admits the Member on the side where it admits the Type, and every other
Member is API `Unchanged`, which covers a paired Type, a Type made internal, and
a one-sided public or internal Type in one rule; for every Type present on both
sides, every owner of a body that canonical IL comparison reports changed is
Body `Changed` or `Indeterminate`, or the change is a residual Body change, and
every other owner Body `Changed` owns a declaration fact that differs; and for a
Type present on one side, an owner is Body `Changed` exactly when the API axis
surface does not admit it, it owns a method with a body, or it is the residual.
`eng/measure-finding-diff-any.cs` reports NativeAOT time per Type for the
largest changed Types of each measured pair.

**Type inventory.** Before the Member fold exists, one paired Type's rows are
readable as the Library level compares them: `FindingDiffAnyTypePairing` pairs
the Types of two images once, by the Type identity above, and its `Inventory`
returns, for one paired Type, every census fact present on one side only, on
the axis the census gives it and spelled from its expansion, every `Ref`
replaced by the key it names so that neither the spelling nor the rows' order
carries an interning id, and every method the Body census pairs with its Body
state through the pairing's body reader, the walk unless a
`FindingDiffAnyBodyReader` is supplied. The inventory's fold is the Library
level's state for the Type, which the gates check for every paired Type of the
fixture pair: API `Changed` exactly when an API fact is present on one side
only, Body `Changed` when a Body fact is, and otherwise the state of the first
paired method, in the Body census's method order, whose body is not
`Unchanged`, so an `Indeterminate` row after a `Changed` one folds to `Changed`
as the Library level reads it, and every row keeps its own state. One name the
pairing withholds: two paired Type rows that spell one display name, which the
Library level decides each by key, are `Indeterminate` under that name, since
the name selects neither row. The [declaration
cursor](finding-census.md#adoption) reads the same slots, and its
identity-set comparison of one Type's census finds the same one-sided facts.
Types present on one side only are not paired; their one-sided form is this
level's. Step 7 folds these rows into Member states under the ownership rules
above, reading each fact's owner from its key parts, and the inventory is the
closed set it folds: the rows are fixed by the two censuses before any body is
read. The Type scenario of [Diff system](diff-system.md#scenarios) enters here,
and [Finding census](finding-census.md#cost-at-type-scope) records what one
Type costs through each reader.

### Member

The Member level is the complete API and body diff of one Member, which
[Library API Diff](inspect-web-library-api-diff.md) and
[Member Body Diff](inspect-web-member-body-diff.md) own. Entered from a Type
or Member cue, it finds that one Member in each image over its Type's
declarations, through the version-pair correspondence that
[API qualified anchor](api-qualified-anchor.md#version-pair-correspondence)
defines, and diffs only that Member. It does not build the Library-wide
Member Body inventory, which remains Library Compare's view.

## Adoption

1. **Producer** (#9798): `FindingDiffAny.Compare`, its gates, and the
   measurement tool.
2. **Browser export** (#9805): per-Type states through a Worker export, with
   Browser/Wasm numbers.
3. **Steps and API axis** (this slice): bounded steps that yield the Worker
   between them, and the API axis alone for the major-version baseline.
4. **Background analysis:** a page-owned queue that runs the default
   baselines after a Library page loads and drives the navigation cues.
5. **Library Compare:** replace the content selector's Public API and Member
   Body choices with **Any Diff** (default), **API Diff**, and **Body Diff**
   lists from Finding Diff Any, keep **String literals**, and link each Type to
   its complete signature-only or body-level diff.
6. **Caching:** retain results per exact endpoint pair in the Worker and in
   persistent storage keyed by the build commit, per
   [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).
7. **Type level:** per-Member states and the residual for one Type,
   including its one-sided form
   ([#10224](https://github.com/richlander/dotnet-inspect/issues/10224)), with
   NativeAOT numbers, through a Worker export. Member cues for the Type on
   screen then come from it: the API Member glyph, the implementation Member
   glyph, or the change glyph for both. This replaces the Type-surface
   Library API Diff that places API Member cues today, which computes the
   whole Library's API diff.
8. **Member level:** a targeted diff of one Member, entered from a cue,
   after API qualified anchor's correspondence adoption
   ([#9827](https://github.com/richlander/dotnet-inspect/issues/9827)).
9. **CLI:** expose the same producer through `diff`.

Two slices adopt the shared equality before step 7, which builds on both:

- **Shared Body equality** (landed,
  [#10196](https://github.com/richlander/dotnet-inspect/issues/10196)): the
  Body axis walks IL with canonical equality under the Body policy and keeps
  the declaration census beside the walk, gated per method against canonical
  IL comparison and by the canonical encodings and dependency versions pairs,
  over the policy's normalization
  ([#10205](https://github.com/richlander/dotnet-inspect/issues/10205));
  Member Body Diff's IL comparison requests the Body policy, gated by the
  dependency versions pair showing no IL row there either.
- **Shared API equality:** the census decides `Unchanged` and presented
  differences, and the API axis surface of one Type decides the rest,
  gated for exactness against the complete Public API comparison of the API
  axis surface.

These slices make Finding Diff Any's equality the complete diffs' own, so a
complete diff can adopt the same walk as its first pass and explain only what
it finds changed. That adoption belongs to each complete diff's owner.

By default, each Library page computes Finding Diff Any in the background after
it first loads, against two baselines: `ApiAndBody` against the last patch
version and `Api` against the last major version. An explicit Library Compare
targets the one version the user selects. The background run acquires the
baseline packages without a user action, which is this design's approved
exception to explicit network work. Scheduling, ordering, and baseline selection
belong to the background analysis design that adopts this export.

## Non-claims

- Finding Diff Any does not define API semantics, canonical IL, or
  correspondence; it answers with the equality their owners define.
- It does not present the declaration facts it compares; the
  [declaration view](declaration-view.md) does.
- It does not report referenced assembly or package version changes as Type
  changes; those versions are compared as candidates in their own right.
- It does not report which change was found, counts, classifications, or text;
  the Member level's complete diff does.
- It does not replace Public API Diff or Member Body Diff; they remain the
  complete views.
- It reports Type definitions only. Type forwarders and assembly-level
  attributes are outside the per-Type contract.
- `FullName` is a display name. A namespace and a nested Type can spell the
  same name (`A.B.C`); such Types remain distinct results with equal names and
  distinct `Identifier` values.
