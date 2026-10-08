# Fast Diff

## Status and owner

This document owns **Fast Diff**: a cheap, host-neutral pass that answers
whether a subject changed between one exact Library pair, so a host can
advertise changes before paying for a complete diff. It is tracked by
[#9716](https://github.com/richlander/dotnet-inspect/issues/9716).

dotnet-inspect builds robust, capable inspection features that provide
foundational capabilities or compelling experiences and are conventionally
sound, delightfully new or unique, or both. Fast Diff serves that mission by
making Compare responsive: users see where differences are without waiting for
every difference to be computed.

> **Claim.** For one exact Library pair and one scope, Fast Diff returns one
> state per immediate child subject: `Unchanged`, `Changed`, or
> `Indeterminate`. `Unchanged` is exact for the facts that pass compares (see
> Passes and scopes): the complete diff would report no difference in those
> facts for that subject. `Changed` and `Indeterminate`
> may over-report relative to the complete diff but never hide a change. Fast
> Diff stops at the first difference per subject, performs no decompilation, and
> never produces row-level detail.

Design basis: user direction in the Fast Diff session, "Unchanged is exact;
Changed/Maybe may over-report", canonical IL operation equality per paired
method, and a host-neutral QuerySpace `Exists` producer with Browser Compare as
first adopter.

## Why

Library-scope Compare computes a complete Library diff, and Member Body
([Member Body Diff](inspect-web-member-body-diff.md)) compares every changed
implementation, to learn which Types and Members changed. When the question is
only "did this change?", that work is wasted. Most subjects in a version pair
are unchanged, and proving that is far cheaper than describing a change.

## Passes and scopes

| Pass | Scope | Returns | Facts compared |
| --- | --- | --- | --- |
| Fast | Library | One state per Type | API (Finding transitions and compatibility classifications) and the one-sided method census; no body comparison |
| Fast | Type | One state per Member | The Library facts for that Type's Members, plus owner-attributed canonical body equality |
| Complete | Member | Full API and body diff for one exact Member | Existing [Annotated Source diff](annotated-source-diff-document.md) cost |

Each pass stops at the first sign of change per subject. The passes are tiered
by cost, not nested by result: a Library `Unchanged` says the Type has no API
or member-inventory difference and says nothing about bodies. The Library pass
does not walk method bodies because exact `Unchanged` on a Type requires every
paired body proven equal, and early exit helps only changed Types, so the body
walk costs the whole library on every unchanged Type. A raw per-side digest
experiment measured that cost independently of complete diff inventory and
serialization; [Consequences for the complete diff](#consequences-for-the-complete-diff)
records the result. Body equality is therefore a Type-pass fact, where the walk
is bounded by one Type. A host that must know whether any body changed in a
Type requests the Type pass; the producer states the facts each pass compares
so no consumer reads more into a state than it carries.

## QuerySpace execution

Fast Diff is a terminal-specialized QuerySpace query, not a projection over a
completed `ApiDiff` or shallow summary. It follows
[Open and closed queries](open-and-closed-queries.md) and
[Query space composition](query-space-composition.md):

- the Library pass issues one owner-scoped `Exists` request per Type over its
  API and method-inventory difference witnesses;
- the Type pass issues one owner-scoped `Exists` request per Member over its
  API and implementation difference witnesses;
- a successful `Exists` settles as `Changed` at the first witness, before row
  projection;
- an exhausted complete source settles as `Unchanged`; and
- a failure or incomplete source before settlement yields `Indeterminate`, not
  `Unchanged`.

The request-set planner may share a source traversal across compatible
subjects. Sharing does not turn `Exists` into Rows or Count: after one subject
settles, it stops receiving work and stops being charged while unresolved
subjects continue. A later shared-source failure does not invalidate an
already settled `Changed`; it makes only unresolved subjects
`Indeterminate`.

The producer must expose terminal-specialized difference-witness capabilities.
It must not construct a complete `AssemblyContextApiComparisonResult`,
`ApiDiff`, shallow summary, or row inventory before executing `Exists`.
Correspondence and classification remain Metadata-owned semantics, but the
Fast Diff operation consumes their per-unit producer capabilities. This is the
mechanism that makes early exit reduce execution rather than merely truncate a
completed projection.

### Raw producer boundary

Fast Diff executes over raw QuerySpace producers:

- Metadata-owned per-unit correspondence and Finding-transition producers
  supply API witnesses;
- a raw MethodDef census supplies one-sided and body-availability witnesses;
  and
- a raw canonical IL producer decodes a Member's direct body and authenticated
  generated execution bodies lazily, and stops at the first unequal operation
  or owned body fact.

Fast Diff does not route through `LibraryBodyAnalysisService`, an Analysis
method population, Research target planning, `ImplementationComparisonQuery`,
`MemberBodyDiffInspection`, a decompiler, or any complete-diff producer. Those
paths answer richer questions, require identities and evidence Fast Diff does
not consume, and prevent terminal pushdown.

Raw Metadata handles and owner-issued correspondence identify each Type,
Member, and MethodDef. Fast Diff does not round-trip a method through a textual
Member selector to recover the body it already owns. This preserves distinct
ordinary methods and conversion operators even when names such as `Explicit`
or `Implicit` overlap selector grammar. [#9713](https://github.com/richlander/dotnet-inspect/pull/9713)
corrected literal-name preservation for digest-qualified selectors; the raw
boundary is not a second workaround for that defect. It avoids unnecessary
identity recovery entirely and keeps the body producer on its existing
Metadata subject.

Decode or resolution failure is local to the unresolved subject. QuerySpace
settles that subject as `Indeterminate`; it does not fail the Library or Type
request and does not invalidate subjects already settled as `Changed` or
`Unchanged`.

Generated execution identity remains separate from Member attribution.
[State-machine relationship index](state-machine-relationship-index.md)
authenticates kickoff, state-machine, and execution MethodDefs; the Analysis
Method source supplies typed state-machine and lifted-body origins that name
their declared owner. Fast Diff folds only those owner-issued origins into the
Member implementation fact. Rejected, ambiguous, incomplete, or
limit-exhausted attribution makes that Member `Indeterminate`, never
`Unchanged`.

## Subject states and equality

| State | Meaning |
| --- | --- |
| `Unchanged` | Both sides exist and every compared fact is equal under the rules below |
| `Changed` | An owner-issued difference was found: added, removed, API difference, or implementation difference |
| `Indeterminate` | Equality could not be decided cheaply or at all (decode failure, unsupported input, work limit); the typed reason is retained |

`Indeterminate` is never reported as `Unchanged`. A host treats it as possibly
changed and may offer the Complete pass.

Equality is decided per paired subject:

- **API.** Metadata-owned Type and Member Finding transitions, including
  changed pairs with no classified `ApiChange`, and the compatibility
  classifications the complete API comparison reports (signature,
  accessibility, modifiers, constraints, and attributes). Fast Diff consumes
  those semantics through terminal-specialized QuerySpace producer
  capabilities. It does not consume
  [Library API diff presentation](library-api-diff-presentation.md), which
  admits only a completed Library comparison and would force the complete diff
  first. The presentation remains the owner of how complete results are shown;
  Fast Diff defines no second notion of API equality.
- **Implementation (Type pass only).** Each Member's implementation fact folds
  its paired direct MethodDef and every authenticated generated execution
  MethodDef attributed to that declared owner. This includes async and iterator
  state-machine execution, lambdas, and local functions. Each physical body
  uses canonical IL operation equality as defined by
  [IL diff canonicalization](il-diff-canonicalization.md) (tokens resolved to
  names, no decompilation). A method with no body on both sides is equal on
  this axis. The compared physical-body facts are canonical operations and
  symbolic operands, branch and switch topology, exception regions (including
  catch types and filters), local variable types, `init locals`, `.maxstack`,
  and MethodImpl flags. `.maxstack` and MethodImpl changes may conservatively
  report `Changed`; omitting them would make physical-body `Unchanged`
  incomplete. A fact the producer cannot compare makes the Member
  `Indeterminate`, never `Unchanged`. String operands compare as resolved
  user-string values, never heap tokens, so a literal that only moved is equal
  and a changed literal is a difference.
- **One-sided methods (both passes).** The complete Implementation Diff compares the union of
  declared methods, so the producer also takes a census of methods present on
  only one side, including non-public ones. A one-sided method makes its Type
  `Changed`, whatever its accessibility. Producing the census needs method
  declarations only, not bodies.

The body comparison may decode lazily: walk both IL streams in lockstep, stop at
the first difference, and resolve token operands only when reached, memoized per
side. Equal token numbers never prove equal targets across assemblies. Length
alone proves a change only if canonicalization does not normalize encodings.
This changes how early the walk stops, not what counts as equal.

Independent per-side caching cannot soundly normalize compiler-generated
ordinals by itself. A generated identity shift such as `<M>b__5_0` to
`<M>b__5_1` therefore reports the owning Member `Changed`, even when its
canonical execution bodies otherwise match. This is permitted conservative
over-reporting; it cannot establish `Unchanged`.

The contract is **presence means change**. Fast Diff takes the first sign of
change, of any kind (added, removed, API, or implementation), and stops. It
does not say which kind of change was found, does not keep looking, and
produces no flags, counts, classifications, or text. A host that needs more
requests the Complete pass.

Compared facts and early exit mean the result is an existence proof, not an
inventory. No row counts, classifications, or text are produced.

## Consequences for the complete diff

Fast Diff must be sound against the complete diff for the same pair, per pass
and for the facts that pass compares: a subject reported `Unchanged` has no
difference in those facts in the complete diff. A Library `Unchanged` Type may
still have a changed body; that is by design and is not a soundness failure. The
converse is not required. The gate is a corpus comparison over real package
pairs with zero `Unchanged` subjects, per pass, that the complete diff reports
changed in the facts that pass compares. Each pass also publishes exact-head
NativeAOT and Browser/Wasm numbers against the complete diff for the same
pairs.

Progress toward the selected Library policy is measured as one acquisition-free
before/after comparison. Both package sides were already acquired from exact
local assets before timing; Browser runtime startup also completed before the
timed operation, and no measured path performed network work. **Before** is
exact #9686 with its whole-Library body walk. **After** is the same exact head
with only that Library body walk skipped:

| Asset and runtime | Before: #9686 body walk | After: same-head body-free projection | Change |
| --- | ---: | ---: | ---: |
| `System.Text.Json` 9.0.0 to 10.0.0, NativeAOT | 141.43 ms | 99.49 ms | -29.7% |
| `Aspire.Hosting` 13.6.0 to 13.6.1, NativeAOT | 454.43 ms | 338.72 ms | -25.5% |
| `Aspire.Hosting` 13.6.0 to 13.6.1, Firefox/Mono Browser-Wasm | 5,425 ms | 3,567.5 ms | -34.2% |

This After is a policy projection, not the Fast Diff implementation: it still
constructs the complete API comparison, and 3,567.5 ms remains unacceptable.
It proves that removing Library body work moves the existing path in the right
direction. The producer implementation slice must publish its own exact
base/head, acquisition-free before/after comparison to establish further
progress.

The Library body policy was tested with a raw per-side digest prototype over
canonical operations, symbolic operands, control-flow topology, exception
regions, locals, body flags, and owner-attributed state-machine, lambda, and
local-function bodies. It did not construct an API diff, Research population,
row inventory, or serialized result. Exact `origin/main` `2b11462b5`, exact
package DLLs, and one host (`dotnet-inspect-perf-3`) were used throughout.
Every measured command ran under `perf-guard`.

These are older-side and newer-side construction costs, not an implementation
before/after comparison. Package acquisition, network work, and Browser runtime
startup were outside the timed regions.

| Pair | Physical bodies older / newer | NativeAOT older / newer digest | Browser-Wasm older / newer digest | Cached comparison |
| --- | ---: | ---: | ---: | ---: |
| `Aspire.Hosting` 13.6.0 to 13.6.1 | 11,483 / 11,483 | 841.4 / 845.8 ms | 15,982 / 15,988 ms | 12.1 ms NativeAOT; 62 ms Browser |
| `System.Text.Json` 9.0.0 to 10.0.0 | 3,884 / 4,055 | 155.1 / 160.6 ms | 3,133 / 3,330 ms | 3.7 ms NativeAOT; 22 ms Browser |

NativeAOT used three warmups and 20 samples; the Aspire side p95 values were
853.2 and 851.7 ms. Browser/Wasm used one warmup and three samples; the Aspire
side p95 values were 16,034 and 16,006 ms. Aspire retained 9,629 declared
owners and 1,857 generated-body origins per side. It found 15 changed owners
across two Types: seven direct-body changes, four generated-only changes, and
four one-sided owners. Stable result hashes agreed across both runtimes.

The higher fidelity is real, but cold Library snapshots do not survive
Browser/Wasm: the two Aspire sides cost about 32 seconds before presentation,
versus 5,425 ms for #9686's narrower whole-Library body comparison and 3,567.5
ms when that comparison was skipped. Cached comparison is cheap, but the first
Library request still has to create both immutable snapshots. The Library pass
therefore remains body-free; Type requests compute and may cache only the
unresolved Members they actually reach.

The existing Member Body path remains a rejected baseline rather than a Fast
Diff source. #9713 fixed its historical selector defect, but the post-fix path
still pays complete API, Research, decompilation, and dual-mechanism inventory
costs. Exact measurements and correction history are retained on
[#9686](https://github.com/richlander/dotnet-inspect/pull/9686) and this design's
pull request rather than expanded here.

## Hosts

The producer is host-neutral and returns the shared `InspectionEnvelope<T>`
shape. Browser Compare is the first adopter; the CLI follows by exposing the
same producer rather than a second implementation.

Consumers decide how to present the result. Browser Compare presentation, the
Library result contract, the navigation cues, and the Member Compare default
belong to their owners ([Compare experience](inspect-web-compare-experience.md)
and [Member Body Diff](inspect-web-member-body-diff.md)) and are specified in
those owners' adoption slices, not here. Fast Diff supplies only the
per-subject state those slices consume.

## Caching and prefetch handoff

Inputs are immutable, so Fast Diff results are a pure function of the exact
Library pair, scope, and subject, and hosts may cache them. The Library result
does not seed the Type pass: early exit stops at the first difference per Type,
so Member states are a separate computation. A miss recomputes and never changes
a result.

The raw producer may also cache one physical MethodDef digest per exact assembly
version. The Library pass does not populate that cache. A Type request reuses
available entries and lazily computes only physical bodies attributed to
unresolved Members; cached whole-snapshot comparison time does not justify
paying whole-snapshot construction on the first Library request.

The producer is **non-streaming**: each request returns one complete result for
its scope (all Type states for a Library request, all Member states for a Type
request). Per-subject streaming is not part of this design or its first
adoption slices.

Host caching, speculative Member-diff prefetch, streaming, and cancellation are
host policy, not producer behavior, and are outside this document. A separate
Browser owner will define them. This document supplies only the per-subject state
that policy consumes.

## Non-claims

- Fast Diff does not define API semantics, canonical IL, or correspondence.
- It does not report counts, classifications, breaking-change status, or text.
- It does not replace Public API or Member Body; they remain the complete
  views.
- It does not claim exact `Changed`. A `Changed` result may be a difference the
  complete diff presents differently or suppresses.
- It does not compare decompiled C#.

## Adoption

The plan has **seven independently mergeable slices** under #9716:

1. **QuerySpace producer.** Library and Type `Exists` requests over one exact
   Library pair, typed states, terminal-specialized producer capabilities,
   request-set execution, and per-pass soundness gates. Includes exact-head
   NativeAOT and Browser/Wasm numbers against the complete and #9686 shallow
   paths for the same pairs.
2. **Browser Library pass.** Consume Library states and update the Compare
   experience owner.
3. **Browser Type pass.** Consume Member states and update the Compare
   experience owner.
4. **Member hybrid.** Update the Member Body Diff owner.
5. **CLI.** Expose the same producer through the `diff` surface.
6. **Browser cache.** Define result retention in a separate Browser owner.
7. **Speculative prefetch.** A later, measurement-gated Browser design; not
   part of the first adoption.

## Open design questions

- Whether `Indeterminate` should offer a one-step promotion to the Complete
  pass in the list, or only at the Member boundary.

## Acceptance scenarios

1. A Library request returns one state per Type, stopping at the first sign of
   change per Type, with no counts and no complete rows. A Type whose only
   change is a method body is `Unchanged` at Library scope and `Changed` at
   Type scope.
2. A Type request returns one state per Member. A `Changed` Member may
   over-report; an `Unchanged` Member has no difference in the compared facts.
3. Added and removed subjects are reported `Changed` with no further kind.
4. A decode failure yields `Indeterminate`, never `Unchanged`.
5. Per pass, no subject reported `Unchanged` appears in the complete diff in the
   facts that pass compares.
6. A method whose only change is a string literal is `Changed` at Type scope; a
   method whose literal only moved heap offsets is `Unchanged`.
7. A Type whose only change is an added private method is `Changed` at Library
   scope.
8. A method whose only change is an exception-handler catch type is `Changed`
   or `Indeterminate` at Type scope, never `Unchanged`.
9. A Type whose only change is a Type-facet Finding with no classified
   `ApiChange` is `Changed` at Library scope.
10. A Type whose only API difference is a changed Member Finding with no
    classified `ApiChange` is `Changed` at Library scope, and that Member is
    `Changed` at Type scope.
11. A changed Type settles its Library `Exists` request at its first witness;
    no later member of that Type is classified, projected, or charged to that
    request.
12. An unchanged Type exhausts its Library witness source and returns
    `Unchanged` without constructing a complete `ApiDiff`, shallow summary, or
    row inventory.
13. Ordinary methods named `Explicit` and `Implicit` and conversion operators
    in the same Type remain distinct raw MethodDef subjects; no textual
    selector round-trip participates in body acquisition.
14. One method whose raw body cannot be decoded is `Indeterminate`; other
    subjects in the same request retain their independently settled states.
15. A change confined to an async or iterator state-machine execution body,
    lambda, or local function is attributed to its declared owner and makes
    that Member `Changed` at Type scope.
16. Rejected, ambiguous, incomplete, or limit-exhausted generated-body
    attribution makes the declared Member `Indeterminate`, never `Unchanged`.
17. A compiler-generated ordinal shift may conservatively make affected
    Members `Changed`; it never proves `Unchanged`.
