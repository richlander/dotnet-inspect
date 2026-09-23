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
| Which declarations should an API comparison match? | The comparison's ordinary API population | Widen the API population where that comparison admits the option |
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
