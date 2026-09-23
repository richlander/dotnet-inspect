# Inspect Web implementation profiles

## Status

This document is the normative owner for the Inspect Web implementation-profile
experience in [issue #7250](https://github.com/richlander/dotnet-inspect/issues/7250).
It owns Browser acquisition, family-result caching, overload-family
projection, interactive presentation, and visible state transitions.

The host-neutral analysis and public-API correspondence are already owned by
`ImplementationProfileFamilyInspectionOperation` and
`AssemblyContextImplementationProfileFamilyQuery`. This design consumes their
completed `InspectionEnvelope<TContent>` without redefining exact family
selection, implementation metrics, overload relationships, generated-body
correspondence, API identity, participant selection, or diagnostics.

## Claim

An explicit Implementation Profiles gesture on an overloaded Member family
loads one exact family inspection and presents family-relative implementation
magnitude without claiming a universal complexity score or an inferred
overload role. The Browser does not analyze or transport unrelated methods in
the same Library.

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

This design does not change any adjacent contract. The Analysis facade lowers
the completed host-neutral envelope to an assembly-local generated wire
contract. The Browser joins only owner-issued method tokens, module identities,
Type definition IDs, stable Member selectors, and exact Library coordinates.

## Supported scope

The first production experience is a Member section for a public API group with
multiple method overloads. It supports package implementation assets and
platform implementation assemblies. Uploaded standalone Libraries are outside
this slice because they do not yet participate in the same retained Browser
workspace and Analysis facade path.

Library- and Type-level ranked lists remain future consumers that require their
own proportional query contracts. This slice does not add those surfaces and
does not make their acquisition, ordering, or filtering normative.

## Activation and acquisition

Ordinary package acquisition, API loading, Type navigation, Member navigation,
overload selection, and first paint perform no implementation-profile work.
The Browser starts acquisition only after the user selects the Implementation
Profiles Member section or explicitly retries that section.

One request names one exact implementation participant and public overload
family:

- a package request uses package ID, package version, target framework, and the
  exact selected implementation Library asset identity;
- a platform request uses framework, platform version, platform pack, and
  assembly file name; and
- both routes carry one metadata Type definition ID and the complete set of
  stable Member selectors for the public method family.

The Analysis JSExport operation opens that participant and invokes
`ImplementationProfileFamilyInspectionOperation`. Unknown, duplicate, partial,
cross-family, or non-method selection fails visibly and never widens to
whole-Library analysis. The wire result preserves the completed envelope:

- Content outcome: available, participant rejected, or participant failed;
- available Content: subject identity, the complete public overload roster,
  profiles, public Member anchors, scoped coverage, contained overload
  relationships, generated framework Types, Analysis diagnostics, and
  API-surface failures;
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

The family is ordered by each overload's largest physical instruction count,
descending. Ties preserve the public API roster order. Physical rows within an
overload use the same descending order. Missing-body rows follow measured
overloads in public API order.

An overload with no attributed physical profile remains visible in the family
roster as a no-body or unavailable row. A successful available Library
inspection with no profile for any family member is a successful empty family
only when coverage and diagnostics do not indicate that the absence may be
incomplete.

## Presentation

Implementation Profiles is a family-level Member section. It is available for
eligible package and platform overload groups before an individual overload is
selected. Selecting a concrete overload may highlight that overload but does
not narrow the family result.

The primary visual cue is physical instruction count normalized only against
the largest physical body in the selected family. It is labeled in text and
does not rely on color. Distinct opcode count and structural facts are separate
textual channels rather than ingredients in a hidden scalar score.

Each physical row provides:

- a relative instruction-count bar and numeric instruction count;
- logical versus physical identity when they differ;
- compact badges for branches, loops, exception regions, async, unsafe, and
  Reflection;
- exact incoming and outgoing sibling-overload relationship counts; and
- progressive disclosure of every raw metric, incomplete reason, and matching
  exact relationship.

The Browser omits relative bars when comparison would add noise: fewer than two
physical rows, or every row has at most eight instructions with no branches,
loops, exception regions, unsafe evidence, or Reflection evidence. The raw
measurements remain available.

The presentation uses host-specific HTML and CSS rather than Markout. The
section is an interactive, replaceable Browser view with loading, retry,
selection highlighting, and disclosure controls; no current CLI or
multi-format consumer needs this rendering. The structured wire contract,
rather than rendered HTML, is the reusable boundary.

The UI does not label an overload as primary, core, adapter, forwarding,
complex, risky, or severe. It does not compare values across Libraries and does
not treat the relative bar as an intrinsic score.

## Visible states

The section distinguishes:

- **loading**: an exact family request is in flight;
- **ready**: all displayed rows are complete and no relevant coverage failure
  is present;
- **ready but incomplete**: available rows are displayed with profile,
  coverage, Analysis, API-surface, or envelope diagnostics;
- **empty**: the completed available inspection has no applicable physical
  profiles and no evidence that the absence is incomplete;
- **rejected or failed Content**: the owner-issued participant outcome and
  diagnostics are shown;
- **producer failed**: the Worker or facade call failed and an explicit Retry
  action is shown; and
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

## Gates

The following gates enforce this design:

1. Analysis-facade projection tests compare package and platform wire results
   with the completed host-neutral envelope, including Content outcome, raw
   metrics, logical and physical tokens, public anchors, coverage,
   relationships, Share, and ordered diagnostics.
2. Generated-facade ownership and ordinary Worker tests prove the complete
   typed result crosses the Analysis facade and Worker transport unchanged.
3. Implementation-profile coordinator tests prove explicit activation,
   exact-family-key single-flight caching, same-family reuse, cross-family
   isolation, explicit retry, workspace replacement, and stale-publication
   suppression through operation authority.
4. Member composition, navigation, and vocabulary-exhaustiveness tests prove
   the section is available for eligible package and platform overload groups,
   works before overload selection, and restores through navigation state.
5. Rendering and accessibility tests prove family-only normalization,
   non-color labels, uniform-small-family suppression, raw metric disclosure,
   generated-body separation, incomplete and empty distinctions, and retry
   focus behavior.
6. The Inspect Web authored typecheck, lint, build, and focused Browser tests
   gate the production composition.
7. The PR Demo records first-acquisition latency and confirms through
   instrumentation that package acquisition, Type navigation, Member
   navigation, and overload selection issue no profile request before the
   explicit section gesture. Timing is observational evidence, not a stable CI
   threshold.
