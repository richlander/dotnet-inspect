# Inspect Web implementation profiles

## Status

This document is the normative owner for the Inspect Web implementation-profile
experience in [issue #7250](https://github.com/richlander/dotnet-inspect/issues/7250).
It owns Browser acquisition, family-result caching, overload-family
projection, member-list presentation, and visible state transitions.

The host-neutral analysis and public-API correspondence are already owned by
`ImplementationProfileFamilyInspectionOperation` and
`AssemblyContextImplementationProfileFamilyQuery`. This design consumes their
completed `InspectionEnvelope<TContent>` without redefining exact family
selection, implementation metrics, overload relationships, generated-body
correspondence, API identity, participant selection, or diagnostics.

This revision replaces the separate Implementation Profiles Member section.
Implementation evidence is now part of the member list, where the reader
already compares overloads.

## Claim

When the member list expands an overloaded method family, it shows which
overloads carry the most code and which overload is the hub the others call.
Two channels carry that claim:

- **Heat**: a tint whose strength follows each overload's instruction count
  relative to the largest same-name method body on the declaring Type,
  including non-public methods.
- **Hub strip**: a marker on an overload that sibling overloads call and that
  calls no same-name method itself.

Heat is a family-relative size cue, not a complexity score. The hub strip is
derived only from owner-issued call relationships, never from names,
signatures, or size. The Browser does not analyze or transport unrelated
methods in the same Library.

The experience preserves the owner-issued logical Member anchor, physical body
identity, raw measurements, exact sibling-call relationships, coverage,
incompleteness, Share outcome, and diagnostics. It never treats a failed or
incomplete inspection as a successful empty family.

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

This design transfers one claim to the family query: the analyzed family is
every same-name method declared on the Type, regardless of accessibility,
while the public roster stays the set of listed Members. It changes no other
adjacent contract. The Analysis facade lowers the completed host-neutral
envelope to an assembly-local generated wire contract. The Browser joins only
owner-issued method tokens, module identities, Type definition IDs, stable
Member selectors, and exact Library coordinates.

## Supported scope

The production experience is the member list's expanded overload rows for a
public API group with multiple method overloads. It supports package
implementation assets and platform implementation assemblies. Uploaded
standalone Libraries are outside this slice because they do not yet
participate in the same retained Browser workspace and Analysis facade path.

Two sets are distinct:

- the **public roster** is the listed overloads; only these are rows; and
- the **analyzed family** is every same-name method declared on the Type,
  regardless of accessibility.

Profiles, sizes, and relationships cover the analyzed family. Non-public
methods are never rows and never show heat or a hub strip, but they set the
family maximum and take part in call relationships. A public overload is
therefore not presented as large when a non-public implementation dwarfs it,
and a public forwarder into a non-public method is never a hub.

Library- and Type-level ranked lists remain future consumers that require their
own proportional query contracts. This slice does not add those surfaces and
does not make their acquisition, ordering, or filtering normative.

## Activation and acquisition

The member list is built in two passes:

1. The Browser loads and paints the Type's members from the API surface. This
   pass performs no implementation-profile work, and package acquisition, API
   loading, and Type navigation remain free of it.
2. After the member list paints, the Browser requests implementation profiles
   for each eligible overload family on the Type and annotates rows as each
   family result publishes.

Each request is family-scoped and bounded by the analyzed family, so it is not
the unbounded implementation analysis that
[progressive disclosure](progressive-disclosure.md) reserves for explicit
requests. The Browser bounds the second pass:

- at most one family request is in flight;
- the currently expanded family is requested first, and the remaining eligible
  families follow in member-list order;
- leaving the Type stops queuing its remaining families; an in-flight request
  may settle its cache entry but does not publish into another Type's list;
- rows render immediately without heat, and heat appears when the family's
  result publishes; and
- a producer-failed family is not retried by navigation. Retry is explicit.

One request names one exact implementation participant and family:

- a package request uses package ID, package version, target framework, and the
  exact selected implementation Library asset identity;
- a platform request uses framework, platform version, platform pack, and
  assembly file name; and
- both routes carry one metadata Type definition ID, the method name, and the
  complete set of stable Member selectors for the public roster.

The Analysis JSExport operation opens that participant and invokes
`ImplementationProfileFamilyInspectionOperation`. Unknown, duplicate, partial,
cross-family, or non-method selection fails visibly and never widens to
whole-Library analysis. The wire result preserves the completed envelope:

- Content outcome: available, participant rejected, or participant failed;
- available Content: subject identity, the complete public overload roster,
  profiles for the analyzed family, public Member anchors, scoped coverage,
  contained overload relationships, generated framework Types, Analysis
  diagnostics, and API-surface failures;
- Share outcome; and
- ordered inspection diagnostics.

Compile-Library unavailability before participant selection remains a distinct
Browser outcome with its existing typed availability receipt. It is not
represented as an empty profile inspection.

## Browser wire identity

The Analysis facade publishes source-generated assembly-local records rather
than process-local query objects or untyped JSON shortcuts. The wire shape may
normalize repeated identities, but it preserves these exact join currencies:

- physical and logical method identity: module version ID plus metadata token;
- public Member identity: Type definition ID plus stable selector;
- physical evidence membership: body-token arrays issued by the query;
- overload relationships: caller, callee, evidence-body identity, IL offset,
  and call kind; and
- subject identity: assembly name, version, culture, and public-key token.

Method names, declaring Types, parameter Types, return Types, and generated
framework Types are carried as host-produced display strings. Browser logic
does not parse those strings to recover identity.

The wire includes every raw `MethodImplementationProfile` measurement:

- IL bytes, instruction count, distinct opcode count, and basic blocks;
- branches, conditional branches, switches, switch targets, and normal-flow
  cyclomatic complexity;
- loops and exception-region counts;
- locals, direct calls, and distinct callees;
- allocations, throws, unsafe, Reflection, and async evidence;
- incoming and outgoing sibling-overload counts; and
- completeness and incomplete reasons.

## Cache and publication authority

The cache key is the exact participant and family request described above,
including Type definition ID and a canonicalized complete selector set. A
successful, empty, incomplete, rejected, or failed settled result belongs only
to that key. An in-flight request is single-flight for its key.

Successful results and owner-issued non-success Content outcomes are retained
for the exact family in the active package or platform workspace. Another
family in the same Library has a different key and cannot consume that result.
A transport or producer failure is retained as a terminal failed state so
navigation does not retry expensive work implicitly. The Retry action
explicitly replaces that failed entry.

Changing package, platform, framework, version, implementation Library, or
workspace generation, Type definition ID, or selector set creates a different
key and cannot consume the previous entry. Replacing the active Member-family
view creates a new operation-authority operation. The exact family request may
still settle its cache entry, but only the current operation may publish into
the active view.

Cancellation detaches the replaced view from its waiter. It does not convert a
valid shared family result into cancellation and does not let a late result
publish into another Member family.

## Family projection

The selected API group supplies the request roster. The completed family result
returns the same owner-issued Type definition ID, stable selectors, and body
tokens, including bodyless overloads. The Browser requires that exact identity
set before joining display state; it never matches by rendered signature text.

Each logical overload retains every attributed physical profile. Generated and
async bodies are grouped under their logical overload but remain separate,
named physical evidence rows. The family operation excludes unrelated and
unattributed Library profiles before transport.

Each logical overload has one **size**: the largest instruction count among
its attributed physical profiles. Generated bodies contribute to their logical
overload's evidence but do not replace the logical body's own measurement when
that body is available.

The **family maximum** is the largest size in the analyzed family, including
non-public methods. When any analyzed body's size is unavailable or incomplete,
the family is ready but incomplete and shows no heat, because the maximum is
unknown.

An overload is a **hub** when all of the following hold:

- at least one owner-issued relationship has another analyzed-family method as
  its caller and this overload as its callee;
- no owner-issued relationship has any of this overload's physical bodies as
  its caller and another analyzed-family method as its callee; and
- every attributed physical profile is complete.

The member list keeps public API roster order. Heat and hub state annotate
rows; they never reorder them.

An overload with no attributed physical profile remains visible in the family
roster as a no-body or unavailable row. A successful available Library
inspection with no profile for any family member is a successful empty family
only when coverage and diagnostics do not indicate that the absence may be
incomplete.

## Presentation

### Overload rows

An expanded overload row shows the member name and its parameter types in
C# spelling without namespace qualification, for example
`Parse(ReadOnlySequence<byte>, JsonDocumentOptions)`. It omits the return
type and parameter names; the parent row and the Member detail carry those.

The Browser does not derive the compact label by editing the rendered
signature. The member surface producer issues it from the same metadata
signature that produces the full signature, so identity and display stay
separate.

### Heat

Heat applies only to overloads whose size is at least half the family
maximum. Every other row is untinted.

- The tint is anchored on the right edge of the row and fades toward the left,
  leaving the left edge for hover and selection.
- Strength follows `t = sqrt(size / family maximum)`. Lightness, chroma,
  and the distance the tint reaches into the row all rise with `t`, so reach
  is a non-color channel for the same fact.
- The tint is one hue from a theme-owned token ramp, distinct from the
  selection accent. Each theme defines its own ramp endpoints; the tint fades
  to the same hue at zero alpha rather than to a painted surface, so hover and
  selection backgrounds remain visible under it.

The Browser shows no heat when comparison would add noise: fewer than two
measured bodies, or every body has at most eight instructions with no
branches, loops, exception regions, unsafe evidence, or Reflection evidence.

### Hub strip

A hub overload shows a narrow strip in the row's left gutter, using a
theme-owned hub token. Heat and the hub strip are independent: a hub may be
untinted and a heated overload may not be a hub. A family may have neither;
for example, every public `JsonDocument.Parse` overload forwards to a
non-public method and is smaller than half of the non-public implementation.

### Accessible description

Each heated or hub row carries an accessible description with its instruction
count, its share of the family maximum, whether that maximum belongs to a
non-public method, and, for a hub, how many
sibling overloads call it. Neither channel relies on color alone.

### Detail

Selecting an overload shows its implementation evidence in the Member detail:
instruction count, the exact sibling relationships it makes and receives,
structural badges, and progressive disclosure of every raw metric, incomplete
reason, and unavailable-body receipt. The separate Implementation Profiles
Member section is retired.

The presentation uses host-specific HTML and CSS rather than Markout. The
member list is an interactive Browser view; no current CLI or multi-format
consumer needs this rendering. The structured wire contract, rather than
rendered HTML, is the reusable boundary.

## Visible states

The expanded family distinguishes:

- **loading**: an exact family request is in flight or queued; rows render
  without heat and the parent row shows a quiet progress cue;
- **ready**: heat and hub state are shown and no relevant coverage failure is
  present;
- **ready but incomplete**: heat and hub state are shown only for overloads
  whose evidence is complete; the parent row marks the family as incomplete,
  and the Member detail lists profile, coverage, Analysis, API-surface, or
  envelope diagnostics;
- **empty**: the completed available inspection has no applicable physical
  profiles and no evidence that the absence is incomplete;
- **rejected or failed Content**: the parent row marks heat as unavailable and
  the Member detail shows the owner-issued participant outcome and
  diagnostics;
- **producer failed**: the parent row marks heat as unavailable and the Member
  detail shows an explicit Retry action; and
- **superseded**: no state is published because operation authority removed the
  view's publication right.

## Real evidence

The primary production scenario is:

- framework `net11.0`;
- platform `Microsoft.NETCore.App` version
  `11.0.0-rc.1.26425.128`;
- pack `Microsoft.NETCore.App`;
- assembly `System.Private.CoreLib.dll`; and
- overload family `System.Text.StringBuilder.AppendFormat`.

It demonstrates a platform Library, several public overloads, one-line
delegation, and a substantive implementation in the same family.

The neighboring thin async-family scenario is:

- package `System.Text.Json` version `10.0.5`;
- framework `net10.0`;
- implementation Library `System.Text.Json.dll`; and
- `System.Text.Json.JsonSerializer.SerializeAsync`.

This family is intentionally neighboring rather than another generated-body
showcase: its ten async-named overloads have ten physical profiles and no
separate generated evidence bodies. It demonstrates that the Browser reports
issued evidence rather than inferring state-machine bodies from names or
signatures. `StringBuilder.AppendFormat` itself includes a separately attributed
generated/local-function body. Deterministic synthetic fixture coverage remains
responsible for rejected, failed, incomplete, bodyless, generated-body, retry,
and stale-publication boundaries.

Production-boundary measurements on .NET 11 RC1 and System.Text.Json 10.0.5
recorded:

- `StringBuilder.AppendFormat`: 15 logical overloads, 16 physical profiles,
  10 overload relationships, 7.35 seconds in a fresh platform-export process,
  and 29,409 serialized bytes;
- `JsonSerializer.SerializeAsync`: 10 logical overloads, 10 physical profiles,
  no overload relationships, 0.36 seconds in a fresh package-export process,
  and 19,510 serialized bytes.

The platform result is therefore bounded to the requested family rather than
the previous 64,716,704-byte whole-Library payload. The latency remains behind
explicit activation and a visible loading state; ordinary package, Type,
Member, and overload navigation does not start this operation.

`System.Text.Json.JsonDocument.Parse` in package `System.Text.Json` version
`10.0.5` is the no-hub scenario. Its five public overloads measure 55, 44, 33,
9, and 8 instructions. Every one calls a non-public same-name method, and the
non-public `Parse(ReadOnlySpan<byte>, JsonReaderOptions, ref MetadataDb, ref
StackRowStack)` measures 288 instructions. No public overload reaches half the
family maximum and none is a hub, so the family shows neither channel.

## Gates

The following gates enforce this design:

1. Family-query tests prove that the analyzed family for `JsonDocument.Parse`
   includes its non-public same-name methods with profiles and relationships,
   and that they never enter the public roster.
2. Analysis-facade projection tests compare package and platform wire results
   with the completed host-neutral envelope, including Content outcome, raw
   metrics, logical and physical tokens, public anchors, coverage,
   relationships, Share, and ordered diagnostics.
3. Generated-facade ownership and ordinary Worker tests prove the complete
   typed result crosses the Analysis facade and Worker transport unchanged.
4. Implementation-profile coordinator tests prove the second pass starts only
   after the member list paints, the expanded family is requested first, at
   most one request is in flight, leaving the Type stops queuing,
   exact-family-key single-flight caching, same-family reuse, cross-family
   isolation, explicit retry, workspace replacement, and stale-publication
   suppression through operation authority.
5. Family-projection tests prove size, the family maximum, the
   half-of-maximum heat threshold, noise suppression, and hub derivation
   over `StringBuilder.AppendFormat` and `JsonDocument.Parse` evidence plus
   synthetic incomplete, bodyless, generated-body, and non-public-callee
   boundaries.
6. Member-list rendering and accessibility tests prove roster order, compact
   labels from the issued display, right-anchored heat, the hub strip,
   accessible descriptions, and every visible state.
7. The Inspect Web authored typecheck, lint, build, and focused Browser tests
   gate the production composition.
8. The PR Demo records the time from member-list paint to heat for both
   real-evidence families and confirms through instrumentation that package
   acquisition, API loading, and Type navigation issue no profile request and
   that the second pass keeps at most one request in flight. Timing is
   observational evidence, not a stable CI threshold.
