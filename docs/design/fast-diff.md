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
> `Indeterminate`. `Unchanged` is exact: the complete diff would report no API
> or implementation difference for that subject. `Changed` and `Indeterminate`
> may over-report relative to the complete diff but never hide a change. Fast
> Diff stops at the first difference per subject, performs no decompilation, and
> never produces row-level detail.

Design basis: user direction in the Fast Diff session, "Unchanged is exact;
Changed/Maybe may over-report", canonical IL operation equality per paired
method, and a host-neutral producer with Browser Compare as first adopter.

## Why

Library-scope Compare computes a complete Library diff, and Member Body
([Member Body Diff](inspect-web-member-body-diff.md)) compares every changed
implementation, to learn which Types and Members changed. When the question is
only "did this change?", that work is wasted. Most subjects in a version pair
are unchanged, and proving that is far cheaper than describing a change.

## Passes and scopes

| Pass | Scope | Returns | Cost bound |
| --- | --- | --- | --- |
| Fast | Library | One state per Type | One equality check per member, stop at the first sign of change |
| Fast | Type | One state per Member | One equality check per member, stop at the first sign of change |
| Complete | Member | Full API and body diff for one exact Member | Existing [Annotated Source diff](annotated-source-diff-document.md) cost |

Scopes nest. A Type is `Changed` when its definition or any member differs, so
the Library pass does not need the Type pass. The Type pass is requested only
for a Type the user opens, and the Complete pass only for a Member the user
opens.

## Subject states and equality

| State | Meaning |
| --- | --- |
| `Unchanged` | Both sides exist and every compared fact is equal under the rules below |
| `Changed` | An owner-issued difference was found: added, removed, API difference, or implementation difference |
| `Indeterminate` | Equality could not be decided cheaply or at all (decode failure, unsupported input, work limit); the typed reason is retained |

`Indeterminate` is never reported as `Unchanged`. A host treats it as possibly
changed and may offer the Complete pass.

Equality is decided per paired subject:

- **API.** The facts that Metadata's `ApiDiff` classifies over the two endpoint
  API surfaces (signature, accessibility, modifiers, constraints, attributes
  the complete diff reports). Fast Diff consumes that Metadata correspondence
  and classification directly, with early exit. It does not consume
  [Library API diff presentation](library-api-diff-presentation.md), which
  admits only a completed Library comparison and would force the complete diff
  first. The presentation remains the owner of how complete results are shown;
  Fast Diff defines no second notion of API equality.
- **Implementation.** Canonical IL operation equality for each paired,
  body-backed method, as defined by
  [IL diff canonicalization](il-diff-canonicalization.md) (tokens resolved to
  names, no decompilation). A method with no body on both sides is equal on
  this axis. Canonical operations do not cover every body fact the complete
  Implementation Diff renders, so the comparison also covers exception regions
  (including catch types and filters) and local variable types. A body fact the
  producer cannot compare, or that canonicalization does not define, makes the
  method `Indeterminate`, never `Unchanged`. String operands compare as
  resolved user-string values, never heap tokens, so a literal that only moved
  is equal and a changed literal is a difference.
- **One-sided methods.** The complete Implementation Diff compares the union of
  declared methods, so the producer also takes a census of methods present on
  only one side, including non-public ones. A one-sided method makes its Type
  `Changed`, whatever its accessibility.

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

Fast Diff must be sound against the complete diff for the same pair: a subject
the fast pass reports `Unchanged` has no row in the complete API diff and no
changed body in Implementation Diff. The converse is not required. The gate is
a corpus comparison over real package pairs with zero `Unchanged` subjects that
the complete diff reports changed.

## Hosts

The producer is host-neutral and returns the shared `InspectionEnvelope<T>`
shape. Browser Compare is the first adopter; the CLI follows by exposing the
same producer rather than a second implementation.

Browser adoption, owned by [Compare experience](inspect-web-compare-experience.md):

- the Library pass runs when Compare opens and advertises changed Types in the
  Type list and the Compare frame. Library Compare no longer renders complete
  rows and counts on open; the result contract is "changed or not, no counts",
  and complete rows are computed only on demand;
- the Type pass runs when a Type opens and advertises changed Members in the
  Member list; and
- the Complete pass runs only at the Member boundary.

The existing **API differences** navigation cue becomes a Fast Diff cue, and the
result-contract change above is a Compare experience owner edit made in the
Browser Library slice, not here.

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

## Member compare hybrid

On Member Compare, the default becomes a hybrid: show the full body diff when
the implementation changed, and otherwise show the API diff, as the type
printer diff. Fast Diff does not choose between them. The Member boundary runs the Complete
pass, so the hybrid selects from that complete result. The presentation
contract belongs to [Member Body Diff](inspect-web-member-body-diff.md) and is
its own slice; Fast Diff supplies only the Member `Changed` marker in the list.

## Non-claims

- Fast Diff does not define API semantics, canonical IL, or correspondence.
- It does not report counts, classifications, breaking-change status, or text.
- It does not replace Public API or Member Body; they remain the complete
  views.
- It does not claim exact `Changed`. A `Changed` result may be a difference the
  complete diff presents differently or suppresses.
- It does not compare decompiled C#.

## Adoption

Each step is independently mergeable under #9716.

1. **Producer.** Library and Type passes over one exact Library pair, with
   typed states, early exit, and the soundness corpus gate. Includes
   NativeAOT numbers against the complete diff for the same pairs.
2. **Browser Library pass.** Operation, Compare frame status, and Type list
   cues; changes the Compare experience owner's Library result contract to
   changed-or-not with no counts.
3. **Browser Type pass.** Member list cues on opening a Type.
4. **Member hybrid.** Default Member Compare presentation; updates the Member
   Body Diff owner.
5. **CLI.** Expose the producer through `diff`.
6. **Speculative Member streaming.** Owned and gated by a separate Browser
   design; not part of the first adoption slices.

## Open design questions

- Whether `Indeterminate` should offer a one-step promotion to the Complete
  pass in the list, or only at the Member boundary.
- Whether added and removed Types and Members are reported as `Changed` with an
  occupied-side marker or as their own states.

## Acceptance scenarios

1. Opening Compare for a version pair marks only changed Types, and shows no counts and no complete rows until
   requested.
2. Opening a changed Type marks only its changed Members; an unchanged Member
   is never marked.
3. A Member the Fast Diff marks `Changed` opens the Complete pass; the hybrid
   shows the body diff when the implementation changed, otherwise the API diff.
4. A decode failure yields `Indeterminate`, never `Unchanged`.
5. Across the corpus, no subject reported `Unchanged` appears in the complete
   diff.
6. A method whose only change is a string literal is `Changed`; a method whose literal only moved heap offsets is `Unchanged`.
7. A Type whose only change is an added private method is `Changed`.
8. A method whose only change is an exception-handler catch type is `Changed`
    or `Indeterminate`, never `Unchanged`.
