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
  this axis. Canonical operations do not cover every body fact the complete
  Implementation Diff renders, so the comparison also covers exception regions
  (including catch types and filters) and local variable types. A body fact the
  producer cannot compare, or that canonicalization does not define, makes the
  method `Indeterminate`, never `Unchanged`. A member already `Changed` by an
  API difference, or added or removed, needs no body comparison for the state.

A `Changed` Member also carries a **cause**: `Api`, `Body`, or both, or
`Added`/`Removed`. The cause is a separate field from the state, so the
three-state contract is unchanged. `Body` is set when a paired method's
implementation differs; it is `Api`-only only when the body comparison ran and
found equality. When an API difference short-circuits the body comparison, the
cause is `Api` and the body is unknown; a consumer that needs the body signal
for such a Member (the hybrid, prefetch) requests a body check for that exact
Member, which is cheap relative to the complete diff and does not decompile.

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

## Caching and speculative Member diffs

Inputs are immutable, so results are cacheable. Fast Diff results are cached
by exact pair identity, scope, and subject. The Library result is reused when
the same Compare reopens. It does not seed the Type pass: early exit stops at
the first difference per Type, so Member states are a separate computation.
Completed Member diffs are cached in a bounded LRU keyed by exact Member
identity. A cache is an optimization; a miss recomputes and never changes a
result.

When a Type opens, a host runs the Type pass and marks Members. It may then
speculatively stream complete Member diffs, subject to all of these:

- only Members whose cause includes a confirmed `Body` difference are
  prefetched; API-only Members take the API diff path and need no prefetch;
- prefetch starts only when the host is idle, in priority order: the hovered or
  focused Member, then the remaining list, up to a fixed cap;
- user-initiated work preempts prefetch, and navigation cancels it. Cancellation
  must reach the computation, not only the publication loop, because a
  single-threaded Wasm worker held by prefetch delays the user's next action;
  and
- each completed Member diff is one independent event, so streaming delivers
  real incremental results rather than replaying a finished array.

Speculative streaming is not part of the first Browser slices. It is adopted
only when measurement shows it helps: member-open latency without prefetch is
noticeable, and with prefetch the user's next Member open is served from cache
at a materially lower time-to-first-diff, at an acceptable idle compute cost.
The measured quantities are time-to-first-diff on Member open with and without
prefetch, prefetch hit rate (the user opens a Member that was prefetched), and
compute spent on Members never opened.

## Member compare hybrid

On Member Compare, the default becomes a hybrid: show the full body diff when
the implementation changed, and otherwise show the API diff, as the type
printer diff. The Fast Diff Member cause selects between them (confirmed `Body`
shows the body diff; `Api` with an equal body shows the API diff) without
running the complete body diff for an API-only change. The presentation contract
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
3. **Browser Type pass.** Member list cues on opening a Type, plus the Fast
   Diff and Member-diff caches.
4. **Member hybrid.** Default Member Compare presentation; updates the Member
   Body Diff owner.
5. **CLI.** Expose the producer through `diff`.
6. **Speculative Member streaming.** Only if the measurement gate above is met;
   requires cancellation that reaches the computation.

## Open design questions

- The prefetch cap and the idle-time definition for a Wasm host.
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
6. Reopening a Compare or Type serves Fast Diff from cache with an identical
   result.
7. Navigating away cancels in-flight prefetch before the next user-initiated
   Member diff starts.
