# Inspect Web implementation profiles

## Status

This document is the normative owner for the Inspect Web implementation-profile
experience in [issue #7250](https://github.com/richlander/dotnet-inspect/issues/7250).
It owns Browser acquisition, result caching, overload-family projection,
member-list presentation, and visible state transitions.

The host-neutral family analysis and public-API correspondence are owned by
`ImplementationProfileFamilyInspectionOperation` and
`AssemblyContextImplementationProfileFamilyQuery`. This design consumes their
completed `InspectionEnvelope<TContent>` without redefining exact family
selection, implementation metrics, overload relationships, generated-body
correspondence, API identity, participant selection, or diagnostics.

Implementation evidence is part of the member list, where the reader already
compares overloads; the separate Implementation Profiles Member section is
retired. This revision acquires member-list evidence once per Type rather than
once per expanded family, because Analysis setup cost dominates a family-sized
request. It replaces the earlier analyzed-family record on the family result,
which no consumer reads under this design.

## Claim

When the member list shows an overloaded method family, its nested overload
rows show which overloads carry the most code and which overloads are hubs the
others call. Two channels carry that claim:

- **Heat**: a tint whose strength follows each overload's instruction count
  relative to the largest same-name method body in its family, including
  non-public methods.
- **Hub strip**: a marker on an overload that same-name methods call and that
  calls no same-name method itself.

Heat is a family-relative size cue, not a complexity score. The hub strip is
derived only from owner-issued call relationships, never from names,
signatures, or size.

The experience preserves owner-issued Member anchors, physical body identity,
raw measurements, exact sibling-call relationships, coverage, incompleteness,
and diagnostics. It never treats a failed or incomplete inspection as a
successful empty family.

## Adjacent owners

This owner composes existing contracts by their issued currencies:

- `docs/inspection-space.md` owns the selected-participant inspection and
  completed `InspectionEnvelope<TContent>` handoff.
- `docs/design/inspect-web-jsexport-partitioning.md` assigns implementation
  profiles to the Analysis facade.
- `docs/design/ts-jsexport.md` owns source-generated C#-to-TypeScript wire
  contracts and facade generation.
- `docs/design/inspect-web-managed-operation-bridge.md` owns managed operation
  admission, settlement, callback lifetime, cancellation, and quiescence.
- `docs/design/inspect-web-operation-authority.md` owns current-view operation
  identity, replacement, stale-publication suppression, and quiescence.
- `docs/design/progressive-disclosure.md` owns the requirement that unbounded
  implementation analysis remain explicit rather than default navigation work.
- `docs/design/cli-member-implementation-profiles.md` owns the CLI
  `Member Metrics` consumer of the family operation. Its contract does not
  change.
- [issue #8577](https://github.com/richlander/dotnet-inspect/issues/8577) owns
  body-traversal breadth pushdown and cooperative Browser execution.

This design transfers one claim to Queries: a **Type heat query** that, for one
participant and one Type definition ID, analyzes every eligible overload
family on that Type in a single Analysis execution and issues the compact
**Type heat record** defined below. The family query and its result are
unchanged. The Analysis facade lowers the completed host-neutral envelope to an
assembly-local generated wire contract. The Browser joins only owner-issued
method tokens, module identities, Type definition IDs, stable Member
selectors, and exact Library coordinates.

## Supported scope

The production experience is the member list for a Type in a package
implementation asset or platform implementation assembly. Uploaded standalone
Libraries are outside this slice because they do not yet participate in the
same retained Browser workspace and Analysis facade path.

An **eligible family** is a public API group on the Type whose member kind is
`method` and that has at least two public overloads. Extension-method families
that the API surface attaches to an extended Type are not eligible: their
declaring static class can also declare same-name extensions for other
receivers, and whether those belong in the family is undecided. That decision
precedes their eligibility.

For each eligible family, two sets are distinct:

- the **public roster** is the listed overloads; only these are rows; and
- the **analyzed family** is every same-name method declared on the TypeDef
  that declares each roster member, regardless of accessibility. For an
  ordinary method family that is the Type itself.

Heat and hub derivation read only the analyzed family. Non-public methods are
never rows and never show heat or a hub strip, but they set the family maximum
and take part in call relationships. A public overload is therefore not
presented as large when a non-public implementation dwarfs it, and a public
forwarder into a non-public method is never a hub.

Readers take sparse walks through an assembly. The Type the reader selected is
the aggregation scope; this design does not analyze a whole Library by
default, does not widen a Type request in the background, and does not add a
Worker-resident analysis store. Library- and Type-level ranked lists remain
future consumers that require their own proportional query contracts.

## Activation and acquisition

The member list is built in two passes:

1. The Browser loads and paints the Type's members from the API surface.
   Package acquisition, API loading, Type navigation, and this first paint
   issue no implementation-profile request.
2. After that paint, when the Type has at least one eligible family and no
   cached heat state, the Browser requests the Type heat record once. Expanding
   a family then reads heat from that record; it issues no request.

Selecting a Type is the explicit gesture that scopes the work. The request is
bounded by that Type's eligible families and their analyzed same-name methods,
so it is not the unbounded implementation analysis that
[progressive disclosure](progressive-disclosure.md) reserves for explicit
requests.

Selecting a concrete overload requests that overload's family detail through
the unchanged family query, for the evidence in the Member detail. That request
remains explicit and per family.

The engine Worker is single-threaded. Heat analysis is synchronous managed CPU
work in the same ordinary Worker as interactive requests such as Member
declaration, Type projection, Member facts, and source. It does not block the
page, but an interactive request that arrives during a heat run waits for the
run to finish, and cancellation cannot interrupt it. The Browser bounds this:

- a Type heat request is sent only when no interactive ordinary-Worker request
  from the current view is outstanding; an interactive request that arrives
  before the heat request starts goes first;
- at most one Type heat request runs; a queued request for a Type the reader
  has left is dropped, and a running request finishes and settles its cache
  entry but publishes only into the view that still owns it; and
- a producer-failed Type is not retried by navigation. Retry is explicit.

Completion uses the existing operation-authority `started` and `terminal`
events. This revision issues no progress events and adds no separate event
sink. Making each run short or yielding, so that interactive work can
interleave, belongs to issue #8577.

One Type heat request names one exact implementation participant and Type:

- a package request uses package ID, package version, target framework, and
  the exact selected implementation Library asset identity;
- a platform request uses framework, platform version, platform pack, and
  assembly file name; and
- both routes carry one metadata Type definition ID. The query derives the
  eligible families and their analyzed families from the participant's public
  API surface and metadata.

Unknown, ambiguous, or non-public Type selection fails visibly and never
widens to whole-Library analysis. A Type with no eligible family completes as
an available record with no families.

## Type heat record

The Type heat query issues, inside the completed `InspectionEnvelope`, one
record per eligible family. Each family record carries:

- the family's public Member anchors in roster order: Type definition ID,
  stable selector, and the owner-issued logical method token;
- for each analyzed method: metadata token, whether it is public, whether it
  was declared with a body, its size, and whether its measurement is complete;
- the family's same-name call relationships: caller and callee tokens; and
- the family's coverage receipt: unavailable bodies and Analysis diagnostics
  that fall inside the analyzed family.

The record carries no raw metric set, physical-body breakdown, or IL offsets.
Those remain in the family detail result. The record also carries the envelope
outcome, Share outcome, and ordered diagnostics.

## Browser wire identity

The Analysis facade publishes source-generated assembly-local records rather
than process-local query objects or untyped JSON shortcuts. The wire shape may
normalize repeated identities, but it preserves these exact join currencies:

- method identity: module version ID plus metadata token;
- public Member identity: Type definition ID plus stable selector;
- overload relationships: caller and callee method identity, and, for the
  family detail result, evidence-body identity, IL offset, and call kind; and
- subject identity: assembly name, version, culture, and public-key token.

Method names, declaring Types, parameter Types, return Types, and generated
framework Types are carried as host-produced display strings. Browser logic
does not parse those strings to recover identity.

The family detail wire includes every raw `MethodImplementationProfile`
measurement: IL bytes, instruction count, distinct opcode count, basic blocks,
branches, conditional branches, switches, switch targets, normal-flow
cyclomatic complexity, loops, exception-region counts, locals, direct calls,
distinct callees, allocations, throws, unsafe, Reflection, async evidence,
incoming and outgoing sibling-overload counts, and completeness with its
reasons.

## Cache and publication authority

The existing implementation-profile result cache holds both results. A Type
heat entry's key is the exact participant, workspace generation, and Type
definition ID. A family detail entry's key is the exact participant,
workspace generation, Type definition ID, and canonicalized complete selector
set. A successful, empty, incomplete, rejected, or failed settled result
belongs only to its key, and an in-flight request is single-flight for its
key.

Returning to a Type reuses its cached heat entry without a request. A transport
or producer failure is retained as a terminal failed state so navigation does
not retry expensive work implicitly; the Retry action explicitly replaces that
entry. Changing package, platform, framework, version, implementation Library,
workspace generation, or Type creates a different key and cannot consume the
previous entry.

Replacing the active view creates a new operation-authority operation. A
request may still settle its cache entry, but only the current operation may
publish into the active view. Cancellation detaches the replaced view from its
waiter; it does not convert a valid shared result into cancellation.

## Family projection

The Browser joins the Type heat record to the member list by Type definition
ID and stable selector; it never matches by rendered signature text.

Each overload has one **size**: the instruction count of its own logical body.
Generated bodies remain separate evidence in the family detail result; the
largest of them stands in for size only when the logical body has no profile
of its own.

The **family maximum** is the largest size in the analyzed family, including
non-public methods. A method declared without a body, such as an abstract or
extern method, has no size and does not affect the maximum; it is recorded as
declared without a body, not as an unavailable body. When the family's
coverage has an unavailable body or an incomplete measurement, the maximum is
unknown and the family shows no heat.

An overload is a **hub** when all of the following hold:

- at least one relationship has another analyzed-family method as its caller
  and this overload as its callee;
- no relationship has this overload as its caller and another analyzed-family
  method as its callee; and
- the overload's own measurement is complete.

Hub state is shown for an overload whose own measurement is complete even when
another analyzed body is incomplete: a missing relationship from an incomplete
body can only withhold a hub strip, never add one.

The member list keeps public API roster order. Heat and hub state annotate
rows; they never reorder them.

## Presentation

### Overload rows

The member navigation list shows an expanded family as its parent member row
followed by nested overload rows. Selecting an overloaded method is the
expansion. Heat and the hub strip annotate the nested rows; the parent row
carries family-level status text.

The target nested-row label is the member name and its parameter types in C#
spelling without namespace qualification, for example
`Parse(ReadOnlySequence<byte>, JsonDocumentOptions)`. That compact label is
owned by a separate member-surface-producer design; the Browser does not
derive it by editing the rendered signature. Until the producer issues it,
nested rows keep the existing signature text.

### Heat

Heat applies only to overloads whose size is at least half the family
maximum. Every other row is untinted.

- The tint is anchored on the right edge of the row and fades toward the left,
  leaving the left edge for hover, selection, and the hub strip.
- Strength follows `t = sqrt(size / family maximum)`. Lightness, chroma, and
  the distance the tint reaches all rise with `t`; the reach is at most 75% of
  the row width, so reach is a non-color channel for the same fact.
- The tint is one hue from a theme-owned token ramp, distinct from the
  selection accent and deliberately low in chroma. Each theme defines its own
  ramp endpoints; the tint fades to the same hue at zero alpha so hover and
  selection backgrounds remain visible under it.

The Browser shows no heat when comparison would add noise: fewer than two
measured bodies in the analyzed family, or every analyzed body has at most
eight instructions with no branches, loops, exception regions, unsafe
evidence, or Reflection evidence.

### Hub strip

A hub overload shows a narrow strip in the row's left gutter using a
theme-owned hub token. Heat and the hub strip are independent: a hub may be
untinted and a heated overload may not be a hub. A family may have neither;
for example, every public `JsonDocument.Parse` overload is smaller than half
of its non-public implementation and calls a same-name method.

### Parent-row status text

The expanded family's parent row shows dim status text after its overload
count:

| State | Text | Token |
| --- | --- | --- |
| Type heat request outstanding | `measuring` | theme success token |
| Heat unknown for this family (unavailable or incomplete evidence) | `heat incomplete` | theme error token |
| Type request rejected, failed, unavailable, or producer failed | `heat unavailable` | theme error token |
| Heat available or suppressed as noise | none | — |

### Accessible description

Each nested row with heat or a hub strip carries an accessible description:
its instruction count; its share of the family maximum and whether that
maximum belongs to a non-public method; and, for a hub, how many same-name
methods call it. Neither channel relies on color alone.

### Detail

Selecting an overload shows its implementation evidence in the Member detail,
from the family detail result: instruction count, structural cues, the exact
sibling relationships it makes and receives within the public roster, and
progressive disclosure of every raw metric, incomplete reason, and
unavailable-body receipt. The detail restates the overload's heat description
from the Type heat record.

The presentation uses host-specific HTML and CSS rather than Markout. The
member list is an interactive Browser view; no current CLI or multi-format
consumer needs this rendering. The structured wire contract, rather than
rendered HTML, is the reusable boundary.

## Visible states

For the Type heat record:

- **idle**: no eligible family, or the member list has not painted;
- **loading**: the Type request is queued or running; the expanded family's
  parent row shows `measuring`;
- **ready**: heat and hub state are shown for families whose evidence is
  complete;
- **ready but incomplete** for a family: no heat for that family, hub strips
  only for overloads whose own measurement is complete, and `heat incomplete`
  on its parent row;
- **rejected, failed, or unavailable Content**, or **producer failed**: no heat
  or hub strip, `heat unavailable` on the parent row, and the owner-issued
  outcome, diagnostics, and an explicit Retry in the Member detail; and
- **superseded**: no state is published because operation authority removed
  the view's publication right.

The family detail result keeps its existing loading, ready, incomplete, empty,
rejected, failed, producer-failed, and superseded states in the Member detail.

## Real evidence

Subjects:

- `System.Private.CoreLib` from `Microsoft.NETCore.App` 11.0.0-rc.1, `net11.0`:
  `System.Text.StringBuilder.AppendFormat` has 15 public overloads and one
  367-instruction hub.
- `System.Text.Json` 10.0.5, `net10.0`:
  - `JsonDocument.Parse`: 5 public overloads measuring 55, 44, 33, 9, and 8
    instructions; the non-public `Parse(ReadOnlySpan<byte>, JsonReaderOptions,
    ref MetadataDb, ref StackRowStack)` measures 288; neither channel shows;
  - `Utf8JsonWriter.WriteString`: 28 public overloads, largest body 17
    instructions, 8 hubs; and
  - `JsonSerializer.Serialize`: all-forwarder overloads with a largest body of
    16 instructions.

Reproduce sizes with
`dotnet-inspect member <Type> <Member> --package System.Text.Json@10.0.5
-S "Member Metrics" --all`, or `--platform System.Private.CoreLib`.

Measurements that shaped this revision, observational rather than thresholds:

- One warm Analysis execution on CoreLib cost about 0.4 s whether scoped to one
  family (16 bodies) or every overloaded family on `StringBuilder` (110
  bodies); on System.Text.Json, 65 ms for one family and 124 ms for every
  overloaded family on `Utf8JsonWriter` (329 bodies). Whole-assembly setup
  dominates, which is issue #8577's Level 2 test case.
- Whole-Library analysis of CoreLib cost 1.4 to 2.4 s with a 300 to 500 MB
  transient managed heap (CoreCLR, warm); that cost is why a Type, not a
  Library, is the aggregation scope.
- In the published Browser on an idle x86 Linux host, a family request settled
  in about 8 s on first use in a session, about 1.5 s warm, and 50 to 120 ms
  from cache; overload rows painted within about 100 ms in every case.

Deterministic synthetic fixture coverage remains responsible for rejected,
failed, incomplete, bodyless, generated-body, retry, and stale-publication
boundaries.

## Gates

The following gates enforce this design:

1. Type heat query tests prove, on the System.Text.Json real asset, that
   `JsonDocument.Parse` records its non-public implementation as the family
   maximum, that `Utf8JsonWriter.WriteString` records its hubs from same-name
   relationships, that ineligible and attached extension families are absent,
   and that one Analysis execution serves every family on the Type; and that
   the family query and CLI `Member Metrics` gates pass unchanged.
2. Analysis-facade projection tests compare Type heat and family detail wire
   results with their completed host-neutral envelopes, including outcome,
   identities, sizes, relationships, coverage, Share, and ordered diagnostics.
3. Generated-facade ownership and ordinary Worker tests prove the complete
   typed results cross the Analysis facade and Worker transport unchanged.
4. Coordinator tests prove no request before the member list's first paint,
   one Type heat request per Type, no request on family expansion, deferral
   while an interactive request is outstanding, at most one heat request
   running with queued requests for departed Types dropped, cache reuse on
   return, explicit retry, workspace replacement, and stale-publication
   suppression.
5. Family-projection tests prove size, the family maximum, the
   half-of-maximum heat threshold, noise suppression, hub derivation, and
   unknown-maximum handling over the real-asset families plus synthetic
   incomplete, bodyless, generated-body, and non-public-callee boundaries.
6. Member-list rendering and accessibility tests prove roster order,
   right-anchored heat with at most 75% reach, the hub strip, parent-row status
   text and tokens, accessible descriptions, and every visible state.
7. The Inspect Web authored typecheck, lint, `knip`, build, and focused Browser
   tests gate the production composition; Browser tests run against a fresh
   build.
8. The PR Demo records, in the published Browser, the time from Type selection
   to heat for the real-evidence Types and confirms through instrumentation
   that package acquisition, API loading, first paint, and family expansion
   issue no heat request and that interactive requests are not queued behind
   an unstarted heat request. Timing is observational evidence, not a stable
   CI threshold.
