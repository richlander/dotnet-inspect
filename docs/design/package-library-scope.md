# Package library scope

## Status

This document is the normative owner for Package Library Scope, tracked by
[#8302](https://github.com/richlander/dotnet-inspect/issues/8302).

The contract remains a target policy adopted independently by each operation.
Existing aggregate package surfaces provide supporting design evidence.
Package Query `library-literal` is the first gated adopter under
[#8297](https://github.com/richlander/dotnet-inspect/issues/8297); its owning
designs and tests prove only that focused implementation-role adoption, not
repository-wide conformance.

## Authority and exact claim

Package Library Scope owns:

> Given one package-content generation, one selector-issued target and optional
> runtime projection, one selector-issued ordered asset-role population, and
> one operation whose answer depends on library evidence, classify that
> operation as aggregate or exact scope and preserve the completeness meaning
> of that choice.

An **aggregate** operation uses every admitted Library occurrence in the
declared role population. An **exact** operation uses one explicitly identified
Library occurrence in that population.

When a package-grain question depends on library evidence, it uses aggregate
scope. A library-grain or coordinate-sensitive question uses exact scope.
A primary, namesake, or first-Library recommendation may help a host enter an
exact-only operation, but it is not an implicit package-grain scope.

This is a semantic classification, not a performance mode. Execution may be
sparse, sequential, streamed, short-circuited where the result contract
permits it, and bounded by the operation's existing work policy.

## Selection order

The product makes three separate decisions:

1. Package asset selection chooses one target and optional runtime projection.
2. The operation declares the asset role whose evidence answers its question.
3. Package Library Scope selects aggregate or exact scope within that
   owner-issued role population.

Target selection therefore precedes library scope. Aggregate scope never
combines incompatible target-framework groups.

The asset role determines what "all libraries" means. The scope owner consumes
the exact ordered role population issued by package selection; it does not
enumerate archive paths, infer Libraries from file extensions, or build a
union of unrelated roles.

For example:

- a compile-surface operation consumes the selected compile population;
- an implementation-body operation consumes the selected implementation
  population for the selected target and runtime context; and
- a relationship operation that requires shared binding context consumes a
  role realization owned by the relationship or realization design.

Surface and implementation images that correspond to one Library are not
independent evidence merely because both occur in the package. Package asset
selection and role realization retain ownership of correspondence, role
preference, implementation overlays, reference-only participants, and
implementation-only participants.

## Scope classification

### Aggregate scope

Aggregate scope applies when library evidence answers a question about the
Package:

- package library inventory and roll-up sections;
- package-wide containment or existence predicates;
- complete package evidence rows drawn from library contents; and
- package-level summaries whose declared population is a library role.

The result may retain one Package as its item grain while evaluating many
Libraries. Evaluation grain, evidence-row grain, and result-item grain remain
separate.

An aggregate operation preserves the producing Library occurrence on every
piece of evidence. Aggregation does not flatten Library provenance into a
synthetic assembly identity.

### Exact scope

Exact scope applies when the selected Library is part of the question:

- one Library's metadata, API, implementation, source, or coordinates;
- an operation whose input already identifies one exact Library occurrence;
  or
- an inspector whose contract cannot produce a meaningful aggregate answer.

An exact operation consumes one owner-issued Library occurrence. It does not
select another Library when that occurrence is missing, incompatible,
unavailable, or ambiguous.

Hosts may recommend an exact Library when transitioning from an aggregate
subject into an exact-only operation. Recommendation and transition behaviors
belong to Navigation or the adopting host-neutral operation; they do not change
this owner's aggregate and exact meanings or establish package-wide evidence.

## Completeness

Aggregate completion depends on the shape of the promised answer.

### Existence predicates

An existence predicate may stop when one Library supplies decisive positive
evidence. That result proves existence, not complete population coverage. Its
terminal accounting records the decisive completion and any role occurrences
that were not evaluated.

A negative aggregate verdict requires successful exhaustion of every admitted
occurrence in the role population. An acquisition, admission, decode,
evaluation, cleanup, cancellation, or work-limit failure prevents an
unqualified package-wide negative verdict.

### Complete evidence

An operation that promises every evidence row exhausts the declared role
population even after finding its first row. A partial row collection may
remain useful when its owning operation permits partial publication, but it
must carry visible incomplete completion and failures. It cannot be presented
as the complete aggregate.

Repeated semantic values in different Library occurrences or at different
coordinates remain separate evidence when the producing operation defines
physical occurrence as its row identity. Aggregate scope does not add
value-based deduplication.

### Exact completion

An exact operation reports completion only for its selected occurrence.
Its positive, negative, unavailable, and failed outcomes make no claim about
sibling Libraries in the same role population.

## Bounds and execution

Cost does not silently narrow aggregate scope to a representative Library.
An adopting operation instead uses its ordinary mechanisms:

- finite candidate and work bounds;
- per-Library and aggregate budgets;
- deterministic owner-issued role order;
- sparse acquisition and disposal;
- explicit cancellation and deadlines; and
- typed not-evaluated, incomplete, or work-limit outcomes.

Package candidate selection may stop after enough package results have been
established. Within one candidate, the promised answer shape determines
whether Library evaluation can stop early.

Aggregate scope does not require eager full-Workspace realization. An
operation may inspect and release one Library at a time when its semantics do
not require shared cross-Library binding context.

## Inputs and retained evidence

The scope owner consumes, without reconstructing:

- package-content generation and selection correspondence;
- requested and selected target context;
- requested and selected runtime context when applicable;
- the exact ordered role population;
- canonical Library occurrence identity; and
- owner-issued selection, correspondence, and failure outcomes.

An aggregate result retains enough accounting to distinguish:

- role occurrences selected for the operation;
- occurrences successfully evaluated;
- occurrences not evaluated because of decisive short-circuiting or bounds;
- occurrences that were not applicable; and
- occurrences that failed.

Exact results retain the selected occurrence and make no sibling-coverage
claim. Display names, paths, and assembly names may explain a scope result but
do not reconstruct occurrence identity.

## Boundaries

Package Library Scope does not define:

- target-framework parsing, compatibility, ranking, or fallback;
- runtime-identifier selection or overlay policy;
- package archive admission or managed-image classification;
- compile, implementation, resource, analyzer, or build asset membership;
- surface-to-implementation correspondence;
- package acquisition, participant lifetime, or disposal;
- cross-Library binding, dependency, call-graph, or relationship semantics;
- operation-specific evidence, row identity, result grain, or rendering;
- host navigation, exact-Library recommendation, or transition behavior; or
- operation-specific candidate, time, memory, decode, or row bounds.

Those owners issue the role population, identities, evidence, limits, and
failures that this policy classifies and preserves.

The scope owner does not require every Package operation to inspect Libraries.
Manifest, source-catalog, dependency-declaration, archive-file, and other
package evidence retains its existing owner and population.

## Supporting designs and precedents

[Package asset-selection correspondence](package-asset-selection-correspondence.md)
owns complete selected compile and implementation populations and explicitly
keeps the legacy `DefaultAsset` convenience outside its aggregate projection
claim.

[Inspection Subject Navigation](inspection-subject-navigation.md) owns the
target aggregate-first Package subject and exact or namesake narrowing for
structural inspection. Its recommendation behavior is a consumer of this
policy, not its authority.

[Package Query assembly evaluation](package-query-assembly-evaluation.md)
records one selected asset, its exact role occurrence, and the count of
unevaluated siblings. Aggregate patterns compose that one-asset evaluator over
their declared role population; patterns that have not adopted this policy
retain their explicit selected-asset contract.

[SourceLink Exposure](../sourcelink-exposure.md) supplies an existing
package-aggregate precedent: package SourceLink sections evaluate selected
Libraries while retaining Library provenance and visible unavailable or failed
rows.

Package workspace Integration queries provide the neighboring relational
precedent. They use role realization and correspondence-aware deduplication
because their answer depends on shared package binding context rather than
independent Library scans.

[NuGet's package folder conventions][nuget-multitargeting] are supporting
ecosystem evidence: framework selection chooses an asset group that may contain
multiple assemblies. They do not assign architectural primacy to the assembly
whose simple name matches the package ID.

## Adoption

Each existing owner adopts this policy independently and records its
operation-specific role, scope, answer shape, bounds, and completion gates.
This document does not change current behavior merely by being present.

The first adoption is Package Query `library-literal` under #8297:

- the question remains package-grain;
- decoded `ldstr` evidence requires the implementation-body role;
- the explicit `Literal Strings` section promises complete occurrence rows;
- evaluation therefore uses aggregate scope and exhausts the selected
  implementation population for each evaluated package candidate; and
- every occurrence retains its producing Library and IL coordinates.

That adoption belongs to the existing
[Package Query library-literal](package-query-library-literal.md) and
[Package Query assembly evaluation](package-query-assembly-evaluation.md)
owners. It must replace their primary-library claims and add implementation
gates; this policy document does not redefine those internals.

Later Navigation, SourceLink, package command, Browser, and relationship
adoptions remain separately scoped. Existing aggregate behavior is evidence,
not an automatic conformance claim.

## Verification

This docs-only policy has no implementation gate. Its claims remain
**unverified** until an adopting owner names Release tests that prove:

- aggregate and exact classification for that operation;
- the exact selector-issued role population;
- producing-Library provenance;
- truthful positive, negative, failed, and not-evaluated completion;
- decisive short-circuiting only where the answer shape permits it; and
- no representative-Library fallback for an aggregate question.

[nuget-multitargeting]: https://learn.microsoft.com/nuget/create-packages/supporting-multiple-target-frameworks
