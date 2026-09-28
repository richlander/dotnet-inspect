# API and implementation population scope

## Status

This document is the normative owner for the distinction between API
visibility scope and implementation population scope. It records the intended
command contract; it does not change current behavior by itself.

## Authority and exact claim

API and Implementation Population Scope owns:

> An API-level operation selects a declaration population whose default is the
> product's ordinary public-facing API. `--all` is an explicit visibility
> gesture that widens that declaration population. An operation that analyzes
> a declared set of implementations selects its complete admitted
> implementation population by default; it does not use `--all` as a proxy for
> completeness.

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

These routes use their ordinary public-facing declaration population by
default. `--all` widens that population to the command's existing
non-public, hidden, and obsolete declarations. The option changes which API
declarations are eligible; it does not mean "perform every available
analysis."

The default is still applied independently at each operation boundary. A
source API endpoint may use `--all` while a destination or correspondence
operation retains its own declaration contract. A route must document any
asymmetry rather than imply that one flag changes every nested operation.

### Accessibility within API visibility scope

A Type's Member inventory is one declaration population with two independent
visibility axes: accessibility and hidden status. Accessibility is the
`api.accessibility` vocabulary's buckets (`public`, `protected`, `internal`,
and `private`). A composite spelling such as `protected internal` belongs to
the one bucket that vocabulary classifies it into. Hidden status marks
declarations the public-facing default omits for reasons other than
accessibility, such as `EditorBrowsable(Never)`. Obsolete declarations are not
a separate axis: they are admitted and marked, as the default already does.

- The default population is the `public` bucket without hidden declarations:
  the ordinary public-facing API. The Metadata owner's admission decides its
  exact members, and the spelling below decides how records form them.
- An `accessibility` term selects one or more buckets. By itself it does not
  admit hidden declarations.
- `--all` selects every bucket and admits hidden declarations into their
  buckets. Combined with an `accessibility` term, the term narrows the buckets
  and hidden declarations stay admitted.
- `--spelling metadata` selects the metadata spelling defined below, and
  `--spelling csharp` selects the default C# spelling. Spelling chooses the
  row unit, so it is an option rather than a row predicate.

Every admitted declaration belongs to exactly one bucket, so Counts over the
buckets of one population are truthful. They cover the same declarations,
under the same admission rules, as the Rows each bucket would return. The
Metadata owner's admission decides which declarations are compiler-generated.
Those declarations are not API declarations and belong to no bucket. A host
does not narrow the population further by name.

### Spelling within API visibility scope

The same Member population has two spellings. Spelling is independent of
accessibility, hidden status, and `--all`.

**C# spelling** is the default. It composes metadata records into one
declaration wherever some consumer can see them as a single unit:

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
because exactly the consumers who can see that interface can reach it. An
implementation of a public interface, such as `IEnumerator.Current`, belongs to
`public`. An implementation of a non-public interface in the same assembly,
such as `JsonSerializerContext`'s implementation of the internal
`IBuiltInJsonTypeInfoResolver.IsCompatibleWithOptions`, belongs to that
interface's bucket, here `internal`. A member reachable by its own name keeps
its own accessibility even when a MethodImpl also targets an interface member.
A finalizer belongs to `protected`, its declared accessibility. A view shows
each declaration with the parts visible at its selected accessibility:
`public` shows `BytesPending { get; }`, and every bucket shows
`BytesPending { get; private set; }`.

**Metadata spelling** composes nothing. It shows one row per metadata record
of the Type itself (method, property, event, or field), including accessor
methods. A method or field record belongs to the bucket of its own
accessibility flag. A property or event record, which has no flag of its own,
takes the accessibility defined above from its accessors. Attached extension
declarations are records of their declaring Type, so they appear only under C#
spelling.

In both spellings the buckets partition that spelling's population, and every
Count is in that spelling's unit: declarations for C# spelling, records for
metadata spelling.

### Implementation operations with named API roots

An implementation operation may begin by resolving a user-written Type or
Member selector through an API declaration population. That initial lookup and
the later body analysis are separate population decisions:

1. Root resolution uses the ordinary public-facing API population by default.
   `--all` is required when the named root is non-public, hidden, or obsolete.
2. After the root is resolved, the operation analyzes its complete admitted
   implementation population by default. `--all` does not widen traversal,
   select more relationships, remove work bounds, or request every analysis.

The presence of `--all` on a body-oriented command therefore does not make
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

| Question | Default population | Meaning of `--all` |
| --- | --- | --- |
| Which API declarations should an API inventory show? | The ordinary public-facing API | Include the command's non-public, hidden, and obsolete declarations |
| Which accessibility should a Type's Member inventory show? | The `public` bucket without hidden declarations; an `accessibility` term selects other buckets, still without hidden declarations | Every bucket plus hidden declarations; an added `accessibility` term narrows the buckets and hidden declarations stay admitted |
| How should a Type's Member inventory spell its rows? | C# spelling: records compose into declarations wherever a consumer sees a single unit | Unchanged; metadata spelling is a separate, explicit choice |
| Which declarations should an API comparison match? | The comparison's ordinary API population | Widen the API population where that comparison admits the option |
| Which named root should a body operation resolve? | Roots in the ordinary public-facing API | Include a non-public, hidden, or obsolete root; the resulting implementation analysis is unchanged |
| Which method bodies should a whole-library metric summarize? | Every admitted implementation body in the selected library population | Not a completeness switch; the report declares its own implementation population |
| Which bodies should a type/member metric or call analysis inspect? | Every admitted body under the selected typed root | Not a substitute for selecting the root or changing its API visibility |

`--all-libraries` and similar package-population controls are separate
selection mechanisms. They decide which Library occurrences participate; they
do not redefine the meaning of API `--all` inside an already selected
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
- source acquisition, decompilation, or authored-source fidelity;
- call, dependency, or graph topology;
- section names, renderer formats, or JSON schemas; or
- host-specific navigation and follow-up gestures.

Those owners issue the declaration filters, implementation identities,
population receipts, evidence, and presentation contracts that this distinction
connects. This document only prevents an API visibility gesture from being
reused as an implementation completeness claim.

## Adoption

API commands and API Diff retain their existing visibility behavior and should
link this rule when they document `--all`. Library Metrics and other
implementation analyses should state their implementation population directly
and should not add `--all` solely to expose non-public or generated bodies.
When an implementation report offers a follow-up API route, that route may
apply its own visibility defaults and explain the separate `--all` gesture.

Current member-visibility inconsistencies and the transition from
single-Library to aggregate package/API subjects are tracked separately by
[#3589](https://github.com/richlander/dotnet-inspect/issues/3589) and
[#7318](https://github.com/richlander/dotnet-inspect/issues/7318). Those
adoptions may change individual producers without changing this scope
distinction.
