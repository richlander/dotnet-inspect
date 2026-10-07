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
| Fast | Type | One state per Member | The Library facts for that Type's Members, plus canonical body equality for its paired methods |
| Complete | Member | Full API and body diff for one exact Member | Existing [Annotated Source diff](annotated-source-diff-document.md) cost |

Each pass stops at the first sign of change per subject. The passes are tiered
by cost, not nested by result: a Library `Unchanged` says the Type has no API
or member-inventory difference and says nothing about bodies. The Library pass
does not walk method bodies because exact `Unchanged` on a Type requires every
paired body proven equal, and early exit helps only changed Types, so the body
walk costs the whole library on every unchanged Type. Measured on NativeAOT in
[#9686](https://github.com/richlander/dotnet-inspect/pull/9686), adding a body
comparison across the library regressed `System.Text.Json` 9.0.0 to 10.0.0
Library Compare by 35.9% against the API-only diff. Body equality is therefore
a Type-pass fact, where the walk is bounded by one Type. A host that must know
whether any body changed in a Type requests the Type pass; the producer states
the facts each pass compares so no consumer reads more into a state than it
carries.

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
- a raw canonical IL producer decodes paired method bodies lazily and stops at
  the first unequal operation or owned body fact.

Fast Diff does not route through `LibraryBodyAnalysisService`, an Analysis
method population, Research target planning, `ImplementationComparisonQuery`,
`MemberBodyDiffInspection`, a decompiler, or any complete-diff producer. Those
paths answer richer questions, require identities and evidence Fast Diff does
not consume, and prevent terminal pushdown.

Raw Metadata handles and owner-issued correspondence identify each Type,
Member, and MethodDef. Fast Diff does not round-trip a method through a textual
Member selector to recover the body it already owns. This preserves distinct
ordinary methods and conversion operators even when names such as `Explicit`
or `Implicit` overlap selector grammar.

Decode or resolution failure is local to the unresolved subject. QuerySpace
settles that subject as `Indeterminate`; it does not fail the Library or Type
request and does not invalidate subjects already settled as `Changed` or
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
- **Implementation (Type pass only).** Canonical IL operation equality for each
  paired, body-backed method, as defined by
  [IL diff canonicalization](il-diff-canonicalization.md) (tokens resolved to
  names, no decompilation). A method with no body on both sides is equal on
  this axis. Canonical operations do not cover every body fact the complete
  Implementation Diff renders, so the comparison also covers exception regions
  (including catch types and filters) and local variable types. A body fact the
  producer cannot compare, or that canonicalization does not define, makes the
  method `Indeterminate`, never `Unchanged`. String operands compare as
  resolved user-string values, never heap tokens, so a literal that only moved
  is equal and a changed literal is a difference.
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

The performance gate includes `Aspire.Hosting` 13.6.0 to 13.6.1. Diagnostic
evidence on #9686 found a warmed Firefox/Mono Browser/Wasm Library request took
5,425 ms with whole-Library body comparison and 3,567.5 ms when body comparison
was skipped. Both paths still constructed the complete API comparison and
returned no changed rows. Neither is an acceptable Fast Diff implementation;
the accepted producer must demonstrate that QuerySpace `Exists` materially
reduces that product-host latency.

The existing Member Body path is a rejected baseline, not an implementation
candidate. On the same Browser/Wasm host, its `System.Text.Json` 9.0.0 to
10.0.0 inventory took 34,272 ms and one retained changed Member took another
15,134.5 ms. `Aspire.Hosting` ran for 67,923 ms before a Research target
resolution failure. These results prohibit using Library Body Analysis,
Research target resolution, or Member Body inventory as a Fast Diff source;
they do not predict the raw QuerySpace producer's latency.

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
