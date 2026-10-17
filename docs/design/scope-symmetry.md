# Scope symmetry

## Status and owner

This document owns one cross-cutting pattern: how an analysis is targeted at
the **Library**, **Type**, and **Member** scopes, and how hosts disclose its
cost across them. It owns:

- the scope spectrum and why Package is not on it;
- symmetry: each scope is a first-class target;
- scope-proportional work;
- one infrastructure for every scope and for the Rows, Count, and Exists
  terminals, with Exists exact;
- **lean roots, heavy leaves**: how a host spends cost as a user narrows scope;
- **no rows within rows** for ephemeral output; and
- the retention rule for results nobody requested.

It is tracked by
[#10215](https://github.com/richlander/dotnet-inspect/issues/10215). Each
existing owner adopts the pattern as its own focused effort
([Adoption](#adoption)). This document does not change any owner's internals.

Adjacent owners stay normative:

- [Analysis surfaces and universes](analysis-surfaces-and-universes.md#report-surface)
  owns the report-surface kinds, question modes, and analysis descriptors.
  This pattern constrains which of those kinds an analysis declares; it mints
  no kind.
- [QuerySpace composition](query-space-composition.md) owns the Rows, Count,
  and Exists terminals and their lowering.
- [Progressive disclosure](progressive-disclosure.md) owns CLI verbosity,
  categories, discovery budgets, and the network policy.
- [Stateless core services](stateless-core-services.md) owns retained
  semantic state and the persistent-cache port.
- [Diff system](diff-system.md) and [Fast Diff](fast-diff.md) own the diff
  request, producers, and their equality.

## Why

The two ends of the spectrum are natural to build well: a whole-Library view
and a single-Member view. Type, between them, is easy to forget. Today it
mostly is, and so, in cost, is Member:

- Inspect Web's Type and Member Compare run the whole-Library API comparison
  and filter afterward, and the Type API cues do the same
  (`LibraryApiDiffExports.QueryLibraryApiDiffCore`).
- Member Body Diff builds a Library-wide inventory before it diffs one Member
  (`MemberBodyDiffExports.QueryMemberBodyDiffCore`).
- The CLI's `diff --analysis` path extracts both complete surfaces and filters
  Types and Members afterward; only the plain Library API diff passes `-t` to
  extraction as an `ApiTypeSelection`.
- Fast Diff answers at Library scope only; its Type and Member levels are
  planned ([Fast Diff adoption](fast-diff.md#adoption), steps 7 and 8).
- No diff result declares a Count or Exists terminal.

The cost lands where users notice it. A user who opens one Member in a large
Library waits for, and pays memory for, a Library they did not ask about. A
host that wants a quick map of what changed has only the complete answer to
ask for.

## Scope spectrum

The analysis scopes nest: a Library contains Types, and a Type contains
Members. Each scope is one owner-issued subject, a
[report surface](analysis-surfaces-and-universes.md#report-surface) of that
kind.

**Package** is not on the spectrum. A Package contains several target
frameworks and several Libraries, so a Package question is a set of Library
questions, one per selected framework and Library, for every analysis whose
answer lives at Library scope or narrower. A host may present the set. Facts
that exist only at Package scope, such as package dependencies, stay Package
analyses. **Root** and **Workspace** surfaces are also off the spectrum; an
analysis of relationships across Libraries, such as the integrations census,
answers at its own surface.

## Symmetry

Library, Type, and Member are first-class targets for every analysis:

1. An analysis that supports one scope declares all three in its descriptor,
   or its owner records which scope does not apply and why as a non-claim. A
   whole-Library view and a single-Member view are equally first-class, and
   Type is never the scope left out.
2. A request names its scope. The scope does not change the analysis, its
   equality, or its result shape; it changes only the subjects answered.
3. A subject that two scopes both answer gets the same answer at each. A
   Member's result inside a Type or Library answer equals that Member's own
   answer. Completeness stays operation-scoped, as in
   [Type-scoped comparison](library-api-diff-presentation.md#type-scoped-comparison):
   a failure in a subject outside the requested scope does not make the
   narrower answer unavailable.

## Scope-proportional work

A request at a scope does the work that scope needs: it reads, decodes, and
compares only its subjects and the dependencies its owner declares, such as the
extension-declaring Types that
[Type-scoped comparison](library-api-diff-presentation.md#type-scoped-comparison)
pairs with a selection. Computing an enclosing scope and filtering afterward
is a defect at the adopting owner unless that owner names it as a reference
slice with a filed, deferred pushdown.

Work scales with the requested scope, not with the Library that contains it.
Each adoption shows this with NativeAOT time at each scope over a large
Library, such as System.Private.CoreLib; a one-Type CoreLib API diff, for
example, takes 0.18 s against 1.27 s for the Library
([Type-scoped comparison](library-api-diff-presentation.md#type-scoped-comparison)).

## One infrastructure and three terminals

Every scope and every terminal answer with one owner-defined equality. The
scope and the terminal are request terms; neither selects a different
equality. How a terminal is planned and executed belongs to
[QuerySpace composition](query-space-composition.md), which lowers Rows,
Count, and Exists as distinct plans and lets a source answer a terminal
natively. A cheaper walk that shares the equality, such as Fast Diff's
per-Type Exists, conforms when it is gated exact against Rows:

| Terminal | Answers | Precision |
| --- | --- | --- |
| **Exists** | whether any row exists | low: one bit |
| **Count** | how many rows exist | middle |
| **Rows** | which rows exist, with their detail | high |

Precision is how much an answer says. **Accuracy** is whether it agrees with
the complete answer, and every terminal is exact:

- **Exists** is `true` exactly when Rows at that scope is non-empty;
- **Count** equals the number of Rows; and
- a subject that cannot be decided carries QuerySpace's typed outcome for it,
  never `false` or zero; Fast Diff's per-Type `Indeterminate` is one example. A Count over children some of which are undecided reports them as
  undecided rather than counting them either way.

A cheaper terminal differs from Rows only in cost: it stops at the first
deciding evidence and builds no row it does not report. A low-precision answer
is the protocol of Exists, not a license to approximate. A faster path with
its own equality is not this pattern: its answers drift from the complete
ones, as [Fast Diff](fast-diff.md#why) records.

The hard requirement is **low latency with exact accuracy for Exists**. It is
what makes the lean root below worth entering.

## Lean roots, heavy leaves

A host spends little to enter a scope and negotiates heavier work as the user
narrows. On entering a scope, it asks for the cheapest exact answer about that
scope's children, typically Exists or Count per child: on entering a Library,
which Types changed. That answer is a map of the territory: exact, low in
precision, and fast. Rows for a narrower scope run when the user traverses to
it or makes an explicit gesture, where their latency is spent on a question the
user asked.

In an interactive host, Library-scope Rows is never the default on entry: most
of it would be discarded unseen, and its compute would not pay for itself. A
Library-scope Rows view remains first-class, entered by an explicit gesture.
Type is the interesting middle: it is often small enough that Rows are cheap,
and the right default there is an adoption decision backed by measurement.

The CLI applies the same rule through its own disclosure model: the default
answer is the cheap one at the requested scope, and broader or heavier work
takes an explicit option ([Progressive disclosure](progressive-disclosure.md)).

## No rows within rows

A **rows-within-rows** query answers Rows at one scope and, inside each row,
Rows at the next narrower scope: every Type of a Library, each with every
changed Member and its detail. Its cost is the product of the scopes, the
query-level form of a nested loop. A full Library diff is exactly this shape.

A rows-within-rows query is legitimate when its consumer keeps the result as a
**persistent artifact** it refers to many times, such as a report, a file the
CLI writes, or a document cached by build commit: the cost is paid once and
read often. For **ephemeral** output, such as a website view that is rendered
and discarded, it is avoided for the same reason a nested loop is avoided in
code.

The usual way out is to reframe the question rather than speed up the nested
query. The outer scope asks for a terminal that needs no inner Rows, Exists or
Count per child, and inner Rows run only for the child the user opens. Fast
Diff is this reframing at Library scope: "which Types changed?" replaces
"every change in every Type", and the complete diff of one Type or Member
answers the question the user then asks.

An adopting owner names each rows-within-rows query it offers and the
persistent consumer that justifies it.

## Retention

A host computes and retains what was requested, at the scope requested.

A retained host such as the website may prefer one wider computation to many
narrower requests, such as retaining a Type's Rows to serve each of its
Members, to save Worker round trips. That is an optimization, not the default.
It needs NativeAOT and Browser/Wasm evidence that it lowers user-observed
latency over the traversals users make, and a stated memory bound. It must
not retain data the user has not requested beyond that bound. Without that
evidence, each traversal asks for its own scope.

Background analysis that the owning design already approved, such as Fast
Diff's default baselines in
[Inspect-web background analysis](inspect-web-background-analysis.md), remains
governed there.

## Adoption

Each owner adopts this pattern as its own focused effort and names this
document. The current state is from a survey of `main` at `b3e9103d4`:

| Owner | Library | Type | Member | First adoption |
| --- | --- | --- | --- | --- |
| [Inspect-web Library API diff](inspect-web-library-api-diff.md) | whole | whole, filtered | whole, filtered | Type and Member Compare use Type-scoped comparison ([#10201](https://github.com/richlander/dotnet-inspect/issues/10201)) |
| [Inspect-web Member Body Diff](inspect-web-member-body-diff.md) | inventory | none | inventory first | Member diff without the Library inventory |
| [Fast Diff](fast-diff.md) | Exists per Type | planned, step 7 | planned, step 8 | Type and Member levels on the shared equality |
| CLI `diff --analysis` ([Diff system](diff-system.md)) | whole | whole, filtered | whole, filtered | pass the Type selection to extraction |
| Diff analyses' descriptors ([Analysis surfaces](analysis-surfaces-and-universes.md)) | `api`, `string-literals` | `api`, `api-attribute` | `api` and body analyses | declare all three scopes or record a non-claim |
| Diff terminals (QuerySpace) | Rows only | Rows only | Rows only | Exists and Count over the shared producer |

Two consequences need a decision in their owners' adoptions:

- **Type cues on Type and Member Compare pages.** Inspect Web marks every
  changed Type in the Library from the whole-Library comparison. Under this
  pattern they come from an exact Exists per Type, Fast Diff's Library answer,
  rather than from Rows nobody requested.
- **Member Body Diff's inventory.** The Library-wide inventory remains the
  Library-scope Rows view, entered explicitly; a Member request does not build
  it.

## Gates

Each adoption gates its own claims, by outcome:

- **Symmetry:** the same subject answered at each scope its owner supports
  gives the same answer.
- **Exactness:** Exists and Count equal the Rows they summarize, over the
  owner's fixture pairs, with QuerySpace's typed outcome for any subject Rows
  cannot decide.
- **No rows within rows:** each rows-within-rows query the owner offers names
  its persistent consumer; an ephemeral host view uses none.
- **Exists latency:** NativeAOT time for Exists at the entered scope over a
  large Library, reported in the PR beside Rows at the same scope, with any
  latency budget the owner sets.
- **Proportional work:** NativeAOT time at each scope over a large Library,
  reported in the PR, shows the narrower scope costs less.
- **Retention:** a host that retains a wider computation reports its latency
  and memory evidence and enforces its bound.

## Non-claims

- This pattern does not define any analysis's equality, rows, or presentation.
- It does not add report-surface kinds or terminals; it uses the existing ones.
- It does not decide, for any host, the default at Type scope; each adoption
  does, with measurements.
- It does not address correspondence across versions, such as a renamed or
  moved Member; that belongs to
  [API qualified anchor](api-qualified-anchor.md#version-pair-correspondence).
- It does not change the network policy or approve any acquisition.
