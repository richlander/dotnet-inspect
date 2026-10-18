# Diff capabilities

## Status and owner

This document owns the **diff capabilities**: the questions a diff answers
about a version pair, from one bit per Type to the complete diff of one
Member, as capabilities of one diff owner over one fact substrate. It owns:

- the question ladder and each rung's precision;
- the two axes and what each covers;
- the Exists contract between a cheap answer and the complete diff;
- the rule that comparison is expansive and filtering is presentation;
- how an uncertain fact kind is settled;
- the provisions that answer each rung, the coverage between rungs, and when a
  separate provision is kept or deleted; and
- the latency bar that keeps Exists distinct from Rows.

It is tracked by
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227), whose
comments hold the operator decisions and spike evidence this document records.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Producer capabilities](query-space-producer-capabilities.md) | Capabilities, provisions, owner-certified coverage, and provision plans |
| [Diff system](diff-system.md#request) | The diff request, its axes per step, version windows, and the operation |
| [Diff observability](diff-observability.md) | A reported change has a view; over-reporting stays within observability |
| [Fast Diff](fast-diff.md) | Today's Exists provision: its census, identity, axes, and Type level |
| [Annotated Type Map](annotated-type-map.md) | The per-Member differences of a Type |
| [Annotated Source diff document](annotated-source-diff-document.md) | The complete diff of one Member |
| [Symbol Key](symbol-key.md) | Byte-exact symbolic identity |
| [Scope symmetry](scope-symmetry.md) | Rows, Count, and Exists at each scope |

## Why

Two implementations of one question drift. Fast Diff and the complete diffs
each define "changed" in their own code, and the
[Annotated Type Map spike](https://github.com/richlander/dotnet-inspect/issues/10215#issuecomment-6246071379)
found that each side had bugs the other did not
([#10223](https://github.com/richlander/dotnet-inspect/issues/10223),
[#9801](https://github.com/richlander/dotnet-inspect/issues/9801)).

The producer-capability model already answers this for other producers: one
producer offers capabilities of rising richness, each may have its own
provision, and the owner certifies how a richer answer covers a cheaper one.
A cheap path is legitimate when it is a provision of the same owner, over the
same facts, held to the same answer. It is not legitimate as a second
definition.

A fast diff is worth having for the same reason a hash check or a regular
expression is: a high-fidelity answer over a large body, cheaply, makes
ecosystem-scale and history-scale questions affordable. So the cheap rung is
judged on latency, and the rich rungs on completeness.

## The question ladder

| Rung | Question | Scope | Precision | Today's provision |
| --- | --- | --- | --- | --- |
| 1 | Does this Type have any diff? | Library | 1 bit per Type per axis | Fast Diff, Library level |
| 2 | Which Members of this Type have any diff? | Type | 1 bit per Member per axis | Fast Diff, Type level |
| 3 | Which differences does this Type have, per Member? | Type | multi-byte per Member | Annotated Type Map |
| 4 | What is the complete diff of this Member? | Member | multi-byte | Annotated Source diff document |

Rungs 1 and 2 are **Exists** questions: one bit per subject, stopped at the
first difference. Rungs 3 and 4 are **Rows** questions: they explain. Each
rung covers the one above it: rung 4 for a Member decides that Member's bit at
rung 2, and rung 2 for a Type decides that Type's bit at rung 1. Those coverage
relationships are certified by the gates below, as the
[producer-capability model](query-space-producer-capabilities.md#covering-relationship)
requires.

## Axes

Every rung answers per **axis**:

- **API**: the Public API of public and protected subjects, including subjects
  hidden from listing, such as `EditorBrowsable(Never)` and `Obsolete` ones.
  Hidden is a facet of the subject, not a reason to skip it.
- **Body**: every subject at every accessibility, its implementation and its
  non-public declarations. An added private field is a Body change.

A request names its axes, as the [diff request](diff-system.md#request) does
per step. The default is API and Body. Within an axis nothing is filtered.

### Comparison is expansive; filtering is presentation

A comparison reports every difference on its axes. It does not apply
presentation filters, such as public Members only or skipping hidden subjects;
a host applies those to the rows it shows, from facets each row carries. The
latency gap between Exists and Rows is what makes an expansive comparison
affordable: Exists stays cheap, and the cost of richer rows is paid only for
the Types a user opens.

Exists is not filtered either. A host that navigates Library to Type to Member
follows the diff to wherever it is: a Type whose only Body change is in a
private Member opens with that Member listed, and the Member opens directly. A
mark therefore never points to an empty view.

### Policies and default targets

The diff system's two Compare policies select axes, not filters:

| Policy | Axes |
| --- | --- |
| Public API Evolution | API |
| Secure Supply Chain Audit | API and Body |

The default comparison targets of a current version are its previous patch,
on API and Body, and the latest patch of the previous major, on API only. For
a current 11.0.2, those are 11.0.1 (API and Body) and the latest 10.0 patch
(API only). A policy may expose a depth that adds further candidates. Window
projection and its steps belong to
[version resolution](version-resolution.md#version-window-projection) and
[#9920](https://github.com/richlander/dotnet-inspect/issues/9920); this
document only states that each step names its axes.

## The Exists contract

1. **Unchanged is exact.** When Exists reports a subject `Unchanged` on an
   axis, the complete diff on that axis has nothing to show for it at any
   level it presents. A missed change is never acceptable.
2. **Changed is observable.** When Exists reports `Changed`, the complete diff
   shows at least one difference for that subject on that axis. The
   difference may be low-signal; it is never empty. This is rule 4 of
   [Diff observability](diff-observability.md#rules) applied to Exists.
3. **Undecided is never read either way.** A subject Exists cannot decide is
   `Indeterminate`, as Fast Diff defines it.

The contract binds provisions, not code. Whether two provisions share code is
an implementation choice; their answers are bound by this contract and gated.

### Uncertain fact kinds

An Exists provision compares facts more cheaply than the complete diff does,
so a raw difference in some fact kinds may not be a difference the complete
diff presents. Such a kind is **uncertain**. Each uncertain kind is settled at
design time, in one of two ways:

- **Promote:** the Exists provision learns the kind's normalization, and a
  gate shows it now decides as the complete diff does. Promotion is admitted
  only where Exists keeps its latency.
- **Present:** the complete diff learns to show the raw difference, marked
  low-signal, so Exists's `Changed` always opens a non-empty diff.

A kind whose raw difference recurs across a Library, such as an attribute blob
that differs only in a dependency's version, is promoted: presenting it would
make every Type `Changed` in a dependency-only change. A rare kind may be
presented.

Settling an uncertain kind at Exists time, by building the complete diff's
model for the Type, is a temporary fallback. Every Exists measurement reports
how often it happened, and the target is never.

The
[uncertain-kind census](https://github.com/richlander/dotnet-inspect/issues/10227#issuecomment-6254506987)
assigns today's kinds:

| Kind | Decision |
| --- | --- |
| Renderable attribute added, removed, or changed | Present (already shown) |
| Hidden subject | Present: the API axis includes hidden subjects as a facet |
| Nullable rows and context with no annotated value on either side | Promote |
| Compiler-emitted attributes (`Intrinsic`, state-machine `typeof`, Type-level `Extension`) | Promote |
| Implementation flags (inlining, `InternalCall`, `Async`) on the API axis | Promote, by moving them to Body |
| Attribute argument the renderer cannot show (generic `typeof`) | Present |
| Referenced-assembly identity inside an attribute blob | Promote |
| Flag kinds, `layout`, `field-offset`, `import` | Decided with a fixture; `layout`, `field-offset`, and `import` are expected to be presented |

## Provisions and coverage

A **provision** is one way the diff owner produces a rung's answer. The owner
offers:

| Provision | Answers | Shape |
| --- | --- | --- |
| Lockstep Exists | Rungs 1 and 2 | Walks both images, compares symbolic facts, stops each axis at its first difference |
| Retained maps | Rungs 2 and 3 | Produces one version's facts once and compares two retained maps by key |
| Complete | Rungs 3 and 4 | Builds the complete models and explains every difference |

All provisions share one fact substrate: Member and operand identity are
[Symbol Keys](symbol-key.md), Member grain is Fast Diff's census key, and the
facts are the axes' facts above. Coverage runs from richer to cheaper: a
Member's complete diff decides its rung-2 bit, a Type's rung-3 map decides its
rung-2 bits, and a Type's rung-2 bits decide its rung-1 bit. Each edge's proof
obligation is a gate below.

### When a separate provision is kept

The difference between a cheap provision and a complete one is only latency
for its contract against the cost of keeping a second implementation that
must agree. A separate provision is kept while it delivers latency the shared
code does not, and is deleted when the shared code delivers enough.

Fast Diff is today's lockstep Exists provision. The
[symbolic-identity spike](https://github.com/richlander/dotnet-inspect/issues/10227#issuecomment-6253951916)
measured the complete IL diff's own code, with symbolic identity, at 0.67 to
1.19 times Fast Diff's Body cost on six pairs, with every method pair agreeing.
Fast Diff's Body walk is therefore deleted once the shared code meets the
latency bar below. Its API census remains the API axis's Exists provision,
owned by the diff owner as the API comparison's first stage, because the
surface model costs about ten times more.

## Latency bar

- **Structural.** An Exists provision builds no complete model, materializes no
  instruction arrays, and formats no text. Every measurement reports any
  fallback to the complete model.
- **Numeric.** On NativeAOT at Library scope, over the
  [Fast Diff gate pairs](fast-diff.md#gates), API and Body Exists costs no more
  than shipped Fast Diff on that pair, and at least three times less than the
  complete diff's Rows over the same pair.

The structural rule is what keeps Exists distinct from Rows as the complete
diff gets faster; the numeric rule is the gate.

## Adoption

Each step is its own focused effort:

1. **Axes.** The API axis includes hidden subjects as a facet, and the Body
   axis includes non-public declarations, in Fast Diff and in the
   [Annotated Type Map](annotated-type-map.md)'s listing, with a view for each
   non-public declaration. This resolves
   [#10224](https://github.com/richlander/dotnet-inspect/issues/10224)'s
   private-field case.
2. **Uncertain kinds.** Promote or present each kind in the order the census
   measured, removing every Exists fallback on the gate pairs.
3. **Shared Body Exists.** The complete IL diff takes symbolic identity
   ([#9918](https://github.com/richlander/dotnet-inspect/issues/9918)) and a
   lockstep comparison without materialized instructions, then meets the
   latency bar; Fast Diff's Body walk is deleted.
4. **API census ownership.** The census moves under the API comparison as its
   first stage, so the API axis has one owner.
5. **Requests.** The diff request names axes per step and Exists or Rows per
   rung ([#10200](https://github.com/richlander/dotnet-inspect/issues/10200)).

## Gates

- **Unchanged is exact:** over the Fast Diff gate pairs and fixtures, every
  subject Exists reports `Unchanged` on an axis has no difference in the
  complete diff on that axis.
- **Changed is observable:** every subject Exists reports `Changed` has at
  least one difference in the complete diff on that axis.
- **Coverage:** for each fixture Type, its rung-1 bit equals the OR of its
  rung-2 bits, and each Member's rung-2 bit equals whether its rung-3 row and
  its rung-4 diff are non-empty on that axis.
- **Expansive:** fixtures with a hidden public Member, a private field, and a
  private method show each one's difference on its axis, with its facets.
- **No fallback:** each Exists measurement reports zero complete-model builds
  on the gate pairs once adoption step 2 completes.
- **Latency:** the latency bar, measured on NativeAOT.

## Non-claims

- It does not define a version window, its projection, or its steps.
- It does not define any fact's equality; the axes' owners do.
- It does not define a host's filters, defaults, or visual design.
- It does not define the QuerySpace operators a diff may later use; that is
  [#10227](https://github.com/richlander/dotnet-inspect/issues/10227).
