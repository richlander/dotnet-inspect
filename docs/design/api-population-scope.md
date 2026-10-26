# API and implementation population scope

## Status

This document is the normative owner for the distinction between API
visibility scope and implementation population scope. It records the intended
command contract; it does not change current behavior by itself.

## Authority and exact claim

API and Implementation Population Scope owns:

> An API-level operation selects a declaration population whose default is the
> product's public-facing API, decided by accessibility alone. `--include` is
> an explicit visibility gesture that widens that population by accessibility;
> `--skip` and `--only` are explicit attribute filters. An operation that
> analyzes a declared set of implementations selects its complete admitted
> implementation population by default; it does not use a visibility gesture
> as a proxy for completeness.

It also owns the boundary between declaration and contextual populations:

> A Type's declaration population contains only declarations physically owned
> by that Type. C# spelling may compose same-owner metadata records into one
> declaration; metadata spelling may retain those records separately. Neither
> spelling imports inherited declarations or extension declarations physically
> owned by another Type. Those belong to separately requested contextual
> populations whose identity, completion, and failures do not alter the base
> declaration population.

This is a semantic distinction, not a performance mode. A producer may still
apply its own bounded-work policy, and it must retain visible incomplete or
unavailable outcomes when the declared population cannot be fully evaluated.

## Two meanings that must remain separate

### API visibility scope

API commands answer questions about declarations that users can name or
compare:

- `type` and `member` API inventories;
- API-oriented `find` and `match` operations;
- API compatibility and declaration correspondence; and
- sections whose rows are API declarations.

These routes use their public-facing declaration population by default, as
[Accessibility within API visibility scope](#accessibility-within-api-visibility-scope)
defines it. `--include` widens that population by accessibility, and `--skip`
and `--only` filter it by attribute facts. These options change which API
declarations are eligible; they do not mean "perform every available
analysis."

The default is still applied independently at each operation boundary. A
source API endpoint may use `--all` while a destination or correspondence
operation retains its own declaration contract. A route must document any
asymmetry rather than imply that one flag changes every nested operation.

### Declaration ownership and contextual populations

A declaration population is stable for one admitted Type definition and
visibility scope. Adding another workspace assembly, changing imports, or
resolving another base Type cannot add members to it. Properties, events, and
explicit interface implementations may compose several metadata records, but
every record that forms the declaration is physically owned by the selected
Type.

Two useful populations are therefore contextual rather than declarative:

- an inherited-member population projects declarations physically owned by
  base Types or interfaces into the context of the selected Type; and
- an extension-member population projects extension declarations physically
  owned by their declaring Types toward a receiver Type.

A consumer may request and present those populations beside declared members.
That composition does not rewrite the declaration population. Every projected
member retains both the selected API target and its physical declaring owner,
and every contextual population retains completion and failure evidence
separate from the declaration population. If an explicitly requested
contextual population is unavailable, the composed request is not complete;
the already completed declaration population does not become incomplete or
silently absorb an empty contextual result.

Spelling chooses how records already admitted to one population form rows. It
does not select another population. In particular, placing an extension under
its receiver or an inherited member under its derived Type is contextual
projection, not C# spelling.

### Accessibility within API visibility scope

**Accessibility is the arbiter.** A declaration's accessibility alone decides
whether it is in a public-facing population. Accessibility is the
`api.accessibility` vocabulary's buckets (`public`, `protected`, `internal`,
and `private`). A composite spelling belongs to the one bucket that vocabulary
classifies it into, and no bucket contains another: `public` does not include
`protected`, and `protected` does not include `private protected`.
`protected internal` is in the `protected` bucket. `private protected` is in
the `private` bucket, because its `private` part wins: no other assembly can
reach it, even by deriving (decided 2026-10-26 on
[#10693](https://github.com/richlander/dotnet-inspect/issues/10693)). The
`private` term selects it with the rest of that bucket, and the
`private protected` spelling selects it alone; neither `protected` nor
`internal` includes it.

**Attributes are facts, not admission.** `EditorBrowsable(Never)` and
`[Obsolete]` are independent status facts about a declaration. Each is read on
its own, listed on the declaration's type or member page, and marked on its
rows. Neither removes a declaration from any population by default, and no
rule treats them together. They are different statements and are used in
opposite directions:

- `EditorBrowsable(Never)` is a presentation hint, usually on infrastructure a
  caller should not reach for. A reader removes it on request:
  `--skip EditorBrowsableNever`.
- `[Obsolete]` is a contract statement that the API may be removed. A reader
  asks for it on request, for example to see which deprecated APIs a codebase
  depends on: `--only Obsolete`.

Contract strength runs public > obsolete > `EditorBrowsable`. Nesting treats
accessibility and attribute facts differently. An enclosing Type's
accessibility limits a nested Type's reach: a public Type nested in a private
Type is unreachable from another assembly, as the Type admission below states.
An enclosing Type's attribute facts do not pass to the Types it nests: a public
Type nested in an `EditorBrowsable(Never)` or `[Obsolete]` Type carries neither
fact unless it declares it (decided 2026-10-25 in
[#10642](https://github.com/richlander/dotnet-inspect/issues/10642)).
Which declarations carry each fact is the
[API status analysis](api-status-analysis.md#authority-and-exact-claim) owner's
rule, including its exemption for the compiler's compatibility `[Obsolete]`
markers.

- The default population is the `public` and `protected` buckets: everything
  another assembly can reach, by calling or by deriving. It is the population reference documentation
  lists and the one whose changes can break another assembly, so it is the
  same population the [diff's API axis](#the-diffs-api-axis-population)
  compares (decided 2026-10-26 on
  [#10693](https://github.com/richlander/dotnet-inspect/issues/10693)). The
  Metadata owner's admission decides its exact members, and the spelling
  below decides how records form them.
- A Type is in the default population only when it and every enclosing Type is
  in the `public` or `protected` bucket. A public nested Type of a protected
  Type is reachable by derivation, so it is in the default population. No
  other assembly can reach a Type nested in an internal or private Type,
  including a `private protected` one, so such a Type and its Members leave
  the default population; `--include all` still lists them. A
  reader that means another population, such as the row-local surface
  classification count, says so in its owner.
- `--include <term>` widens the population by accessibility. Its fixed terms
  are the bucket names `internal` and `private`, which add that whole bucket,
  and `all`, which selects every bucket. It never changes attribute filtering.
- `--skip <term>` removes declarations carrying an attribute fact. Its fixed
  term set starts with `EditorBrowsableNever`.
- `--only <term>` keeps only the named selection. Its fixed terms are the
  accessibility terms, which `--only` shares with the `accessibility` term
  below, and the attribute facts, starting with `Obsolete`.
  `--only public` returns no `protected` member.
- The accessibility terms are the four bucket names and the `private protected`
  spelling, a narrower selection inside the `private` bucket. An
  `accessibility` term, for example `--where "accessibility=private"`, and
  `--only` accept the same accessibility terms, and several combine as a
  union. Attribute facts then narrow the result.
- An unknown term is a usage error that lists the term set. `--skip` and
  `--only` never name the same term.
- `--spelling metadata` selects the metadata spelling defined below, and
  `--spelling csharp` selects the default C# spelling. Spelling chooses the
  row unit, so it is an option rather than a row predicate.

One admission rule covers every combination. The *reach set* is the default
buckets, every bucket `--include` adds, and the bucket containing each
accessibility term named through `--only` or an `accessibility` term (the
`private` bucket for `private protected`). A declaration is admitted
when it and every enclosing or declaring Type is in the reach set. When an
accessibility term is named, only declarations whose own bucket or spelling it
names are kept; `--skip` and an attribute term in `--only` then filter by fact.
So `--include internal` admits an internal Type with its public Members and
public nested Types; `--only private` keeps a private nested Type of a public
Type and the private Members of public Types; and, combined with
`--include all`, an accessibility term narrows the rows without narrowing
reach.

Every admitted declaration belongs to exactly one bucket, so Counts over the
buckets of one population are truthful. They cover the same declarations,
under the same admission rules, as the Rows each bucket would return. The
Metadata owner's admission decides which declarations are compiler-generated.
Those declarations are not API declarations and belong to no bucket. A host
does not narrow the population further by name.

**One implementation.** This policy is implemented in one place. One Metadata
admission takes a declaration's accessibility, its enclosing Types' reach, and
the request's `--include`, `--skip`, and `--only` selection, and answers
whether the declaration is in the population. One Metadata status reader
reads `EditorBrowsableNever` and `Obsolete` as two independent facts. Every
reader that forms an API population calls them: Type and Member listings and
lookup, counts, Find and its indexes, the hierarchy, dependency, extension,
integration, telemetry, and opportunity scanners, Library Info, the Browser
exports, Analysis planning, and the diff's presentation. No reader
re-implements either, decides admission from an attribute directly, or ORs the
two facts. A layering gate enforces that only the admission and the status
reader call the attribute rule. The CLI and Browser parse their options into
the same typed selection; neither host spells the policy.

`--all` is the legacy spelling of `--include all`. Today it also admits hidden
and obsolete declarations; under this rule it implies no attribute filtering in
either direction, and the adoption below retires it.

### Spelling within API visibility scope

The same Member population has two spellings. Spelling is independent of
accessibility, attribute facts, `--include`, `--skip`, `--only`, and contextual
population selection.

**C# spelling** is the default. It composes metadata records into one
declaration wherever the same Type physically owns the records and a consumer
sees them as a single unit:

- A property or event and its accessors are one declaration, even when one
  accessor is narrower. Code inside the class sees
  `Utf8JsonWriter.BytesPending { get; private set; }` as one property.
- An explicit interface implementation's property or event record and its
  accessor methods are one declaration. Any holder of the interface sees
  `IEnumerator.Current` as one property.
- Each method overload stays its own declaration. A family of overloads is
  grouped by name, not composed.

A property's or event's accessibility is the widest of its accessors'
accessibilities, joined in the ECMA-335 accessibility order: `private` is
narrowest, `private protected` is narrower than both `protected` and
`internal`, those two join to `protected internal`, and `public` is widest.
This is C#'s declared accessibility for the property or event. The
`api.accessibility` vocabulary then assigns the bucket. For example,
`JsonConverter.RequiresReadAhead { internal get; private protected set; }` is
`internal`. A narrower accessor is part of that declaration's shape, not a
separate member.

An explicit interface implementation is a private member whose MethodImpl
declaration is a member of an interface. It keeps its member kind (method,
property, or event) and belongs to the bucket of the interface it implements,
because exactly the consumers who can see that interface can reach it. That
bucket is the `api.accessibility` classification of the interface's declared
accessibility. An interface referenced from another assembly is public to this
assembly's consumers, so no other assembly is resolved. An
implementation of a public interface, such as `IEnumerator.Current`, belongs to
`public`. An implementation of a non-public interface in the same assembly,
such as `JsonSerializerContext`'s implementation of the internal
`IBuiltInJsonTypeInfoResolver.IsCompatibleWithOptions`, belongs to that
interface's bucket, here `internal`. A non-private member keeps its own
accessibility even when a MethodImpl also targets an interface member.
A finalizer belongs to `protected`, its declared accessibility.

When an extension population projects a declaration toward its receiver, its
effective accessibility is the narrower of its own accessibility and its
declaring Type's, because only consumers who can see the declaring Type can
call it. That classification belongs to the contextual extension population,
not to the receiver's declaration buckets. For example, the public method
`JsonHelpers.ReadWithVerify` of System.Text.Json's internal `JsonHelpers` is
an `internal` projection toward `Utf8JsonReader` while remaining a declaration
of `JsonHelpers`. Its C# spelling keeps the declared modifier, as
`public static void ReadWithVerify(this ref Utf8JsonReader reader)`. A view
shows each declaration with the parts visible at its selected accessibility:
`public` shows `BytesPending { get; }`, and every bucket shows
`BytesPending { get; private set; }`.

**Metadata spelling** composes nothing. It shows one row per metadata record
of the Type itself (method, property, event, or field), including accessor
methods. A method or field record belongs to the bucket of its own
accessibility flag. A property or event record, which has no flag of its own,
takes its accessors' join defined above, not its interface's bucket. An
extension method record appears under its declaring Type; projecting it under a
receiver is not a spelling.

Hidden status also remains record-local under metadata spelling. A hidden
property or event record does not hide its accessor MethodDef records; each
accessor carries its own hidden fact, and `--skip EditorBrowsableNever` applies
to each record alone.

In both spellings the buckets partition that spelling's population, and every
Count is in that spelling's unit: declarations for C# spelling, records for
metadata spelling.

### Implementation operations with named API roots

An implementation operation may begin by resolving a user-written Type or
Member selector through an API declaration population. That initial lookup and
the later body analysis are separate population decisions:

1. Root resolution uses the public-facing API population by default.
   `--include` (today `--all`) is required when the named root is internal or
   private, including `private protected`; a protected, hidden, or obsolete
   root resolves without it.
2. After the root is resolved, the operation analyzes its complete admitted
   implementation population by default. `--include` does not widen
   traversal, select more relationships, remove work bounds, or request every
   analysis.

The presence of `--include` on a body-oriented command therefore does not make
implementation completeness opt-in. It widens only the API boundary used to
resolve the requested root.

### Implementation population scope

Implementation analysis answers questions about bodies or physical evidence
associated with an admitted set of APIs. Examples include:

- library-wide `Library Metrics`;
- type- and member-level implementation metrics;
- implementation-oriented Diff;
- calls, call graphs, and other body-derived relationship evidence; and
- distributions, coverage receipts, or outlier reports over implementations.

These operations include public, internal, private, and compiler-generated
bodies when those bodies belong to the operation's declared implementation
population. Their completeness boundary is the operation's selected
Library, Type, Member, endpoint pair, or other typed root—not the API
visibility default.

An aggregate implementation result that identifies a private or
compiler-generated body should retain typed identity and provide a direct
drill-down or an explicit transition to an API command. It should not require
the user to infer from the aggregate result that `--all` was needed to make
the implementation analysis complete.

## Decision table

| Question | Default population | Widening and filtering |
| --- | --- | --- |
| Which API declarations should an API inventory show? | The `public` and `protected` buckets, with `EditorBrowsable(Never)` and `[Obsolete]` declarations admitted and marked | `--include` adds accessibility buckets; `--skip EditorBrowsableNever` and `--only Obsolete` filter by attribute fact |
| Which accessibility should a Type's Member inventory show? | The `public` and `protected` buckets; an `accessibility` term or `--only` on its own selects buckets | `--include` adds buckets; an `accessibility` term combined with `--include` narrows them |
| Which members belong to a Type's declaration population? | Declarations physically owned by that Type; inherited and extension members require separately requested contextual populations | Widen only the visibility of the declaration population; do not request contextual populations |
| How should a Type's Member inventory spell its rows? | C# spelling: same-owner records compose into declarations wherever a consumer sees a single unit | Unchanged; metadata spelling is a separate, explicit choice |
| Which declarations should an API comparison match? | A diff's API axis compares the default population: the `public` and `protected` buckets; attribute facts never decide what is compared | Widen the API population where that comparison admits `--include` |
| Which named root should a body operation resolve? | Roots in the default population | `--include` resolves a root outside it (internal or private, including `private protected`); the resulting implementation analysis is unchanged |
| Which method bodies should a whole-library metric summarize? | Every admitted implementation body in the selected library population | Not a completeness switch; the report declares its own implementation population |
| Which bodies should a type/member metric or call analysis inspect? | Every admitted body under the selected typed root | Not a substitute for selecting the root or changing its API visibility |

`--all-libraries` and similar package-population controls are separate
selection mechanisms. They decide which Library occurrences participate; they
do not redefine the meaning of API `--include` inside an already selected
Library.

## Population and identity

An implementation population must retain the evidence needed to explain what
was analyzed:

- physical body identity and logical source ownership remain distinct;
- compiler-generated bodies remain distinguishable from attributed source
  members;
- package, Library, endpoint, target-framework, and provenance context remain
  owner-issued; and
- incomplete, unavailable, or not-evaluated bodies remain visible in the
  operation's coverage result.

Display visibility is not an identity contract. A private member hidden from an
API inventory can still be a valid implementation row, and a compiler-created
physical body can be valid evidence even when no ordinary API declaration
names it. Producers must not reconstruct implementation identity from a
display name or from whether an API row happened to be visible.

Declaration and contextual API populations likewise retain distinct identity.
A contextual projection preserves its selected API target, physical declaring
owner, and population receipt. A composed view does not infer those identities
from where a row is displayed, and it does not combine their Counts unless the
composition owner explicitly defines and completes that aggregate population.

## Progressive disclosure

The cost of a population is controlled by the operation's explicit section,
verbosity, capability, and work-bound policies. The product must not overload
`--all` with an unrelated request for exhaustive implementation analysis:

```console
# API visibility: include declarations outside the public-facing default.
dotnet-inspect member JsonSerializer \
  --package System.Text.Json Serialize:1 --all \
  -S Callers

# Implementation population: summarize the selected Library's admitted bodies.
dotnet-inspect library ./artifacts/bin/ILInspector.Analysis/release/ILInspector.Analysis.dll \
  -S "Library Metrics"
```

The second command does not need `--all` to include private or
compiler-generated bodies in its metric population. If a user follows a
metric result into an API-level command, that separate command may require
`--all` to resolve a non-public declaration. That is a navigation concern, not
a change to the aggregate report's denominator.

The same phase distinction applies within one command. For example,
`graph calls` may require `--all` to resolve a non-public starting member.
After resolution, the call graph still analyzes the complete admitted body
population and applies its ordinary traversal, relationship, and work-bound
request.

## Boundaries

This owner does not define:

- which declarations are public, hidden, obsolete, or compiler-generated;
- package asset selection, Library aggregation, or target-framework selection;
- implementation-profile metric formulas or body correspondence;
- contextual-population traversal, applicability, ordering, or index shapes;
- source acquisition, decompilation, or authored-source fidelity;
- call, dependency, or graph topology;
- section names, renderer formats, or JSON schemas; or
- host-specific navigation and follow-up gestures.

Those owners issue the declaration filters, implementation identities,
population receipts, evidence, and presentation contracts that this distinction
connects. This document only prevents an API visibility gesture from being
reused as an implementation completeness claim.

### The diff's API axis population

A diff's API axis, as [Diff capabilities](diff-capabilities.md#axes) defines it,
compares the declarations another assembly can reach: the `public` and
`protected` buckets, less `private protected` declarations while the
vocabulary still places them in `protected`, with every enclosing Type in the
same buckets. Under
[Accessibility within API visibility scope](#accessibility-within-api-visibility-scope)
that is exactly the default population, not a combination of `--include` and
an accessibility term. Hidden declarations stay admitted into their buckets. `EditorBrowsable` and `Obsolete`
status do not decide what the diff compares; a separate [API status
analysis](api-status-analysis.md) reports them and their transitions, as Diff
capabilities states. A Type is admitted when its own row and every enclosing
Type's row is in an API axis bucket, so neither a public nested Type of a
non-public Type nor a public Member of an internal Type is admitted: no other
assembly can reach either one. Each Type remains its own row; a nested Type does
not inherit its enclosing Type's changes. Metadata issues this population as the
API axis surface (`ApiSurfaceExtractionScope.ApiAxis`); Finding Diff Any's API
census admits the same declarations. The API comparison reports an accessibility
move between API axis buckets when both compared surfaces are API axis surfaces,
as each surface records its extraction scope. Under `--all` (`IncludeAll`),
every accessibility move between the two sides is a Change, including moves that
leave or enter the API axis; it is Breaking when external reach narrows,
Additive when it widens, and NonBreaking when external reach is unchanged.
External reach counts callers in other assemblies that are not friends, so
`protected internal` and `protected` have the same reach: a move between them is
NonBreaking on the API axis as well. Breaking and Additive describe the public
contract, the API axis: under `--all`, any change wholly outside the API axis on
both sides, judged by external reach through every enclosing Type, is
NonBreaking. Comparisons of other scopes keep their existing output.

A property's C# spelling states each accessor's accessibility relative to the
property's own, so its signature can change when no accessor's reach does. When
a matched Member's old and new signatures differ only in that accessor
spelling, with the same accessors and the same declaration otherwise, the
comparison classifies the signature change by each accessor's own external
reach, as it classifies an accessibility move: Breaking when any accessor's
reach narrows, including an accessor that leaves the externally reachable
population, Additive when reach only widens, and NonBreaking when no
accessor's reach changes. Unlike the accessibility move above, this holds in
every comparison scope, because every scope reports the signature row. The
change stays one signature row, beside the property's own accessibility move when
the widest accessor moves. Every other signature change, including an
accessor added or removed and any change to a declaration modifier the
signature does not spell, such as `init`, `static`, `virtual`, `override`,
`sealed`, `abstract`, or `readonly`, stays Breaking. An event's signature spells no
accessor, so it never takes this rule.

For example, `protected int V { get; set; }` to
`public int V { get; protected set; }` widens the getter and keeps the setter
protected, so its signature row is Additive beside the Additive move, and the
reverse is Breaking. `{ get; set; }` to `{ get; protected set; }` and
`{ get; protected set; }` to `{ get; private set; }` narrow the setter and are
Breaking; `{ get; protected set; }` to `{ get; protected internal set; }` is
NonBreaking. Real version pairs carry this shape: Avalonia 11.3.14 to 12.1.2
widens `FlyoutBase.IsOpen` from `{ get; protected set; }` to
`{ get; set; }` (Additive), and System.Text.Json 6.0.0 to 8.0.6 moves
`JsonTypeInfo<T>.SerializeHandler` from `{ get; private protected set; }` to
`{ get; internal set; }`, which neither side's setter lets another assembly
reach (NonBreaking). The rule was adopted on 2026-10-25 in
[#10628](https://github.com/richlander/dotnet-inspect/issues/10628), where
the operator delegated the choice.
`FindingDiffAnyTests` and `LibraryApiDiffCommandTests` gate it.

The ordinary population is the presentation default for diff output: a host
shows it unless the user selects more, and it filters by each compared
declaration's accessibility rather than by narrowing the comparison. Today it
also leaves out hidden declarations. Under
[Accessibility within API visibility scope](#accessibility-within-api-visibility-scope)
it becomes the default population, the same `public` and `protected` buckets
the API axis compares, with hidden and obsolete rows marked, and
`--skip EditorBrowsableNever` leaves hidden rows out on request. Rows outside
it remain only under `--include`.

## Adoption

API commands retain their existing visibility behavior and should link this rule
when they document `--all`. API Diff compares the diff's API axis population
above and presents the ordinary population by default. The comparison, Finding
Diff Any's census, and the CLI and Inspect Web Library API Diff compare it, and
those hosts filter it for presentation as [Library API diff
presentation](library-api-diff-presentation.md#ordinary-presentation) owns. The
other diff routes still compare the Public scope directly. Library Metrics and
other implementation analyses should state their implementation population
directly and should not add `--all` solely to expose non-public or generated
bodies. When an implementation report offers a follow-up API route, that route
may apply its own visibility defaults and explain the separate `--all` gesture.

[#10693](https://github.com/richlander/dotnet-inspect/issues/10693) adopts
[Accessibility within API visibility scope](#accessibility-within-api-visibility-scope).
Today 109 sites read the two attributes. 39 of them filter, under two
conflated rules: Never-or-Obsolete for Types and Never-only for Members. The
adoption runs in this order, each step with population evidence and NativeAOT
timing:

1. Separate the two facts. `[Obsolete]` stops suppressing any declaration, and
   every rule that combines the two facts is deleted.
2. Route every filter through the one admission and every mark through the one
   status reader, keeping today's `EditorBrowsable(Never)` default, behind a
   layering gate.
3. Change the default in the admission: `private protected` moves to the
   `private` bucket in the `api.accessibility` vocabulary, which gains the
   `private protected` spelling as a narrower selection
   ([Vocabulary](vocabulary.md), `ApiAccessibilityVocabulary`,
   `ApiAccessibility.Classify`, and Metadata's `AccessibilityBucket`); the
   default population becomes the `public` and `protected` buckets;
   `EditorBrowsable(Never)` declarations are admitted;
   `--skip` and `--only` are added; and the
   diff's ordinary view shows the API axis population with hidden rows
   marked. The owners that
   state today's defaults adopt the rule in the same step:
   [Type-declaration visibility](type-declaration-visibility.md) (the
   locator's `EditorBrowsableNever` and `Obsolete` restrictions),
   [Library API diff presentation](library-api-diff-presentation.md#per-side-facts)
   (ordinary sides exclude hidden), [API status analysis](api-status-analysis.md)
   (hidden rows counted outside), [Find](find-search-service.md) (default
   suppression of hidden and obsolete definitions), and
   [Member accessibility composition](member-accessibility-composition.md)
   (the bucket map's default buckets and hidden admission). The
   [Diff capabilities](diff-capabilities.md) definition of the API axis as
   "`--all` narrowed by an `accessibility` term", and its "less
   `private protected`" wording, are replaced by the default population.
4. `--include` replaces `--all` in every command definition and in the
   [CLI reference](../cli-reference.md).

[#10694](https://github.com/richlander/dotnet-inspect/issues/10694) makes Type
lookup one path that consumes the admission.
[#10695](https://github.com/richlander/dotnet-inspect/issues/10695) lists each
Type's and Member's attributes with faithfully decoded values.

Current member-visibility inconsistencies and the transition from
single-Library to aggregate package/API subjects are tracked separately by
[#3589](https://github.com/richlander/dotnet-inspect/issues/3589) and
[#7318](https://github.com/richlander/dotnet-inspect/issues/7318). Those
adoptions may change individual producers without changing this scope
distinction.

[#9180](https://github.com/richlander/dotnet-inspect/issues/9180) locks the
declaration/contextual population boundary. Assembly Inspection adopts it by
making its declaration model stable and declaration-only. Inherited- and
extension-population owners then define their own typed results and
completeness contracts. CLI and Browser adoption consume those same
host-neutral populations; neither host reconstructs or mutates the declaration
population.
