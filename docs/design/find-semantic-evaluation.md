# Find semantic evaluation

## Status and owner

Proposed focused design for
[#9190](https://github.com/richlander/dotnet-inspect/issues/9190).

**Find semantic evaluation** owns one host-neutral operation:

> Given one normalized Type or Member Find question and one ordered,
> already-authorized population of immutable source-local facts, evaluate each
> pattern once and return one semantic Find block containing ordered matches,
> exact source and declaration associations, pattern settlement, and attributed
> source coverage.

The operation has two explicit stages:

1. evaluate one exact source under the question, retaining strong matches,
   complete-population Type candidates, exact declaration associations, and
   source coverage; and
2. reduce one ordered population of those source evaluations into one semantic
   block.

The split lets a scheduler evaluate one source once and reuse its immutable
question-specific facts in several Ecosystem populations. Population reduction
still sees the complete ordered candidate set needed for Type tier settlement.

This owner defines normalized Find questions, source-evaluation facts, semantic
Type and Member matches, population reduction, ordering, limits, and completion.
It does not authorize or acquire sources, define Ecosystem membership, schedule
several populations, select final presentation rows, render output, or activate
a selected declaration.

## Demo

The first CLI Type scenario is:

```console
dotnet-inspect find 'IList<T>' --platform runtime
```

The CLI lowers the parsed pattern and visibility into a `TypeFindQuestion`.
The Runtime source owner supplies an exact platform population and detached
declaration facts. The shared evaluator returns a Type block whose match retains
the Runtime source coordinate and
`System.Collections.Generic.IList<T>` declaration identity. The CLI projects
that block into its current output without re-running matching.

The first Member scenario is:

```console
dotnet-inspect find '.Add*' --ecosystem aspire --jsonl
```

The host removes the leading-dot sentinel before constructing a
`MemberFindQuestion`. Each exact source is evaluated once. The Ecosystem
scheduler reduces those source evaluations into Aspire, ASP.NET Core,
Microsoft.Extensions, and Runtime blocks without parsing a rendered signature
or rebuilding a member identity.

`System.Text.Json@10.0.0` plus the .NET Runtime is the overlapping-source case.
Both may produce `System.Text.Json.JsonSerializer`, but their exact source
coordinates remain distinct. Equal full names do not collapse package and
platform observations.

## Basis and owner map

<!-- markdownlint-disable MD013 -->

| Owner | Contract consumed |
| --- | --- |
| [Find type-search service](find-search-service.md) | Existing Type Exact, Direct, Glob, Namespace, Prefix, Substring, Partial, and no-match meaning; CLI source and presentation concerns remain there until migrated |
| [Find member-name search service](find-member-search-service.md) | Existing Member Direct/Glob grammar, pattern multiplicity, declaring-Type filtering, and discovery-not-selection meaning |
| [Type Find population selection](type-find-population-selection.md) | First-nonempty Prefix, Substring, or Partial selection over one complete ordered Type population while retaining exact associations |
| [Type, member, and API representation](type-member-api-representation.md) | `MetadataTypeDefinitionName`, `MemberAnchor`, and the rule that identity is not recovered from display strings |
| Reverse Type-Declaration Locator | Exact source-backed Type coordinates, declaration observations, visibility facts, and population coverage |
| `AssemblyContextTypeInventoryQuery` and `AssemblyContextMemberMatchesQuery` | Existing host-neutral execution, participant order, rejection, failure, and metadata-inspection evidence; their current string projections are not sufficient declaration identity |
| [Inspection layers](inspection-layers.md) | `DotnetInspector.Queries` as the host-neutral L1 owner consumed by both CLI and Browser |
| [Host-observable content kinds](host-observable-content-kinds.md) | Serialization-ready Result and Document boundaries, complete-empty meaning, and partial completion |
| [Semantic row selection](semantic-row-selection.md) | Final Head, Tail, Window, Count, and projection semantics outside this evaluator |
| [Untrusted-data threat model](untrusted-data-threat-model.md) | Construction-time containment for user and package-origin text retained in questions and results |
| CLI and Inspect Web focused owners | Syntax, source authorization, operation authority, transport, presentation, selection, and activation |

<!-- markdownlint-enable MD013 -->

The conventional basis is a typed query request over owner-issued immutable
facts, followed by a detached result. The deliberate addition is a
question-specific source-evaluation value before the completed block. A single
block-only API would force a shared source to be evaluated again for every
Ecosystem population. A raw inventory-only cache would make each scheduler
reimplement Find classification. The intermediate value preserves the one
semantic owner while permitting source work to be shared.

The current CLI services are implementation oracles, not host-neutral
contracts. They consume `FindOptions`, host `HttpClient`, CLI diagnostics, and
CLI result rows. This design transfers their semantic classification claim to
the shared owner while leaving source authorization and presentation in the
CLI.

The existing assembly-context query results are execution precedents rather
than sufficient row inputs. `AssemblyTypeInventoryEntry` exposes decoded name
strings, and `MemberSearchResult` omits `MemberAnchor`. Adoption must add or
extend owner-issued query facts so the evaluator receives structured names,
anchors, and exact source associations directly. It must not parse
`FullName`, `Signature`, `Library`, or `Source` to manufacture them.

## Normalized questions

`FindQuestion` is a closed Type-or-Member union:

```text
FindQuestion
  TypeFindQuestion
    ordered TypeFindPatterns
    visibility
    optional maximum matches
  MemberFindQuestion
    ordered MemberFindPatterns
    visibility
    optional declaring-Type filter
    optional maximum matches
```

Questions are immutable, resource-free, and serializable. Construction rejects
an empty pattern array, an empty pattern, an invalid declaring-Type filter, and
a nonpositive match limit before source work starts.

The question carries semantic intent, not host syntax:

- comma splitting, repeated arguments, and `--members` are CLI concerns;
- a leading dot is Member-lens shorthand and is removed by the host;
- Spotlight kind chips are Browser concerns;
- `--where ecosystem=`, selected Ecosystems, and Workspace registration do not
  enter a Find question; and
- output format and semantic row selection do not enter a Find question.

Each normalized pattern retains its input ordinal and the original inert
spelling used for visible correspondence. Pattern construction classifies only
grammar that changes evaluation:

- a Type pattern is ordinary direct text, a Type glob, or an exact-or-descendant
  namespace request;
- a Member pattern is Direct or Glob; and
- the `this[]` alias remains a Direct Member pattern.

Exact, Prefix, Substring, and Partial are result classifications, not alternate
host-authored pattern modes. Exact single-Type selection remains a separate
operation and does not broaden this plural discovery question.

Visibility is the established Find public/default or include-all choice. It is
not Workspace admission and does not authorize a source. The source owner must
already have produced facts that can satisfy the requested visibility without
guessing.

`MaximumMatches` is an evaluator-work and result bound over matching semantic
rows. It is not CLI `-n`, a package-prefix candidate bound, or permission to
discard source failures. A host may lower a proven semantic Head plan into this
bound. Other row-selection operations run after the block settles.

## Source evaluation

`FindSourceEvaluation` is one immutable, question-specific map outcome:

```text
FindSourceEvaluation
  Available
    source facts and matches
    metadata inspection failures
    evaluation coverage
  Rejected
    exact source identity
    owner-issued rejection
  Failed
    exact source identity
    detached owner-issued failure
```

Every variant retains:

- the exact normalized question value and pattern ordinals;
- the source owner's exact source coordinate and source order;
- selected Library and assembly identity when the source owner established
  them; and
- participant realization and evaluation coverage.

`Available` additionally retains Type strong matches and complete-population
candidates, or Member matches, plus exact declaration associations and
metadata inspection failures.

The source owner supplies identity and coverage. The evaluator never derives a
package, platform family, Library, assembly, or acquisition origin from display
text. Two source requests that differ in authority, selected version, target
framework, runtime identifier, platform generation, or selected asset remain
different sources even when their labels match.

A Type source evaluation retains enough facts for later population reduction:

- Exact, Direct, Glob, and Namespace matches that are true independently of
  neighboring sources;
- ordered broadenable candidates carrying
  `MetadataTypeDefinitionName`, declaration kind, source coordinate, and exact
  declaration observation; and
- whether the candidate population and visibility evidence are complete.

It does not choose Prefix, Substring, or Partial for one source in isolation.
Those tiers are relative to the complete population being reduced. It does not
emit a source-local NotFound row.

A Member source evaluation retains Direct or Glob matches with the exact source
coordinate, declaring `MetadataTypeDefinitionName`, and producer-issued
`MemberAnchor`. Display signature, return Type, and kind remain semantic facts
beside that identity; none substitutes for it. An optional declaring-Type
filter is applied to the structured declaring name before a row is admitted.

Member matching is source-local and monotonic: adding another source does not
reclassify an existing Member match. A member that matches two input patterns
has two pattern associations, preserving the established pattern-multiplicity
contract.

Evaluation is side-effect free after the source facts are supplied. Its public
contract accepts no network, filesystem, package, platform, Workspace, or cache
service and retains no reader, stream, lease, service, or executable callback.
The source-evaluation value is an in-process intermediate, not completed
host-observable content, but it remains detached and resource-free.

## Population reduction

One `FindSemanticPopulation` carries the exact question, the population
owner's coverage statement, and an ordered array of source evaluations.
Construction rejects mixed questions, repeated source identities, or a source
outcome that is not named by the population coverage. A declared complete-empty
population is valid and reduces to a complete empty block. Missing, rejected,
failed, or visibility-insufficient sources instead produce partial coverage;
they do not make the population structurally invalid.

The reducer returns exactly one matching block kind:

```text
FindSemanticBlock
  TypeFindBlock
    question
    ordered TypeFindMatches
    pattern settlements
    source coverage
    match completion
  MemberFindBlock
    question
    ordered MemberFindMatches
    pattern settlements
    source coverage
    match completion
```

Blocks are detached, serialization-ready content. They contain no source
evaluation object identity, live resource, callback, or host action. Their
typed source and declaration coordinates are sufficient to preserve semantic
identity across transport; a host separately binds an authorized activation
operation.

For each Type pattern in input order:

1. Exact, Direct, Glob, or Namespace matches settle under their existing
   grammar and order.
2. An explicit glob or namespace request does not enter broadened selection.
3. When an ordinary pattern has no strong matches and the population is
   complete, `TypeFindPopulationSelector` chooses the first nonempty Prefix,
   Substring, or Partial tier over the complete ordered candidate population.
4. When no tier matches and population coverage is complete, the pattern
   settles as `NoMatch`.
5. When required population coverage is partial or failed, absence remains
   inconclusive; the block retains the source evidence and does not publish a
   false `NoMatch`.

Member reduction concatenates source-local matches in pattern, source, assembly,
declaration, and member order. A complete zero-match population settles the
pattern as `NoMatch`; partial or failed coverage remains inconclusive.

Pattern settlement is typed evidence beside the match array:

```text
FindPatternSettlement
  Matched
  NoMatch
  Inconclusive
  NotEvaluated
```

`NoMatch` is not a synthetic Type or Member row. A CLI compatibility projection
may render its established NotFound shape, while Browser and structured
consumers can distinguish a complete miss from an absent or unsearched
pattern.

The optional maximum counts matches across patterns in input order. Once it is
reached, later patterns settle as `NotEvaluated`, match completion is
`MatchLimitReached`, and source failures already observed remain visible. The
limit is applied only after a tier has been selected and ordered. It cannot
truncate a candidate census needed to establish Prefix, Substring, Partial, or
NoMatch.

## Identity and order

Every semantic match retains three separate currencies:

1. source identity from the source owner;
2. declaration identity from Metadata; and
3. pattern association from this question.

A Type match uses `MetadataTypeDefinitionName` plus its exact source-backed
declaration coordinate and observation. A Member match uses the declaring
`MetadataTypeDefinitionName`, `MemberAnchor`, and exact source coordinate.
Acquisition origin and rendered source labels remain separate facts.

No cross-source deduplication occurs in this operation. A package and platform
observation with the same Type or Member identity are distinct matches. A
scheduler may evaluate one repeated exact source only once before population
construction, but it cannot merge two different source identities because
their display names happen to agree.

Rows order by:

1. pattern ordinal;
2. the owning grammar or selected Type tier;
3. source order;
4. assembly and declaration inventory order; and
5. producer member order where applicable.

Prefix, Substring, and Partial retain the order defined by
`TypeFindPopulationSelector`. Arrival time, task completion, display label, and
Ecosystem name are never ranking inputs.

## Completion and failure

One valid block may be complete empty, contain matches plus attributed source
failures, or be limited. It carries two independent completion dimensions:

- source coverage states whether every admitted source and required declaration
  fact was evaluated; and
- match completion is `Exhausted` or `MatchLimitReached`.

Source rejection, image failure, unsupported declaration evidence, and
metadata-inspection failure remain attributed to their exact source or
declaration. They are not converted to an empty source evaluation or a
`NoMatch` settlement.

Cancellation before reduction completes produces no block. A scheduler may
retain already settled source evaluations as immutable cache facts under the
source owner's freshness rules, but a canceled question has no completed
Document.

An unexpected evaluator defect propagates as operation failure. This design
does not add a success-shaped catch or a generic diagnostic-only fallback.

## Host adoption

The production path is a stack of focused adoptions:

1. Add the normalized question and semantic block vocabulary plus Type
   evaluation in `DotnetInspector.Queries`; migrate one CLI Type path while
   preserving output and exact locator associations.
2. Add Member evaluation with `MemberAnchor` retention and migrate the CLI
   Member path.
3. Remove superseded CLI classification after all CLI source routes lower into
   the shared operation.
4. Implement
   [Ecosystem Find Search](ecosystem-find-search.md) over shared source
   evaluations and semantic blocks.
5. Let Inspect Web execute the same question inside .NET and transport only the
   portable completed content and explicit activation association.

The unified progressive row stream in
[#9128](https://github.com/richlander/dotnet-inspect/issues/9128) is a
presentation consumer. `FindDiscoveryRow` remains a one-way projection; it is
not an input to this evaluator and its display coordinates are not upgraded to
declaration identity.

The first shared C# call site is:

```csharp
TypeFindQuestion question = TypeFindQuestion.Create(
    ["IList<T>"],
    FindVisibility.Public);
FindSourceEvaluation source = FindSourceEvaluator.Evaluate(
    question,
    runtimeFacts);
TypeFindBlock block = FindSemanticReducer.ReduceType(
    question,
    [source]);
```

The first Browser call remains behind a .NET operation:

```typescript
const session = await spotlightFind.start({
  text: "IList<T>",
  kind: "types",
  maximumMatches: 20
});
installFindBlocks(session.blocks);
```

The Browser adapter receives transported blocks and activation associations.
Constructing a `FindQuestion`, classifying matches, joining declaration
identity, or filtering a realized Ecosystem population in TypeScript is outside
this contract.

## Evidence and acceptance

The implementation must add Release gates for:

- rejection of empty or invalid Type and Member questions;
- Type Exact, Direct, Glob, Namespace, Prefix, Substring, Partial, and complete
  NoMatch settlement over real Platform declarations;
- explicit-generic `IList<T>` discovery from the Runtime with its exact source
  and declaration association;
- first-association retention for equal Type names within one population;
- Member Direct, Glob, `this[]`, declaring-Type filtering, overload
  multiplicity, and `MemberAnchor` retention;
- distinct package and Runtime `JsonSerializer` observations;
- partial and failed source coverage withholding `NoMatch`;
- match limits that do not truncate a population-relative Type decision;
- one immutable source evaluation participating in two population reductions
  while retaining equal source-associated rows; and
- CLI parity for migrated Type and Member paths.

Existing `TypeFindPopulationSelectionTests`, `MemberSearchTests`, locator tests,
and CLI Find tests remain supporting gates. The new shared evaluator tests own
the composition assertions above; CLI tests alone cannot prove Browser-reusable
semantics.

The Browser adoption later adds published Firefox/Wasm acceptance. This design
inherits the repository's SRM-only, NativeAOT, prohibition on
inspected-assembly loading, and cross-platform contracts and introduces no new
platform dependency or exception.

Implementation PRs that change CLI execution must publish exact NativeAOT
base/head evidence for every affected Find terminal. This design-only PR is
Markdown-only and uses `markdownlint`.

## Non-claims

This design does not:

- define source acquisition, Workspace realization, or cache freshness;
- define Ecosystem lineage, phase order, membership, or package-prefix paging;
- define package search or package-query predicates;
- define exact single-Type or exact Member selection;
- define final Head, Tail, Window, Count, projection, or output formatting;
- define Spotlight chips, grouping, stable selection, or activation;
- make acquisition origin part of producer identity;
- establish equality from Type/member display text; or
- require a global Type-and-Member display row vocabulary.
