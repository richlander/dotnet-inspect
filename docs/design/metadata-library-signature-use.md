# Metadata Library signature use

## Status and ownership

This focused `ILInspector.Metadata` design is tracked by
[#8827](https://github.com/richlander/dotnet-inspect/issues/8827). It supplies
the signature-use producer required by the
[Library Metrics Type structural-leverage extension](library-structural-report.md#type-structural-leverage)
and consumes the bounded traversal contract in
[Bounded Metadata Traversal](bounded-metadata-traversal.md).

**Metadata Library Signature Use** is the single normative owner established
here. Its exact claim is:

> Given one supported ECMA-335 assembly image and either its complete Type
> population or one exact metadata namespace, enumerate the complete,
> all-accessibility population of named Type-definition occurrences admitted by
> that population, bind occurrences whose targets are definitions in that exact
> image, and publish detached source-to-target evidence with exact image and
> population identity, coverage, work, and visible failures.

This owner defines Metadata construction, population selection, binding,
classification, and qualification only. It does not define Graph degree,
Research namespace roll-up, rankings, designations or roles, CLI rendering,
Browser presentation, or provider-backed acquisition.

The existing Metadata `Signatures` relation family remains member-to-shape
`Accepts` and `Returns` evidence for Subject Relations. Its signature excludes
hierarchy and generic constraints and its endpoints are not this Type-to-Type
population. `TypeDependencyScanner` likewise retains its selected-root,
depth-qualified dependency traversal. Neither contract is broadened or
privately copied here.

## Product question

The operation answers:

> Which Types admitted by this exact whole-Library or exact-namespace
> population are referenced by each other's hierarchy, constraints, and member
> signatures, and how complete was that population?

In abstract form:

```text
AssemblyInspectionSession.LibrarySignatureUses(Request)
  -> Available(Result)
   | Rejected(ImageFailure)
```

The request selects either the whole Library or one exact metadata namespace.
Whole-Library selection admits every Type as a source and retains every local
target occurrence. Exact-namespace selection admits only Types in that
namespace as sources and retains only occurrences whose targets are also in
that namespace. The latter is the producer population for an induced namespace
Type graph; it is not a whole-Library result filtered after execution.

An available result can be complete or partial. Partial means that admitted
sites remain unexamined because malformed, unsupported, or bounded evidence
prevented a complete answer. It never means that those sites produced no
relationships.

## Library and Type identity

One result is bound to the assembly identity, non-empty module MVID, and
selected population read from the same open image. Its canonical Type
inventory contains every admitted TypeDef except the metadata `<Module>`
pseudo-type, independent of accessibility. The empty metadata namespace is a
valid exact namespace; it is distinct from whole-Library selection.

Each Type retains:

- its `MetadataTypeDefinitionAddress`;
- its exact structured `MetadataTypeDefinitionName`;
- its owner-issued definition kind; and
- positive ranking-support classification for universal base, attribute,
  exception, enum, and delegate Types when the image proves it.

Definition kind and positive ranking-support classification remain Metadata
facts. They do not decide whether Research includes a Type in any ranking.
Attribute and exception classification follows a bounded base chain that
reaches a local definition or authenticated core-library root; display text
alone cannot classify a Type. A chain that leaves the image through another
dependency produces no positive attribute or exception classification.

MVID plus TypeDef token is the physical endpoint currency. The structured name
supports deterministic order, display, and same-image TypeRef binding; it does
not replace the address. Duplicate exact definition names reject the image
instead of selecting one.

## Signature-use population

For source Type `A` and target Type `B`, one physical named occurrence produces
`A -> B` when it appears in:

- the base Type of `A`;
- one implemented interface of `A`;
- one generic-parameter constraint declared by `A` or one of its methods;
- a field signature owned by `A`;
- a property Type or indexer parameter owned by `A`;
- an event Type owned by `A`; or
- a method parameter or return Type owned by `A`.

The population includes all admitted declarations, not only public API.
Constructed generics, arrays, pointers, byrefs, function pointers, and
participating custom modifiers contribute each contained named occurrence.
Generic parameters do not themselves name a target definition.

Each occurrence retains:

- exact source and target TypeDef addresses and structured names;
- a closed site kind;
- the physical metadata token that owns the site; and
- the zero-based ordinal of the named occurrence within that site.

The ordinal preserves parallel evidence even when the same target occurs more
than once in one signature. Metadata order determines site and occurrence
order. The producer does not deduplicate `(source, target)` peers, remove self
relationships, rank Types, or aggregate evidence. Those are Graph and Research
decisions over this complete population.

### Local target binding

Only targets defined in the exact inspected image and admitted by the selected
population enter the result:

- a direct TypeDef origin binds by its validated handle;
- current-assembly and equivalent self-assembly references bind by one unique
  exact structured name;
- intrinsic primitive signature codes do not become named Type-definition
  occurrences; and
- foreign assembly and module occurrences are outside the Library-local
  population.

Named occurrences nested inside a constructed shape bind independently. For
`LocalGeneric<LocalArgument>`, both definitions are occurrences. For
`ExternalGeneric<LocalArgument>`, only the local argument enters the
population.

Custom-modifier participation follows `SignatureOccurrenceDecoder`: required
modifier Types participate and optional modifier Types remain retained decoder
evidence but do not become structural-use relationships.

## Completion and result

Coverage accounts for declaration sites, not emitted edges:

```text
considered = examined + unavailable + limited
```

There is no accessibility exclusion. The `<Module>` pseudo-type is outside the
candidate Type population rather than an excluded candidate.

One available result contains:

- an exact image and population receipt plus Metadata operation counters;
- the canonical Type inventory;
- canonical relationship occurrences;
- site coverage;
- a `Complete` or `Partial` disposition; and
- typed diagnostics for every unavailable or limiting site.

`Complete` requires every admitted site to complete without diagnostics.
Classification flags are positive-only facts from the bounded evidence
available in the exact image. An empty complete relationship collection proves
that the admitted Library has no local signature-use occurrences. A malformed
site, unsupported shape, or exhausted operation budget is `Partial`, with
healthy evidence retained and the affected site visible. A failure that
prevents an exact image receipt or trustworthy Type inventory rejects the
operation and publishes no population.

Cancellation propagates with the caller's token. It is not artifact failure.

## Bounds and failure

The operation reuses `SignatureOccurrenceDecoder`,
`AssemblyTypeDeclarationInventory`,
`CoreLibraryRootAuthentication`,
`MetadataTypeDefinitionAddress`, `MetadataOperationPolicy`, and existing
guarded relationship traversal. It does not add a second signature parser,
unbounded TypeSpec walk, or inspected-assembly loading.

The operation charges applicable existing dimensions:

- metadata rows admitted;
- declaration sites considered;
- relationship occurrences retained;
- signature and TypeSpec bytes decoded;
- structured nodes materialized; and
- retained text.

Diagnostics distinguish malformed metadata, unsupported shape, and budget
limits. Each identifies the owning site token when available and retains the
relevant budget dimension, limit, and attempted charge for a limit. Text
detail is diagnostic evidence, not a consumer parsing surface.

Windows Metadata remains unsupported. Product code remains SRM-only,
NativeAOT-friendly, Roslyn-free, and free of inspected-assembly loading.

## Consumer boundary

Metadata owns the selected population identity, Type inventory, physical
occurrence population, local binding, classification, image receipt, coverage,
counters, and diagnostics.

Research consumes the detached result and:

- maps each canonical Type to a Graph node;
- maps every occurrence to a typed Graph relationship;
- rolls a whole-Library population up to namespace leverage;
- selects incoming and outgoing distinct-neighbor degree for one exact
  namespace population;
- applies its explicit self-loop policy;
- decides designation eligibility from Metadata-issued classification;
- derives signature-evidence roles and designations; and
- preserves Metadata qualification in the report.

Research never reopens metadata, binds a TypeRef by display text, repairs an
incomplete population, or treats a missing edge as an examined zero.

Query composition selects the whole-Library or exact-namespace producer
population before work starts. It must preserve this closed-document result's
identity, evidence, completion, ordering, and failures; a post-execution filter
is not a namespace shard. These operations are reference semantic terminals,
not temporary host implementations.

## Before/after evidence

Stage 3 in [#8825](https://github.com/richlander/dotnet-inspect/issues/8825)
has explicit endpoints:

- **Before:** a frozen, #8732-derived direct SRM reference extractor in the
  performance-oracle harness.
- **After:** this Metadata-owned product operation.

Both routes run at one frozen candidate head over System.Text.Json 10.0.0 and
the .NET 11 RC1 System.Private.CoreLib. On their shared supported scope, the
gate compares exact canonical occurrence identities and completion before
reporting warm median time and allocation. The after route additionally
records Metadata operation counters.

This is a same-head implementation before/after. The older public-Type-only
prototype timings are motivating evidence, not an equivalent-output baseline.
An isolated after cost cannot replace the required comparison.

## Required evidence

Release fixtures prove:

- every site kind and all-accessibility participation;
- nested definitions, generic definitions, constructed shapes, constraints,
  function pointers, arrays, pointers, byrefs, and modifier participation;
- parallel evidence and self relationships;
- exact current-image binding, intrinsic primitive exclusion, and
  foreign-target omission;
- duplicate-definition rejection;
- malformed signature and relationship handling;
- budget-limited partial completion; and
- detached deterministic results after session disposal.

System.Text.Json 10.0.0 proves a representative package path.
System.Private.CoreLib from SDK `11.0.100-rc.1.26425.128` proves scale,
all-accessibility participation, deterministic completion, and the Stage 3
before/after measurement.
