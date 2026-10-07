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
| Fast | Library | One state per Type | One equality check per member, early exit per Type |
| Fast | Type | One state per Member | One equality check per member, early exit per Member |
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

- **API.** The Metadata-corresponded public API facts that the complete Library
  API diff classifies (signature, accessibility, modifiers, constraints,
  attributes the complete diff reports). Fast Diff reuses
  [Library API diff presentation](library-api-diff-presentation.md)
  correspondence and classification as the single source of API semantics; it
  does not define a second notion of API equality.
- **Implementation.** Canonical IL operation equality for each paired,
  body-backed method, as defined by
  [IL diff canonicalization](il-diff-canonicalization.md) (tokens resolved to
  names, no decompilation). A method with no body on both sides is equal on
  this axis. A member already `Changed` by an API difference, or added or removed,
  needs no body comparison.

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
  Type list and the Compare frame;
- the Type pass runs when a Type opens and advertises changed Members in the
  Member list; and
- the Complete pass runs only at the Member boundary.

The existing **API differences** navigation cue becomes a Fast Diff cue; the
cue contract change is a Compare experience owner edit made in that adoption
slice, not here.

## Member compare hybrid

On Member Compare, the default becomes a hybrid: show the full body diff when
the implementation changed, and otherwise show the API diff, as the type
printer diff. The Fast Diff Member state selects between them without running
the complete body diff for an API-only change. The presentation contract
belongs to [Member Body Diff](inspect-web-member-body-diff.md) and is its own
slice; this document supplies only the Member state it consumes.

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
   cues; updates the Compare experience owner.
3. **Browser Type pass.** Member list cues on opening a Type.
4. **Member hybrid.** Default Member Compare presentation; updates the Member
   Body Diff owner.
5. **CLI.** Expose the producer through `diff`.

## Open design questions

- Whether the Library pass memoizes Type results so the Type pass reuses them.
- Whether `Indeterminate` should offer a one-step promotion to the Complete
  pass in the list, or only at the Member boundary.
- Whether added and removed Types and Members are reported as `Changed` with an
  occupied-side marker or as their own states.

## Acceptance scenarios

1. Opening Compare for a version pair marks only changed Types, with no
   complete Library diff computed.
2. Opening a changed Type marks only its changed Members; an unchanged Member
   is never marked.
3. A Member whose only change is a signature shows the API diff by default; a
   Member whose implementation changed shows the body diff.
4. A decode failure yields `Indeterminate`, never `Unchanged`.
5. Across the corpus, no subject reported `Unchanged` appears in the complete
   diff.
