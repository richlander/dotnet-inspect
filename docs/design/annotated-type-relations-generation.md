# Annotated Type Relations generation

## Status and owner

This document owns concrete typed contribution settlement and canonical
document generation for
[Annotated Type Relations](annotated-type-relations.md). It owns:

- the feature-specific contribution type returned by adapter queries;
- exact correspondence among a resolved plan, adapter query, contribution,
  selected descriptor, focus, and terminal;
- independent settlement of relationship and color-family outcomes;
- canonicalization of Rows into the document's subject, relation, occurrence,
  annotation, family-outcome, and focus-join planes; and
- Count and Exists settlement without requiring Rows contributions.

Its exact claim is:

> Given one accepted `AnnotatedTypeRelationsPlan` and the already-executed
> results of its exact typed adapter queries, generation verifies exact
> contribution correspondence, preserves every selected relationship and
> color-family outcome independently, and emits the canonical terminal result
> without executing, reacquiring, or replanning producer work.

This is slice 3 of the six-slice plan in
[#10283](https://github.com/richlander/dotnet-inspect/issues/10283), tracked by
focused issue
[#10390](https://github.com/richlander/dotnet-inspect/issues/10390).

## Follow the T

`AnnotatedTypeRelationsContribution` is a concrete product type. The
Annotated Type Relations registration and generation layer may construct,
validate, and interpret it. General infrastructure may not.

`InspectionQueryCatalog` sees only exact `InspectionQueryDefinition` identity,
dependency closure, cost, execution order, and opaque typed result storage.
QuerySpace remains agnostic to this result type. Neither layer branches on,
joins, covers, filters, or optimizes Annotated Type Relations contributions.

Accordingly, every terminal adapter is an
`InspectionQuery<AnnotatedTypeRelationsContribution>`. The concrete generator
uses `InspectionQueryResults.Get` with that exact query. Erasing the adapter to
an arbitrary result type and recovering it through reflection, casts,
callbacks, string ids, or a universal result interface is invalid.

This is the "follow the T" boundary: the first layer that knows the domain
meaning of `T` owns it; every more-general layer only carries its typed
association.

## Inputs

Generation consumes:

- one accepted exact-Type `AnnotatedTypeRelationsPlan`;
- one completed `InspectionQueryResults` from executing exactly that plan's
  distinct adapter-query demand; and
- one `AnnotatedTypeRelationsContribution` for every demanded adapter query.

Generation does not accept producer-native context, acquisition services,
execution delegates, QuerySpace planners, or an inspection query catalog.
Missing results fail through typed query-result access. A contribution is
accepted only when:

- its focus is the plan's exact focus authority;
- its terminal equals the plan terminal;
- every relationship family is selected and mapped to that exact adapter query;
- every color family is selected and mapped to that exact adapter query;
- no selected family is repeated or omitted; and
- every relationship family records the plan's exact predicate-color set.

One contribution may contain several relationship and color families when
those descriptors deliberately share one adapter query. Sharing the result
does not merge their completion, failure, coverage, or diagnostics.

## Contributions

One relationship-family contribution retains:

- its exact descriptor and `SubjectRelationForm`;
- candidate-population authority;
- producer disposition, finite coverage, and diagnostics;
- the exact predicate-color descriptor set applied by the producer; and
- one terminal-specific value:
  - canonical `SubjectRelationRow` values for Rows;
  - an exact or lower-bound nonnegative cardinality for Count; or
  - `True`, `False`, or `Undecided` for Exists.

A complete Count is exact. A non-complete Count is a lower bound. A positive
Exists witness may settle despite incomplete remaining work. `False` requires
a complete relationship family. Unavailable or failed families publish no
successful scalar or Rows value.

One color-family contribution retains its exact descriptor, candidate
population, disposition, finite coverage, diagnostics, and, for Rows,
producer-owned annotations. Color-family completion remains independent of
relationship completion.

Contributions contain already-settled domain results. They do not expose
execution callbacks, source choices, provision plans, decoded bodies, or
retained producer intermediates.

## Canonical Rows generation

Rows generation admits the producer-issued canonical rows already selected
under the recorded predicate-color set. The generator does not reinterpret
color targeting or rerun the predicate.

Generation assigns deterministic document-local ids in plan and producer row
order:

1. canonical relations;
2. first-seen semantic and occurrence subjects;
3. descriptor-owned occurrences;
4. selected color annotations; and
5. focus joins.

Canonical relation identity is the exact relationship descriptor plus exact
semantic source and target. An occurrence retains its exact adapter producer,
producer-native occurrence id, descriptor-projected identity, and any
producer-native source occurrence ids cited by a derived occurrence.
Occurrence identity is the exact relationship descriptor plus its
descriptor-projected occurrence identity. Repetition is a contract failure,
never silent deduplication. Derived relationships require source occurrence
receipts; observed relationships reject them.
Every Row correspondence uses the plan's exact focus authority and its
relationship family's exact population authority.

An annotation targets an exact subject, canonical relation identity, or
occurrence identity. Its characteristic descriptor must admit the target kind,
value contract, and derivation. Relation and occurrence targets must resolve
to retained Rows; subject targets enter the subject plane when first seen.
Annotation identity is its exact descriptor plus exact target. Repetition
fails.

Rows completion is true only when every selected relationship family and every
predicate color family is complete. Decoration-only color incompleteness
remains visible in its family outcome but does not make the relationship Rows
population incomplete.

## Scalar settlement

Count sums relationship-family cardinalities because relationship descriptor
identity partitions canonical relations. The result is exact only when every
relationship-family Count is exact and every predicate color family is
complete; otherwise it is an explicit lower bound. Count never requests,
constructs, or derives from Rows in this generation path.

Exists is:

- `True` when any relationship family supplies a positive witness;
- `False` only when every relationship family supplies `False` and every
  predicate color family is complete; and
- `Undecided` otherwise.

Exists does not require unrelated decoration-only color completion. A true
witness is preserved when another family is incomplete or failed. False never
uses absence from an incomplete relationship or predicate-color population.

Scalar documents retain selection and family outcomes but have no fabricated
subject, relation, occurrence, annotation, or focus-join rows.

## Output

`AnnotatedTypeRelationsDocument` retains:

- exact focus and resolved selection;
- terminal outcome;
- document-local subjects;
- canonical logical relations;
- producer-native occurrences;
- producer-owned annotations;
- every selected relationship and color-family outcome; and
- focus joins.

The document is session-bound while any retained owner-issued subject,
population, evidence, diagnostic, or query identity is process-local. Portable
projection remains a later owner.

## Failure contract

Generation throws a contract exception for missing query results, foreign
focus or terminal, missing or extra families, wrong adapter-query ownership,
repeated family or canonical identities, mismatched row descriptors/forms,
foreign focus correspondence, invalid annotation targets or values, or a
success-shaped terminal inconsistent with family disposition.

Producer unavailability, partial completion, limits, and failures are data in
family outcomes. They do not throw merely because they are non-successful.

## Gates

- **Follow the T:** adapter registrations accept only
  `InspectionQuery<AnnotatedTypeRelationsContribution>`; general query
  infrastructure is unchanged.
- **No execution:** generation accepts only a plan and completed results and
  has no catalog, context, acquisition, or execution input.
- **Exact correspondence:** focus, terminal, query, descriptor, form, and
  predicate-color sets match the accepted plan by exact identity.
- **Shared adapter:** one contribution can settle several descriptors mapped
  to one query while every family outcome remains separate.
- **Family isolation:** one unavailable, partial, or failed family neither
  erases another family's Rows nor strengthens its completion.
- **Canonical identity:** repeated relation, occurrence, annotation, or family
  identity fails visibly.
- **Rows/Count equivalence:** equivalent complete Rows and Count contributions
  report the same cardinality.
- **Exists settlement:** a witness settles true; false requires complete
  relationship and predicate-color families; every other absence is
  undecided.
- **Scalar work:** Count and Exists contributions contain no Rows and scalar
  documents fabricate none.

## Real assets and production path

The first production adopter remains outgoing calls to trimming and
NativeAOT-annotated APIs in `System.Text.Json` 10.0.13 and `Newtonsoft.Json`
13.0.4. That adopter supplies the first producer-native contributions and
NativeAOT work evidence in slice 4.

## Non-claims

- This owner does not execute queries or define QuerySpace, inspection query,
  producer-capability, source, acquisition, caching, or lifetime semantics.
- It does not define relationship, color, predicate, transfer, absorption, or
  evidence semantics.
- It does not select physical provisions or claim that sharing one adapter
  query is cheaper.
- It does not define `AllApplicable`, portable intent or projection, CLI or
  Browser gestures, table or graph projection, or rendering.
- It does not introduce a universal contribution, outcome, annotation, or
  document type outside Annotated Type Relations.
