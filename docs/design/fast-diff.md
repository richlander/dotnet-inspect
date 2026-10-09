# Fast Diff

## Status and owner

This document owns **Fast Diff**: one pass over a Library image pair that
decides, for every Type, whether its API and its implementation differ,
without building either complete diff. It is tracked by
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716) and
supersedes the earlier body-free, presence-only design proposed in #9717.

The producer is `FastDiff.Compare` in `ILInspector.Metadata`. It depends only
on System.Reflection.Metadata, so it runs unchanged under NativeAOT and
Browser/Wasm.

## Why

Library Compare needs to show which Types changed so a user can choose where
to look. A complete Library diff answers far more than that and costs far
more. Most Types in a version pair are unchanged, and proving that is the
dominant cost, so Fast Diff makes the unchanged case cheap and stops each
changed axis at its first difference.

## Contract

> **Claim.** For one Library image pair, Fast Diff returns, for
> every Type, an **API** state and a **Body** state, each `Unchanged`,
> `Changed`, or `Indeterminate`. `Unchanged` is sound for the facts its axis
> compares. `Changed` may over-report. A row that cannot be decoded makes the
> unsettled axes of its Type `Indeterminate`, never `Unchanged`.

The two axes are independent searches. Each stops at its own first
difference, and a host derives **Any** as either axis `Changed`. A Type that
changed its API still walks its bodies until the first body difference, so the
worst case for a changed Type is the cost of an unchanged one.

Types are named by their `ApiType.FullName` spelling: `.` between nested names
and the metadata backtick arity.

### Axes

The **API** axis compares the Public API facts of a Type and its members:

- Type flags (except `beforefieldinit`), base Type, interfaces, generic
  parameters and constraints, custom attributes, layout, and the effective
  nullable context, which a nested Type inherits from its declaring Type;
- public, protected, and protected internal member signatures and flags,
  parameter names, flags, and defaults, constants, and custom attributes,
  including compiler-emitted attributes such as nullable annotations that
  shape rendered signatures; and
- explicit interface implementations.

A Type is public by its own row (public, nested public, protected, or
protected internal), matching the Public API surface, which admits a public
nested Type of a non-public Type. Non-public Types have no API facts.

The **Body** axis compares the implementation of every member, public or
not, as `--all` would:

- IL, compared in lockstep with symbolic operands, resolved user strings,
  branch and switch operands, exception regions and catch Types, local
  variable Types, `init locals`, and stack size;
- the metadata of non-public members; and
- `beforefieldinit`, when the Type has a static constructor.

Neither axis's facts are configurable. A caller can ask for the API axis
alone; every Body state is then `NotCompared` and no IL is read. A public member present on one side only is an
API fact; its body is not compared. A Type public on either side partitions
its facts as public on both, so a visibility change is an API fact only.

### Steps

`FastDiffComparison` advances one comparison in bounded steps for a
single-threaded host. Each step takes readers over the same two images, makes
progress on at least one Type row or compared Type, and stops at the first
such boundary after its budget elapses. The retained state holds handles and
strings, never a reader, so a host re-enters its own image callbacks for each
step; a reader over a different image is rejected by module version ID.
Stepping reaches exactly the result of one uninterrupted `FastDiff.Compare`.
Cancellation is observed at the same boundaries.

### Identity

Metadata tokens, in IL operands and in signatures, compare by symbolic name,
resolved once per side and memoized. They never compare by token number or
referenced assembly identity, so a renumbered or retargeted reference in an
operand or signature is not a difference. Custom attribute and constant
values are compared as raw blob bytes (see [Observability](#observability)).

Compiler-generated nested Types (state machines, closures, local-function
holders, and Types nested beneath them) belong to their nearest declared
Type. A change confined to an async method, lambda, iterator, or local
function therefore changes the Body of the Type that declares it. Types
beneath a top-level compiler-generated Type, such as
`<PrivateImplementationDetails>`, are not reported; a body that references
their content-named members still compares those names.

### Observability

Fast Diff adopts [Diff observability](diff-observability.md). The API axis
targets the semantic level of the Public API diff, and the Body axis targets
the symbolic IL level of the body diff, so `Changed` should point to something
a user can see. IL operand and signature keys never compare token numbers or
referenced assembly identity, so a renumbered token there is not a change.

`Changed` may still over-report. These compared facts are known divergences
from that target:

- **Encoded IL.** The Body axis walks IL in lockstep and compares opcode
  encodings (`br.s` against `br`), branch and switch offsets, body size, stack
  size, and exception region offsets. An encoding-only difference, such as a
  re-encoded branch that canonical comparison treats as equal, reports
  `Changed`.
- **Type flags no view presents.** Type flags are compared raw, except
  `beforefieldinit`. Sealed, abstract, static, visibility, and layout are
  presented; flags such as `Serializable` and the string format are not, on
  the API axis for public Types and on the Body axis otherwise.
  `beforefieldinit` is a Body fact when the Type has a static constructor, and
  no view presents it.
- **Attribute blobs.** Custom attribute values are compared as raw blob
  bytes. A Type-valued or enum-typed argument is stored as an
  assembly-qualified name that includes the referenced assembly version, so a
  framework major-version move reports `Changed` for a semantically identical
  attribute, for example on System.Linq.Queryable from 10 to 11. Comparing
  those arguments by symbolic name is
  [#9802](https://github.com/richlander/dotnet-inspect/issues/9802).

Two other reported differences are observable and remain by design:

- a renumbered compiler-generated name, such as a closure or state machine
  renumbered by a new member earlier in the Type, which the IL view shows; and
- non-IL implementation facts on the Body axis, such as an attribute added to
  an internal member, which the complete views with every member show.

Canonical IL comparison is read under rule 6 of Diff observability: it
reports three System.Private.CoreLib 10 to 11 `calli` sites whose stand-alone
signatures differ only in token numbers. Fast Diff decodes those signatures
symbolically and correctly reports them `Unchanged`; IL diff canonicalization's
adoption is
[#9801](https://github.com/richlander/dotnet-inspect/issues/9801).

## Gates

`FastDiffTests` gates:

- exact API and Body states for one targeted change per Type in the
  `ILInspector.Metadata.FastDiff` fixture pair, including lambda-, async-, and
  local-function-only bodies, constraint Types, constants, defaults,
  parameter names, attributes, interfaces, visibility, and added and removed
  Types;
- API soundness: every Type the complete Public API comparison (signatures
  and attributes) reports changed is not API `Unchanged`, over three fixture
  pairs; and
- Body soundness: every declared owner of a body that canonical IL comparison
  reports changed is not Body `Unchanged`, over the same fixture pairs.

Stepping and the API axis are gated by outcome: a comparison stepped at
every row and Type boundary, with fresh readers per step, equals the whole
comparison over the fixture pair and every malformed image in
`FastDiffMetadataSafetyTests`; a reader over a different image is rejected;
and API-only states equal the API states of the full comparison with no body
compared.

`eng/measure-fast-diff.cs` runs the same checks over a real image pair and
reports NativeAOT timing, API-only and stepped timing, and over-reporting. At introduction, over six real
pairs, no Type was missed on either axis, apart from the three encoding-only
`calli` sites described under [Observability](#observability). Medians cover
both sides, every Type, API and bodies, on one Apple silicon development
machine:

| Pair | NativeAOT | API changed (complete) | Body changed (canonical IL owners) |
| --- | ---: | --- | --- |
| Aspire.Hosting 13.6.0 to 13.6.1 | 99 ms | 0 (0) | 2 (2) |
| System.Text.Json 9.0.0 to 10.0.0 | 16 ms | 12 (12) | 112 (84) |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 20 ms | 36 (33) | 76 (36) |
| Newtonsoft.Json 11.0.2 to 13.0.4 | 16 ms | 108 (83) | 213 (104) |
| System.Private.Xml 10 to 11 | 68 ms | 3 (0) | 78 (74) |
| System.Private.CoreLib 10 to 11 | 216 ms | 157 (133) | 641 (314) |

In these measurements, Fast Diff compares both sides of most Libraries,
every Type, API and bodies, in under 100 ms on NativeAOT; only the largest
assemblies, such as System.Private.CoreLib, take longer.

Canonical IL comparison could not decode some bodies (for example 5,454 in
CoreLib), so Body soundness is shown only for the bodies it compared.

### Malformed metadata

Every walk over artifact-derived metadata is bounded. Declaring chains and
Type reference resolution scopes use the shared `MetadataRelationshipTraversal`
primitives, and every signature decode passes `SignatureBlobGuard`, with Type
specifications under `TypeSpecGuard`. A rejected walk or decode makes the
affected Type `Indeterminate` on both axes, and so does a Type row whose name
cannot be read; other Types keep their states, and no metadata shape
terminates the process. `FastDiffMetadataSafetyTests` gates cyclic and
100,000-deep Type reference chains, nested Type chains, a self-referential
Type specification, a 100,000-deep signature blob, and top-level and nested
Type names outside the string heap in a child process.

## Browser export

`MetadataExports.QueryLibraryFastDiff` (Worker operation
`queryLibraryFastDiff`, request schema 2) runs `AssemblyContextFastDiffQuery`
over the implementation assemblies of one exact compile asset in two package
versions. It accepts the Library API Diff endpoint coordinates and `axes`,
`ApiAndBody` or `Api`. It returns only Types with an axis that is `Changed` or
`Indeterminate`, plus the compared Type count; absence means every requested
axis is `Unchanged`. Each Type carries `identifier`, the escaped definition
name (`Outer+Inner`) that Library navigation joins on, and `fullName` for
display. Malformed rows that have no decodable name receive a row-token
identifier and never join a navigation entry.

The Worker is single-threaded, so the operation runs the comparison in
[steps](#steps) of at most 8 ms and yields to the Worker event loop between
them. Foreground operations and cancellation run between steps; one Fast Diff
never holds the Worker for its whole duration.

Firefox, published Release site, warm calls through the production Worker
(interpreted Wasm). Foreground is a cheap Worker call issued every 25 ms while
one `ApiAndBody` comparison runs; it takes 1 ms when the Worker is idle:

| Pair | Types | API and Body | API only | Foreground median (max) |
| --- | ---: | ---: | ---: | ---: |
| Aspire.Hosting 13.6.0 → 13.6.1 | 1,144 | 1,561 ms | 225 ms | 40 ms (69 ms) |
| System.Text.Json 9.0.0 → 10.0.0 | 320 | 303 ms | 91 ms | 40 ms (54 ms) |
| Newtonsoft.Json 13.0.3 → 13.0.4 | 300 | 348 ms | 104 ms | 39 ms (45 ms) |

Before stepping, one Aspire.Hosting comparison held the Worker for 1,490 ms.
The comparison itself accounts for more than 99% of each call; the interpreter
costs roughly 15x relative to NativeAOT.

## Adoption

1. **Producer** (#9798): `FastDiff.Compare`, its gates, and the measurement
   tool.
2. **Browser export** (#9805): per-Type states through a Worker export, with
   Browser/Wasm numbers.
3. **Steps and API axis** (this slice): bounded steps that yield the Worker
   between them, and the API axis alone for the major-version baseline.
4. **Background analysis:** a page-owned queue that runs the default
   baselines after a Library page loads and drives the navigation cues.
5. **Library Compare:** replace the content selector's Public API and Member
   Body choices with **Any Diff** (default), **API Diff**, and **Body Diff**
   lists from Fast Diff, keep **String literals**, and link each Type to its
   complete signature-only or body-level diff.
6. **Caching:** retain results per exact endpoint pair in the Worker and in
   persistent storage keyed by the build commit, per
   [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).
7. **Member states:** per-Member states for an opened Type.
8. **CLI:** expose the same producer through `diff`.

By default, each Library page computes Fast Diff in the background after it
first loads, against two baselines: `ApiAndBody` against the last patch
version and `Api` against the last major version. An explicit Library Compare
targets the one version the user selects. The background run acquires the
baseline packages without a user action, which is this design's approved
exception to explicit network work. Scheduling, ordering, and baseline
selection belong to the background analysis design that adopts this export.

## Non-claims

- Fast Diff does not define API semantics, canonical IL, or correspondence; it
  is sound against the owners of those.
- It does not report which change was found, counts, classifications, or text.
- It does not replace Public API Diff or Member Body Diff; they remain the
  complete views.
- It reports Type definitions only. Type forwarders and assembly-level
  attributes are outside the per-Type contract.
- `FullName` is a display name. A namespace and a nested Type can spell the
  same name (`A.B.C`); such Types remain distinct results with equal names and
  distinct `Identifier` values.
