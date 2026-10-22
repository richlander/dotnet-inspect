# Notable findings

## Status and owner

This document owns **notable findings**: how a host selects the findings worth
a mark in a sparse symbol list, how those marks relate to the dense Findings
view, and what guidance each mark carries. It owns:

- the **notable set**: the owner-issued presentation preset that admits a
  finding to a sparse mark, and the four criteria that define it;
- the **Library-explained** rule that demotes a Member finding to one
  Library-level mark;
- the **structure vocabulary**: one binary pair, foundation and peak, at
  Library, Type, and family scope;
- the **guidance catalog**: one upstream link and one line of advice per
  admitted descriptor, and how hosts render it;
- the **marks-are-projections** contract: every findings mark in a symbol
  list is a projection of one keyed Findings document at that list's scope,
  and the structure mark is a separate owner-issued cue outside it;
- the **Findings view**: the dense Rows projection of the same document, its
  modes, and its join to the marks; and
- the **compilation profile** line a Library presents.

It is tracked by
[#10418](https://github.com/richlander/dotnet-inspect/issues/10418).

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Map](annotated-type-map.md) | The keyed document, its one-version and diff forms, listing, tallies, and the Library, Type, and Member scopes and terminals |
| [Annotated Type Relations](annotated-type-relations.md) | Relations outside a Type, caller populations, and relation color |
| [Hidden-fact annotations](hidden-fact-annotations.md) | The descriptors, the positive-only contract, and the rule that a gesture is chosen per render, not stored on a fact |
| [Finding nomenclature](finding-nomenclature.md) and [Annotated Source Finding provenance](annotated-source-finding-provenance.md) | `Finding<T>`, `PairFinding<T>`, descriptors, and classifications |
| [Scope symmetry](scope-symmetry.md) | Lean roots, heavy leaves, and no rows within rows |
| [Fast Diff](fast-diff.md) | Per-Type and per-Member change states and the default baselines |
| [Library structural report](library-structural-report.md), [Inspect Web implementation profiles](inspect-web-implementation-profiles.md), and [Inspect Web method-leverage achievements](inspect-web-method-leverage-achievements.md) | The degree, hub, and leverage evidence that the structure vocabulary presents |
| [Inspect Web Compare Experience](inspect-web-compare-experience.md) | The one-inspector, several-modes frame, drill-down, and Member result boundary |
| [Memory-safety models](memory-safety-models.md) | Unsafe implementation and unsafe propagation as independent colors |

This is a pattern document under
[Design scope](../design-scope.md#stage-implementation-after-locking-the-design).
It defines the preset, the vocabulary, the catalog, and the projection
contract. Each adopting owner stages its own adoption under
[Adoption](#adoption); this document does not rewrite their internals.

## Why

Inspect Web's symbol lists carry an achievement rail. Today it mixes three
kinds of claim under one visual grammar:

- **rankings**: sea level, mountain peak, Top Leverage, and Implementation
  Hub, each a position in a Library-relative order with its own eligibility
  rule and metaphor;
- **change**: a glyph for Fast Diff Exists against a baseline the user usually
  did not choose; and
- **heat**: a continuous tint meaning larger than its overload siblings.

A ranking cannot be read without a tooltip, because "relative to what?" is
part of the claim. A tint carries no name. Meanwhile the facts that make the
product an IL analysis tool, such as a hidden box, an allocation in a loop, an
unsafe dereference, or a changed body, are objective, opcode-witnessed, and
need no explanation to a .NET developer. They appear only three levels down,
in Member Facts and Annotated Source, and nothing joins a mark to them.

The result is noise where there should be signal. There is no one view of
every notable symbol, no way to filter a symbol list by what was found, and no
path from a mark to the evidence and to what to do about it.

The fix is a model, not an icon set:

> Every findings mark in a symbol list is a projection of one keyed Findings
> document at the scope the list shows. The Findings view is the Rows
> projection of the same document. A findings mark and its row are joined by
> key in both directions. Rankings and relations are not findings; their
> numbers are columns in the Findings view or color in Annotated Type
> Relations, and the one structure cue a row keeps is issued by the structure
> owners, not by the map.

The keyed document is the Annotated Type Map, in its one-version form
(**Current**) and its map-diff form (**Changes**). The map admits only
[local instruction findings](annotated-type-map.md#admitted-findings); it
does not carry degree, leverage, or sibling-call evidence, so a structure
cue cannot be its projection and this document does not pretend it is. This
document decides what the map's findings look like when they reach a symbol
list, and nothing about how the map is produced.

## Terms

- **Mark**: a binary glyph in a symbol list row, at most one per slot. A
  mark carries no count, intensity, or color scale. The one exception is the
  typed **undecided treatment** for a row whose outcome the map cannot
  decide, defined in [Marks are projections](#marks-are-projections).
- **Current mark**: the slot that says the row has notable findings in the
  one-version map.
- **Changes mark**: the slot that says the row is a changed row of the map
  diff.
- **Structure mark**: the slot that says the row is a foundation or a peak at
  its scope. It is issued by the structure owners named in
  [Structure marks](#structure-marks), not projected from the map, and it is
  not a navigation target.
- **Notable**: counted toward the Current mark by the notable set. It is a
  presentation preset, not a severity. A finding that is not notable is still
  a finding and is still in the map.
- **Tally**: the map's count of a row's elements by descriptor, as
  [Annotated Type Map](annotated-type-map.md#tallies) defines it.
- **Chip**: a filter control labeled with a descriptor and its tally.
- **Compilation profile**: the Library-level facts that set expectations for
  its Members: optimized or Debug, runtime async or state-machine async,
  unsafe present. Each fact is read over every method of the Library with a
  CIL body, regardless of accessibility.

## The notable set

The map admits every descriptor a request names. The notable set is narrower.
It is an owner-issued, named preset over descriptors and the attributes their
findings carry, chosen per render exactly as
[hidden-fact annotations](hidden-fact-annotations.md#two-gestures-side-and-caret)
choose a gesture: the selection lives beside the finding, never on it, so the
positive-only contract holds. The CLI's `--focus` is the same kind of
selection at Member scope; the notable set is the product's default focus
for a symbol list.

A finding is notable when any one of four criteria holds:

| Criterion | Admits | Reads |
| --- | --- | --- |
| **Hidden** | A fact the C# text does not show | The descriptor: box, closure, state machine, delegate, enumerator |
| **Amplified** | A fact a loop multiplies | Path context `LoopBody`, multiplicity `Loop`, or conditionality `PerIteration` |
| **Contract** | A caller obligation or a safety boundary | The descriptor: every `unsafe.*` and `lifetime.*` fact the map admits |
| **Expectation** | A fact that contradicts the Library's compilation profile | The profile and an admitted descriptor for the Member's metadata classification; see [Expectation](#expectation) for what is presented before that descriptor exists |

The criteria read attributes the producers already emit. The allocation
producer carries path context, escape, multiplicity, and frequency on every
occurrence, and the annotation carries conditionality. No new analysis is
needed to apply the preset; it is a filter over the existing detail.

Every criterion that drives a Current mark reads a finding the map admits,
so every notable finding has a map row to join to. A fact the map does not
admit cannot drive a mark, however informative: it is presented another way
until a descriptor for it is admitted. Unsafe propagation and unsafe
implementation are relation colors owned by
[Annotated Type Relations](annotated-type-relations.md#memory-safety-color):
a bodyless Member may propagate unsafe, and such a Member has no map row.
That color therefore does not drive a Current mark; Relations presents it.
When the map admits a Member-header descriptor for the contract, the
Contract criterion reads it like any other admitted descriptor.

### Demotions

The same attributes demote. These are never notable:

- `alloc.new` on a straight-line path: a visible `new` is what the source
  says, and marking it is noise;
- any allocation on an error path (`ErrorPath`, escape `ThrowPath`): it runs
  when the method fails, not when it works;
- any allocation with conditionality `CachedOnce`: it runs at most once; and
- a throw site. Throws are a column in the Findings view, never a mark. A
  throw helper would otherwise light every row of a Library.

An amplified demoted fact is still amplified: a visible `new` in a loop body
is notable. Demotion by path or frequency applies before amplification is
read, so an allocation in a loop inside a catch block is still an error-path
fact and stays quiet.

### Library-explained facts

A fact the Library's compilation profile explains is not a Member finding.
The first case is the class state machine in a Debug build. The allocation
producer reports a state machine only on a `newobj` of a state machine type,
which means a class state machine: Release async methods use struct state
machines and never reach that path, so in a Release Library the fact is
rare and honestly Hidden. In a Debug build every async method carries one.

When the profile explains a fact, that descriptor stops counting toward the
Members' Current marks and one Library-level mark names the profile instead.
The Member rows keep the finding in their tallies and in the Findings view;
only the sparse mark moves. The Findings view says why on the row.

Iterators and async iterators are not explained by the profile. Their state
machines are classes in every build, allocated on every call, and they stay
notable as Hidden.

### Expectation

An Expectation finding needs two facts: the Library's profile and a Member
that contradicts it. The first instance is a state-machine async method in a
Library that has runtime async methods. The .NET 11 runtime compiles its own
libraries with runtime async and documents that they then contain no
compiler-generated state machines, and the runtime specification applies the
`Async` implementation flag only to methods that return `Task` or `ValueTask`
variants and have a CIL body. A method that still carries a state machine in
such a Library therefore has a reason: an async iterator, an `async void`
method, a custom method builder, or a method the compiler declined to
convert. The residue is short and each entry is informative.

In a Library with no runtime async, state-machine async is the norm and no
mark is issued. Whether a Member is async stays a trait filter, not a
finding.

The metadata layer already classifies each method as runtime async or
state-machine async. The profile's runtime-async fact is that classification
read over every method of the Library with a CIL body, at every
accessibility: an internal runtime-async method sets the expectation as
surely as a public one. The existing Library presence flags are not that
fact: they summarize public non-accessor methods only, and a Library whose
only runtime-async methods are non-public would report none. The Expectation
adopter supplies the whole-population presence fact through the existing
per-method classification; it adds no second scanner.

The classification is metadata, not an admitted finding. A Release `async
void` method, like any Release async method with a struct state machine,
produces no `alloc.statemachine` occurrence, because the allocation producer
recognizes a state machine only on `newobj`; such a Member has no map row
unless some other finding lists it. An async iterator is different: its
state machine is a class in every build, so it already has an
`alloc.statemachine` row and a Hidden mark, and Expectation would only add a
second reason for a mark it already carries. The no-row case is the Release
`async void` method and its struct-state-machine kin, and a Current mark
such a Member cannot join to is forbidden by the projection claim.
Expectation therefore drives a Member mark only after two adoptions land: the
[provenance owner](annotated-source-finding-provenance.md#classification-contract)
adds a Member-header descriptor for the Member's async classification, and
the map admits Member-header descriptors whose fact is the Member's own
declaration. Until then Expectation is presented in two places that need no
row: the compilation profile line counts the state-machine async methods in a
runtime-async Library and links the catalog entry, and the member list's
trait filter gains a state-machine async trait beside the existing async
traits, so a reader can list them. Neither is a mark, and no gate below
claims an Expectation mark before those steps.

Expectation also makes Changes mode informative for a migration. The
implementation flag is a declaration fact Fast Diff compares, and a package
version compared before and after it adopted runtime async lists each method
whose flag Fast Diff's equality decides as changed. Today that is the
non-admitted Members, whose declaration facts sit on the Body axis. For a
Member the Public API surface admits, Fast Diff's owner records the
implementation flags as
[uncertain API facts](fast-diff.md#axes) until Diff capabilities adoption
step 1 moves them to Body, so a migrated public method is listed only when
its body also changed or once that step lands. The complete migration census
is promised after that step, not before.

## Structure marks

Heat and the four ranking glyphs collapse to one vocabulary with two values,
**foundation** and **peak**, read at three scopes:

| Scope | Over | Foundation | Peak |
| --- | --- | --- | --- |
| Library | Types | sea level: many Types depend on it | mountain peak: it depends on many Types |
| Type | Members | Top Leverage: many callers in the Library | none issued |
| Family | overloads | hub: siblings call it and it calls none | forwarder: it calls a sibling and none calls it |

The family scope is the strongest case for a structure mark in a sparse
list: it answers "which overload should I read" at the moment the reader
is choosing one. The hub designation is already binary and already derived
only from sibling-call edges. Size and tint were a second channel riding on
the same evidence and retire; the family's sizes become a column in the
Findings view.

A structure mark is at most one glyph per row, either value or none. The
same two glyphs serve every scope, and the chip and tooltip name the scope.
Color stays secondary.

The structure mark is not a projection of the map and makes no claim to be.
Its source is the owner that already issues the designation at that scope:
[Library structural report](library-structural-report.md) for Library poles,
[Inspect Web implementation profiles](inspect-web-implementation-profiles.md)
for the family hub, and
[Inspect Web method-leverage achievements](inspect-web-method-leverage-achievements.md)
for Type-scope foundation. The Browser joins each designation to a visible
row by the exact identity that owner issues, as it does today. A Member with
a structure mark need not be listed in the map, so the mark is not a
navigation target: its tooltip carries the owner-issued numbers (degree,
callers, or family sizes and sibling calls) and the scope name, and the
Findings view shows the same numbers in its structure columns for the rows
it lists. The structure owners' existing gates, not the projection gate,
govern the mark's correctness.

Type-scope foundation reads Library-wide caller degree, which
[Annotated Type Relations](annotated-type-relations.md#type-focused-relation-inventory)
treats as a relation needing an explicit caller population. Until that
document is retained for the Type, the Type-scope mark comes from the
existing method-leverage designation; when Relations lands, it comes from
there and the designation query retires. Family and Library scopes have their
producers today.

## Guidance catalog

Every descriptor the notable set admits carries one upstream link and one
line of advice. The catalog is owner-issued, static data keyed by descriptor
id, with an optional refinement per attribute such as loop context. Hosts
render it; nothing fetches it at run time. Entries point at Microsoft Learn,
the .NET blog, or a dotnet/runtime specification, so the product's claims
rest on the platform's own guidance rather than on the product's opinion.

| Descriptor | What to do | Guidance |
| --- | --- | --- |
| `alloc.box` | Avoid the conversion to `object` or an interface; use generics or a span. | [Boxing and unboxing](https://learn.microsoft.com/dotnet/csharp/programming-guide/types/boxing-and-unboxing) |
| `alloc.closure` | Make the lambda `static` or pass the captured state as a parameter. | [Capture of outer variables](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/lambda-expressions#capture-of-outer-variables-and-variable-scope-in-lambda-expressions) |
| `alloc.delegate` | Cache the delegate in a static field or use a `static` lambda. | [Lambda expressions](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/lambda-expressions) |
| `alloc.statemachine` | A class state machine allocates on every call; an iterator's does in every build, an async method's only in a Debug build or with a non-`Task` return. The Member's classification selects the specific advice below. | [Iterators](https://learn.microsoft.com/dotnet/csharp/fundamentals/building-software/iterators) and [Understanding the whys, whats, and whens of ValueTask](https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/) |
| `alloc.statemachine`, iterator | A `yield` method allocates on every call; return a collection or a struct enumerator when the sequence is small or hot. | [Iterators](https://learn.microsoft.com/dotnet/csharp/fundamentals/building-software/iterators) |
| `alloc.statemachine`, async | A class state machine means a Debug build or a non-Task method; see the Library profile. | [Understanding the whys, whats, and whens of ValueTask](https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/) and [PoolingAsyncValueTaskMethodBuilder](https://learn.microsoft.com/dotnet/api/system.runtime.compilerservices.poolingasyncvaluetaskmethodbuilder) |
| `alloc.enumerator` | Enumerate the concrete type, or expose a struct enumerator. | [Avoid memory allocations and data copies](https://learn.microsoft.com/dotnet/csharp/advanced-topics/performance/) |
| `alloc.array`, in loop | Hoist the array, or rent from `ArrayPool<T>`. | [ArrayPool&lt;T&gt;](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1) |
| `alloc.new`, in loop | Hoist the object or reuse one instance; measure the hot path first. | [Avoid memory allocations and data copies](https://learn.microsoft.com/dotnet/csharp/advanced-topics/performance/) |
| `unsafe.deref`, `unsafe.calli` | Prefer `Span<T>` and function pointers behind a validated safe boundary; document the caller's obligation. | [Unsafe code](https://learn.microsoft.com/dotnet/csharp/language-reference/unsafe-code) and [Unsafe code best practices](https://learn.microsoft.com/dotnet/standard/unsafe-code/best-practices) |
| `unsafe.stackalloc` | Cap the length and assign to a span. | [stackalloc expression](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/stackalloc) |
| `unsafe.stackalloc`, in loop | Move the allocation outside the loop. | [CA2014: Do not use stackalloc in loops](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2014) |
| `lifetime.*` | The compiler enforces the ref-safe context; read the contract before changing the signature. | [Ref safe context](https://learn.microsoft.com/dotnet/csharp/advanced-topics/performance/#ref-safe-context) and [ref struct types](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/ref-struct) |
| Expectation, state-machine async under runtime async | Confirm the method is an async iterator, `async void`, or uses a custom builder; otherwise recompile with runtime async on. | [Runtime Async](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime#runtime-async) and the [runtime-async specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/runtime-async.md) |
| Library, Debug-compiled | Ship an optimized build; Debug state machines and disabled JIT optimization are not representative. | [DebuggableAttribute](https://learn.microsoft.com/dotnet/api/system.diagnostics.debuggableattribute) |
| Throw count (column) | Prefer `Try*` patterns and throw helpers on cold paths. | [Best practices for exceptions](https://learn.microsoft.com/dotnet/standard/exceptions/best-practices-for-exceptions) |

A refined row applies when its attribute is known. Otherwise the descriptor's
base row applies, and a descriptor with no matching refined row and no base row
renders no guidance and no placeholder: a straight-line `alloc.new` or
`alloc.array` has rows only in a loop and is demoted above, so its row in the
Findings view carries no advice. A host that knows the Member's classification
selects the iterator or async row, and one that does not renders the base row,
which must be true of both. The loop refinement reads a producer-issued loop
attribute on the finding, never a derivation by a host. The allocation producer
issues that attribute today. A producer that does not, such as the unsafety
producer at the time of writing, leaves the loop row unreachable for its
descriptors, and the adopter that promises loop-refined guidance for them
supplies the attribute as a precondition of its adoption.

Every link above was fetched and its title confirmed on 2026-10-21. A slow,
network-classified test checks each catalog link resolves, under
[Test cost classification](../testing-cost-classification.md); the ordinary
gates read the catalog offline.

Other owners register entries in the same catalog for their own
descriptors. The first Relations adopter, calls to `RequiresUnreferencedCode`,
`RequiresDynamicCode`, and `RequiresAssemblyFiles` callees, points at
[Prepare .NET libraries for trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)
and the
[Native AOT deployment overview](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).
The async-to-blocking color points at
[CA1849](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca1849).
This document owns the catalog's shape and the entries for its own preset,
not those owners' descriptors.

Hosts render guidance in four places: the mark's tooltip and accessible
description, the Findings row, the Annotated Source finding inspector, and
CLI Facts at `-v:d`. The link opens in a new browsing context. A descriptor
without a catalog entry renders no guidance and no placeholder; the gate
below makes an admitted descriptor without an entry a failure.

## Marks are projections

A symbol list row reserves three fixed slots, in this order:

1. the **Current mark**: the row has at least one notable finding in the
   one-version map;
2. the **Changes mark**: the row is a changed row of the map diff against
   the settled Package Diff baseline; and
3. the **structure mark**: foundation or peak at the row's scope, issued by
   the structure owners as [Structure marks](#structure-marks) states.

Each slot is binary. The gutter has three glyphs to learn, and they mean the
same thing on a Type row, a Member row, and an overload row. A row fills zero
to three slots, and nothing reflows when a new descriptor family lands. The
first two slots are the findings marks this section governs; the third is
outside the projection contract.

The Changes mark is the map diff's Exists made visible: Fast Diff answers it
per Type today, and the map diff's changed rows answer it per Member.
[Scope symmetry](scope-symmetry.md#lean-roots-heavy-leaves) says a host
enters a scope with the cheapest exact answer about its children and spends
Rows on traversal; the rail is the leanest root in the product, and the
Findings view is its Rows. A mark that tried to say *which* finding would be
a partial Rows answer squeezed into one glyph, with the reader unable to tell
what lost the contest. That is why the categories live in the tooltip, the
chips, and the Findings columns, not in the gutter.

The Current mark reads the notable subset of the map's findings, which is
narrower than the map's own Exists: a method whose only finding is a
straight-line `alloc.new` has an admitted finding and a `true` Exists, and no
Current mark. The map defines Exists over its admitted rows and no filtered
terminal, so the Current mark is answered at two scopes in two ways:

- **Member rows** read the opened Type's map Rows. The host requests that map
  when the Type is opened and may
  [retain it](annotated-type-map.md#retention) while the Type is open, and
  Type is the scope where Rows are cheap, so no lean-root claim is made or
  needed here.
- **Type rows** in the Library inventory need Exists over the notable
  predicate at Library scope. That is a filtered terminal the map does not
  define, and it is a named adoption for the map owner: predicate pushdown
  into its Library-scope one-version producer, gated exact against Rows and
  measured against the unfiltered census. Until it lands, Type rows carry no
  Current mark. Materializing Library Rows and filtering them in the Browser
  is not an interim: it is the rows-within-rows shape the pattern forbids.

A Type row's Changes mark is Fast Diff's Exists when it is `true`. A Member
row's marks read its own row. An overload-family row aggregates: it shows a
findings mark when any overload in its represented population would, and
that mark is presence only, not a key join, because the map keys one row
per Member and a family is several. Activating a family's findings mark
expands the family, as the nested overload rows already do, and the exact
overload rows carry the marks that navigate. This follows the
[method-leverage rail](inspect-web-method-leverage-achievements.md#exact-member-attribution),
which reserves the parent slot for alignment and places the glyph on exact
rows. The Changes mark is absent while the baseline is pending or
unavailable, and never derived from another baseline.

The map lists two kinds of row, and each slot follows the map's own
[distinction](annotated-type-map.md#scopes-and-terminals) over the evidence
that slot presents. A **changed** row has a listing element: an admitted
finding in the one-version map, a shape transition in the map diff. Its
Exists is `true` even when another requested analysis Failed or its Body
comparison is `Indeterminate` beside a settled API transition; the map keeps
the settled evidence and carries the partial outcome on the row. An
**unresolved** row has no listing element and an unsettled outcome; its
Exists is undecided, not `true`.

Each slot decides three states from that:

- The **Changes slot** is filled when the row has a shape transition,
  whatever else is unsettled beside it; takes the undecided treatment when
  the row has no transition and an `Indeterminate` shape comparison; and is
  empty otherwise. A settled API transition beside an `Indeterminate` Body
  comparison is filled, with the partial outcome named in the description.
- The **Current slot** is defined over notable evidence, which is narrower
  than the row's listing. It is filled when the row has a notable finding,
  whatever else is unsettled beside it; takes the undecided treatment when
  the row has no notable finding and an analysis among the admitted ones
  Failed or was Unavailable, because that outcome could conceal a notable
  finding; and is empty when the row's findings are all non-notable and
  every admitted analysis settled. An `alloc.box` beside a failed analysis
  is filled; a straight-line `alloc.new` beside a failed analysis is
  undecided, not filled, because no notable evidence is known.

The **undecided treatment** is the same glyph outlined rather than filled,
with the accessible description naming the typed outcome. It is never
rendered as changed or notable, and it is never replaced by an empty slot,
because an empty slot reads as unchanged or clean. It is a typed outcome,
not a count or a scale, and it is the only state a slot has beyond present
and absent. A Type row whose Fast Diff state is `Indeterminate` takes the
same treatment in its Changes slot.

Findings marks come from the map and from nothing else. The Browser never
computes a finding, a count, or a change from display text, signatures, or row order,
and never substitutes a visible runner-up when the marked Member is filtered
out of view, as
[method leverage](inspect-web-method-leverage-achievements.md#icon-only-presentation)
already requires.

### Learn more

A mark answers at three distances, each cheaper than the next:

- **Hover.** The tooltip and accessible description are the row's notable
  tally with the catalog's one-line guidance per descriptor, for example
  `2 notable: unsafe dereference; array allocated in a loop`. For the Changes
  mark it is the transition kinds; for the structure mark it is the scope and
  the owner-issued numbers. No navigation.
- **Click.** The atomic subject-plus-lens activation to the Findings view at
  that mark's mode, with the row focused, as [Click-through](#click-through)
  states.
- **Chips.** The categories the glyph does not show live in the filter chips
  beside the list, with their counts.

### Click-through

A findings mark is a navigation target, and its key is the key of the map
row it projects. The map keys Member rows by Member and Library-scope rows
by exact Type identity, so the join has two shapes:

- A **Member or overload row's** mark performs the atomic subject-plus-lens
  activation that Compare drill-down already uses, with the Findings lens,
  and lands on the row with the same Member key in the Type's Findings view
  in the mark's mode: Current for the Current mark, Changes for the Changes
  mark. From that view, activating a row navigates to the Member with its
  recommended lens.
- A **Type row's** mark activates the containing Library subject with the
  Findings lens in the mark's mode and focuses the Library-scope row with
  the same Type identity. From Library Findings, activating a Type row
  drills to that Type with Findings retained, exactly as Library Compare's
  Type rows drill today. A Type row's Current mark and Library Current share
  the census prerequisite, so neither exists without the other.

A family row's mark expands the family instead, as
[Marks are projections](#marks-are-projections) states. The structure mark
is not a navigation target, because its row need not exist in the map.

### Chips

The member list of an opened Type gains a findings facet in its Filters row
beside query, kind, accessibility, and trait. Its chips are the descriptors
present in that Type's map, each labeled with its tally, offered only when
the tally is non-zero. Selecting a chip keeps the Member rows whose tally
includes that descriptor. The chips are the legend: a reader who does not
know a glyph reads the chip beside the same count. The chip set follows the
map, not the notable set, so a reader can widen to every allocation when
they choose.

Chips are a Member-list control only. The Type inventory has no descriptor
chips, because Library rows carry no descriptor tallies and the Library
census of adoption step 8 answers Exists and Count over a predicate, not
per-descriptor occurrence counts per Type. A Library aggregate with an exact
tally contract would be the map owner's adoption, and this document does not
plan it.

### Compilation profile

Library Overview states the profile on one line: optimized or Debug, runtime
async or state-machine async or neither, unsafe present or absent. The
Library-level marks from the Library-explained rule sit on that line, and so
does the Expectation count: in a runtime-async Library, the number of
state-machine async methods, with the catalog entry's link. A reader meets
the expectations before the marks that are read against them.

## Findings view

The Compare inspector becomes **Findings** at Library, Type, and Member,
keeping Compare's one-inspector frame, drill-down, and Member result
boundary. Its modes are:

- **Current**: the one-version map. At Library scope it needs the map's
  Library-scope one-version producer, and until that adoption lands the
  view shows a typed unavailable state naming it, not an empty result;
- **Changes**: the map diff against the Package Diff baseline; and
- **Clone**: retained as Compare's existing Clone mode until it has a better
  home, and not redefined here.

At each scope the view is the map's Rows:

| Scope | Rows | Columns |
| --- | --- | --- |
| Library | one row per listed Type with its Count | Type, listed-Member Count, structure (incoming and outgoing degree from the Library structural report), changed-Member Count in Changes mode |
| Type | one row per listed Member with its tally | Member, each admitted descriptor's tally, throws, size, structure (callers; family sizes and sibling calls), transition kinds in Changes mode |
| Member | that Member's row | Each finding with its coordinates and guidance; an Annotated Source action by instance key for each finding that has one, and declaration coordinates with no Source action for a Member-header finding |

Library rows carry no descriptor tallies. The map's
[tallies](annotated-type-map.md#tallies) are a projection of an opened Type's
map and are not computed for Types the user has not opened; a Library row
carries the Type's entries and its listed-Member Count, and that Count is
what the Library column shows. Descriptor tallies appear when a Type is
opened. The Library structure columns read the Library structural report,
which is a whole-Library document already issued for the Type inventory.

A body finding carries an IL offset and a producer-issued instance key, and
its Annotated Source action uses the selection
[Finding interaction](inspect-web-finding-interaction.md#experience) already
defines. A Member-header finding, such as the async classification of
adoption steps 14 and 15, has no body offset and no instance key, as the
[provenance classification](annotated-source-finding-provenance.md#classification-contract)
states; its row presents the Member's declaration coordinates and the
catalog's guidance, offers no Annotated Source action, and says so, as the
Facts section's unkeyed header rows already do. It still drives the Current
mark and the chips, because those read the map row, not a Source target.

Structure and throw columns are where the numbers live. A reader who wants
"most callers" or "largest overload" sorts a column; no glyph encodes a
rank. Library Rows use the map's Library scope and run no fact census, as
[Scope symmetry](scope-symmetry.md#no-rows-within-rows) requires; opening a
Type asks for that Type's map.

Findings is not the default lens. It is empty for most Types, and the API
inspector remains the navigation backbone. The Type's tally chips appear in
the Members header line, where the change status already sits, so the
reader sees the Type's notable count without leaving API.

Loading, failure, unavailable, and successful-empty states keep Compare's
frame. A successful empty Current view says the Type has no findings for
the admitted analyses; an unresolved row stays visible with its typed
outcome, never presented as changed, notable, or clean.

## Hosts

The CLI prints the same preset: `--focus` with no argument selects the
notable set, the Facts table gains a notable column, and `-v:d` adds the
guidance line and link. The Markout lowering carries the catalog entry as
a field, so JSON and TSV consumers receive the link.

Inspect Web adopts the projection contract, the Findings inspector, the
chips, and the profile line, each as a separately staged slice below.

## Gates

- **Preset:** for a fixture Library with every descriptor and attribute
  combination, the notable set admits exactly the Hidden, Amplified,
  Contract, and Expectation cases and demotes straight-line `alloc.new`,
  error-path, cached-once, and throw cases, including a visible `new` in a
  loop admitted and an allocation in a loop inside a catch block demoted.
- **Library-explained:** a Debug-built fixture with three async methods
  issues one Library mark and no Member marks for the state machine, while
  its iterator keeps a Member mark; the Release build of the same fixture
  issues no Library mark and marks only the iterator.
- **Expectation:** a fixture Library with runtime async methods and one
  async iterator counts the iterator on the profile line and selects it
  with the state-machine async trait, and no Member carries an Expectation
  mark before the descriptor adoptions; the same Library without runtime
  async counts nothing and the profile line says so; a Library whose only
  runtime-async method is internal still counts its public async iterator,
  so the profile reads every accessibility; once the descriptor is admitted,
  the iterator carries the Current mark and nothing else does, and a Release
  `async void` method with no other finding carries it too because it now
  has a row; Changes between the two builds lists each non-admitted method
  whose implementation flag changed, and lists an admitted method only when
  its body changed or Diff capabilities adoption step 1 has landed, with the
  complete census gated after that step.
- **Projection:** for every fixture Type, each Member row's Current mark
  equals whether its map row has a notable finding, its Changes mark equals
  whether the map diff lists it as changed, and an unresolved row in either
  form renders the undecided treatment and never the changed or notable
  state; a row with notable evidence or a shape transition beside a Failed,
  Unavailable, or `Indeterminate` outcome renders its mark and names the
  partial outcome in its description, so an `alloc.box` beside a failed
  analysis and an API transition beside an `Indeterminate` Body comparison
  are both filled; a row whose only finding is a straight-line `alloc.new`
  beside a failed analysis renders the undecided Current treatment, and
  beside settled analyses renders an empty Current slot; a family row shows
  a findings mark exactly when one of its overloads does, and that mark is
  not a key join; each Type row's Changes mark equals Fast Diff's `true` Exists and
  its undecided treatment equals Fast Diff's `Indeterminate`; a Type row's
  Current mark equals the Library census's notable Exists once that
  adoption lands and is absent before it, and is never computed by filtering
  materialized Library Rows; no findings mark exists for a key the map does
  not list, and no mark of any slot carries a count, intensity, or color
  scale.
- **Baseline:** with the Package Diff baseline pending, unavailable, or
  failed, no row of any scope shows a Changes mark, and a result settled for
  a different baseline is not used; the Changes marks appear only after the
  current baseline settles, and Current and structure marks are unaffected
  throughout.
- **Join:** activating a Current or Changes mark on an exact Member or
  overload row lands on the row with the same Member key in the Type's
  Findings view in that mark's mode, and activating that row returns to the
  Member; activating a Type row's mark lands on the Library-scope row with
  the same Type identity in Library Findings in that mark's mode, and
  activating that row drills to the Type with Findings retained; activating
  a family row's mark expands the family and navigates nowhere, including
  when several overloads are marked; a filtered-out marked Member is not
  replaced by a visible one.
- **Tooltip:** a marked row's accessible description names each notable
  descriptor with its tally and catalog line, and a Changes mark names its
  transition kinds.
- **Chips:** in an opened Type's member list, the chip set equals the
  descriptors with non-zero tally in that Type's map, and selecting a chip
  keeps exactly the Member rows whose tally includes it; the Type inventory
  offers no descriptor chips.
- **Structure:** a fixture family with a hub and two forwarders marks the
  hub foundation and the forwarders peak, with no tint and no size cue, and
  the family's sizes appear in the Findings column; a Library fixture with
  aligned and opposing poles issues one structure glyph per Type; a hub
  whose Member the map does not list still carries its structure mark, has
  no Findings row, and activating the mark navigates nowhere.
- **Catalog:** every descriptor the preset admits has a catalog entry; a
  finding with no matching refined row and no base row, such as a
  straight-line `alloc.new`, renders no guidance and no placeholder; an
  `alloc.statemachine` finding whose classification is unknown renders the
  base row and a known classification renders its refined row; a loop row is
  reached only through a producer-issued loop attribute; the slow link test
  resolves every entry; the ordinary gates do not touch the network.
- **Hosts:** CLI Facts with `--focus` and no argument equals the Browser's
  notable set for the same Member, and `-v:d` prints each admitted finding's
  guidance line and link.
- **Guidance delivery:** for a fixture Member with one admitted body
  finding, the Findings row, the Annotated Source finding inspector, and
  the mark's tooltip each render that finding's catalog line and link, and
  the Markout JSON and TSV lowerings of CLI Facts at `-v:d` carry the same
  URL as a field; for a fixture Member whose only finding is a Member-header
  finding, the Findings row and tooltip render the catalog line and link,
  the row offers no Annotated Source action and says so, and the Current
  mark is still present; a descriptor without an entry renders no guidance
  and no placeholder at each of those boundaries.
- **Profile:** Library Overview for a Debug, a runtime-async, and an unsafe
  fixture states each profile fact on one line.

## Adoption

Each step is its own focused effort naming this document and one adopting
owner:

1. **Lock this document.** Owner: this document.
2. **Preset and catalog on the descriptor model.** Owner:
   [Hidden-fact annotations](hidden-fact-annotations.md). The notable
   classification and the catalog entry sit beside each descriptor the
   annotation model declares, and the slow link test lands. Prerequisite:
   step 1.
3. **Profile and Expectation facts.** Owner: Metadata classification
   (`MethodClassificationScanner` and the Debuggable attribute's
   arguments). A whole-population runtime-async and state-machine presence
   fact and the Debug fact are issued for the preset, the Library-explained
   rule, the Findings row's explanation, and the profile line. Prerequisite:
   step 2. Every host slice below that presents the preset depends on this
   step, because the preset's Expectation criterion and its Library-explained
   demotion read these facts.
4. **CLI Facts presentation.** Owner: the CLI host
   ([CLI host architecture](../cli-architecture.md)). `--focus` with no
   argument selects the notable set, Facts gains the notable column, and
   `-v:d` prints the guidance line and carries the link through Markout.
   Prerequisites: steps 2 and 3.
5. **Findings inspector.** Owner:
   [Inspect Web Compare Experience](inspect-web-compare-experience.md).
   Compare is renamed Findings; Current presents the one-version map at Type
   and Member scope and Changes presents the map diff at every scope,
   unresolved rows keep their typed outcome, and the structure and throw
   columns read their existing owner-issued data. Library Current shows a
   typed unavailable state naming the Library census until step 9a enables
   it. Prerequisites: steps 2 and 3, and Annotated Type Map adoption steps 4
   and 5 for the Type-scope documents.
6. **Findings marks and click-through.** Owner:
   [Inspect Web Surface Composition](inspect-web-surface-composition.md),
   which composes the Type and Member navigation panes. The Member rows'
   Current and Changes marks, the undecided treatment, their tooltips, and
   the Members header tally derive from the opened Type's map; the Changes
   mark replaces Compare's navigation diff cue. Prerequisites: steps 2, 3,
   and 5.
7. **Chips.** Owner:
   [Inspect Web Surface Composition](inspect-web-surface-composition.md),
   whose collapsed Filters row owns the member text, kind, accessibility,
   and trait filters and now the findings facet and its selection
   semantics. The chips use the disclosure grammar
   [Inspect Web presentation language](inspect-web-presentation-language.md)
   already defines, without changing it. Prerequisite: step 6.
8. **Library census with the notable predicate.** Owner:
   [Annotated Type Map](annotated-type-map.md), as its own adoption step 4
   for the Library-scope one-version producer, extended with Exists and
   Count over a descriptor-and-attribute predicate, gated exact against Rows
   and measured against the unfiltered census. The whole-Library body decode
   that Type leverage already runs after the first paint is the cost budget
   this census shares.
9. **Type-row marks.** Owner: Inspect Web Surface Composition. Type rows
   take the Current mark from the Library census's notable Exists.
   Prerequisites: steps 6 and 8.
9a. **Library Current.** Owner: Inspect Web Compare Experience, which owns
    the inspector's result and mode semantics. The Library Current view
    replaces its typed unavailable state with the census's Rows.
    Prerequisites: steps 5 and 8.
10. **Compilation profile line.** Owner: Inspect Web Surface Composition,
    which composes Library Overview. The line reads the profile facts over
    every method. Prerequisite: step 3.
11. **Family structure.** Owner:
    [Inspect Web implementation profiles](inspect-web-implementation-profiles.md).
    Tint and size retire; hub and forwarder present as family foundation
    and peak; family sizes become a Findings column. Prerequisite: step 5
    for the column.
12. **Library structure.** Owner:
    [Inspect Web Type leverage](inspect-web-type-leverage.md). Poles
    present as Library foundation and peak with the shared glyphs.
13. **Type structure.** Owner:
    [Inspect Web method-leverage achievements](inspect-web-method-leverage-achievements.md).
    Top Leverage presents as Type foundation with the shared glyph until
    Annotated Type Relations supplies caller degree, when the designation
    query retires under that owner.
14. **Async classification descriptor.** Owner:
    [Annotated Source Finding provenance](annotated-source-finding-provenance.md).
    A Member-header descriptor for the Member's runtime-async or
    state-machine async classification enters the provenance matrix.
    Prerequisite: step 3.
15. **Member-header admission.** Owner:
    [Annotated Type Map](annotated-type-map.md). The map admits
    Member-header descriptors whose fact is the Member's own declaration,
    under its own adoption and gates. Only after this step does Expectation
    drive a Current mark; steps 3, 4, 6, and 10 present it as a profile-line
    count and trait filter before it. Prerequisite: step 14.

Steps 5 and 6 need no new analysis beyond the facts step 3 issues and
remove most of the explanation burden; they go first after steps 2 and 3
land. Steps 11, 12, and 13 are independent of each other and of the
findings marks. Steps 14 and 15 are the Expectation mark's own path and
block nothing else.

## Non-claims

- It does not define any finding, descriptor, equality, key, or the map's
  production; those belong to their producers and to Annotated Type Map.
- It does not grade a finding. Notable is a presentation selection, and a
  finding outside the set is still reported.
- It does not define the Library-scope one-version map's producer; that is
  Annotated Type Map's adoption step 4.
- It does not define caller populations or relation color; those belong to
  Annotated Type Relations.
- It does not make structure a finding, add it to the map, or claim the
  structure mark is a projection; the structure owners issue it and gate it.
- It does not present unsafe propagation or unsafe implementation color as
  a mark; a bodyless propagating Member has no map row, and that color
  belongs to Annotated Type Relations until the map admits a Member-header
  descriptor for the contract.
- It does not define a filtered Exists or Count terminal for the map; the
  notable predicate at Library scope is the map owner's adoption, and until
  it lands Type rows carry no Current mark and Library Current is
  unavailable.
- It does not add an async classification descriptor or admit Member-header
  descriptors to the map; those are the provenance and map owners'
  adoptions, and until they land Expectation is a profile count and a trait
  filter, not a mark.
- It does not define descriptor chips for the Type inventory or a Library
  aggregate of per-Type occurrence tallies; chips are a Member-list control.
- It does not make a family row a key-join target; the map keys Members, and
  a family mark only expands the family.
- It does not move implementation flags between Fast Diff's axes; that is
  Diff capabilities adoption step 1.
- It does not decide Clone's long-term home.
- It does not add `throw` to the map's vocabulary; the throw column reads
  the existing body signals until the provenance owner admits a throw
  descriptor.
- It does not reproduce an analyzer, ILLink, or a profiler, and its guidance
  links are pointers to platform advice, not verdicts about the inspected
  code.
