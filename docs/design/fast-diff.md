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
  parameters and constraints, custom attributes, and layout;
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

Neither axis is configurable. A public member present on one side only is an
API fact; its body is not compared. A Type public on either side partitions
its facts as public on both, so a visibility change is an API fact only.

### Identity

Tokens compare by symbolic name, resolved once per side and memoized. They
never compare by token number or referenced assembly identity, so a
referenced framework moving from one major version to the next is not a
difference.

Compiler-generated nested Types (state machines, closures, local-function
holders, and Types nested beneath them) belong to their nearest declared
Type. A change confined to an async method, lambda, iterator, or local
function therefore changes the Body of the Type that declares it. Types
beneath a top-level compiler-generated Type, such as
`<PrivateImplementationDetails>`, are not reported; a body that references
their content-named members still compares those names.

### Known over-reports

`Changed` may appear where the complete diff shows nothing:

- a renumbered compiler-generated name, such as a closure or state machine
  renumbered by a new member earlier in the Type;
- reordered or re-encoded IL that canonical comparison treats as equal; and
- non-IL implementation facts on the Body axis, such as an attribute added to
  an internal member.

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

`eng/measure-fast-diff.cs` runs the same checks over a real image pair and
reports NativeAOT timing and over-reporting. At introduction, over six real
pairs, no Type was missed on either axis except three CoreLib `calli` sites
that canonical IL comparison reports from raw signature bytes containing
renumbered tokens. Medians cover both sides, every Type, API and bodies:

| Pair | NativeAOT | API changed (complete) | Body changed (canonical IL owners) |
| --- | ---: | --- | --- |
| Aspire.Hosting 13.6.0 to 13.6.1 | 89 ms | 0 (0) | 2 (2) |
| System.Text.Json 9.0.0 to 10.0.0 | 15 ms | 12 (12) | 112 (84) |
| Newtonsoft.Json 13.0.3 to 13.0.4 | 18 ms | 36 (33) | 76 (36) |
| System.Private.Xml 10 to 11 | 63 ms | 3 (0) | 78 (74) |
| System.Private.CoreLib 10 to 11 | 206 ms | 157 (133) | 641 (314) |

## Adoption

1. **Producer** (this slice): `FastDiff.Compare`, its gates, and the
   measurement tool.
2. **QuerySpace and Browser export:** expose per-Type states through a Worker
   export, with Browser/Wasm numbers, and the API axis returned before the
   Body axis when that shortens time to first result.
3. **Library Compare:** replace the content selector's Public API and Member
   Body choices with **Any Diff** (default), **API Diff**, and **Body Diff**
   lists from Fast Diff, keep **String literals**, and drive the navigation
   cues from Fast Diff states. A Type or Member click still opens its
   complete diff.
4. **Caching:** retain results per exact endpoint pair in the Worker and in
   persistent storage keyed by the build commit, per
   [#9793](https://github.com/richlander/dotnet-inspect/issues/9793).
5. **Member states:** per-Member states for an opened Type.
6. **CLI:** expose the same producer through `diff`.

Fast Diff runs only when the user is in Library Compare Diff; automatic
background generation is a later decision.

## Non-claims

- Fast Diff does not define API semantics, canonical IL, or correspondence; it
  is sound against the owners of those.
- It does not report which change was found, counts, classifications, or text.
- It does not replace Public API Diff or Member Body Diff; they remain the
  complete views.
