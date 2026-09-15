# Type-declaration visibility selection

## Owner and claim

L2 type-declaration visibility selection owns this contract: a consumer can
select detached locator choices by independent `PublicSurface`,
`EditorBrowsableNever`, and `Obsolete` facets, replacing the corresponding
preset restrictions before pruning, while retaining undecidable choices as
attributed evidence rather than complete negative answers.

The motivating assets are `Microsoft.NETCore.App.Ref@10.0.10`:
`System.Runtime.dll` defines ordinary `System.Object`, EditorBrowsable Never
`IsExternalInit`, obsolete `ExecutionEngineException`, and non-deprecated
`Span<T>` with compiler-compatibility attributes; `netstandard.dll` advertises
forwarders whose target attributes are unavailable.

Supporting owners retain their contracts:

- [Metadata](type-forwarding-resolution.md#definition-discovery-attributes)
  owns public-surface and definition-local discovery facts. An exported
  declaration's public surface describes its declaration, not its target.
- [The locator](reverse-type-declaration-locator.md) owns coordinate and
  observation vectors and population coverage. It does not choose visibility.
- [Row queries](row-query-order.md) supply predicate intent and operator
  vocabulary. Their Boolean executor deliberately treats missing values as
  nonmatches; this focused selector instead needs three-valued outcomes and
  attributed unknown evidence. It does not change generic row-query semantics.
- [Output shapes](output-shapes.md#reverse-type-declaration-locator-projection)
  owns the existing Sections, JSON, Markout, and row-window lowering.

## Presets and constraints

| Facet | Values | Default restriction | All restriction |
| --- | --- | --- | --- |
| `PublicSurface` | `true`, `false` | `=true` | None |
| `EditorBrowsableNever` | `true`, `false`, `unknown` | `=false` | None |
| `Obsolete` | `true`, `false`, `unknown` | `=false` | None |

Every facet accepts `=` and `!=`. Shared binding consumes existing
`RowQueryPredicateIntent` values, accepts case-insensitive facet/value names,
and returns the first typed one-based field/operator/value failure atomically.
It introduces no expression grammar. Typed callers can construct the same
immutable plan directly; shared descriptors expose only implemented facets.

An explicit predicate replaces **all preset restrictions for that facet**.
Other facets keep their preset restrictions. Repeated explicit predicates
remain conjunctive; their order does not change selection. Contradictory
predicates are not algebraically simplified.

Thus Default plus `Obsolete=true` admits deprecated public declarations that
are not EditorBrowsable Never. All plus `Obsolete=true` removes those other
restrictions. Default plus `PublicSurface=false` can admit nonpublic
declarations; it must not filter an already-pruned public vector.

Compiler-generated **names** remain a separate search-scope restriction in
both presets, using Metadata's reserved-name grammar over declaration name
segments. This is not a compiler-generated attribute fact or a fourth facet.
Raw locator projection remains available without any visibility plan. Full
accessibility, complete EditorBrowsable states, and an explicit
compiler-generated scope/facet are deferred.

## Unknown evidence and completeness

Known facts compare normally. For an unavailable fact, comparison with `true`
or `false` is unknown, including `!=`; absence is never false. `=unknown`
matches unavailable evidence and `!=unknown` matches available evidence.
Conjunction uses three-valued logic: any known false excludes the candidate;
otherwise any unknown makes it undecidable; otherwise it matches.

For example, All plus `Obsolete=unknown` selects forwarder observations as
ordinary matched rows. Default plus the same predicate still leaves its
EditorBrowsable restriction, so a forwarder is undecidable. Consumers can
choose All or override both restrictions when inspecting unknown evidence.

Each evaluated answer partitions its input into known matches, known
exclusions, and undecidable candidates. Undecidable candidates retain their
full coordinate, declaration facts, observation, and the unique facets with
unknown predicate outcomes. They are separate from matches and survive row
windows and strict-window failure. Matched vectors retain input ordering and
every surviving observation, including equal coordinates or identical bytes.

L2 answer completeness conjoins unchanged upstream realization/evaluation
completeness with visibility completeness. A known match may coexist with an
incomplete answer. A zero-match vector with undecidable candidates is not a
complete negative answer. Row-selection completeness remains separate.

## Admission and projection

Visibility selection requires a locator result evaluated with
`includeAll: true`. Public-only input produces the typed
`AllDeclarationsRequired` selection failure: visibility and row windows do
not execute, candidate arrays are empty, input counts and source coverage
remain, and visibility completion is false. Upstream query rejection remains
upstream rejection.

Selection occurs before existing row windows. `AvailableCandidateCount`
counts known visibility matches before windows; visibility coverage retains
the original input count and excluded/undecidable evidence. JSON carries the
effective plan and coverage. Markout's existing Gaps surface reports every
undecidable observation and any admission failure; output limiting does not
hide this evidence. Existing projection without a visibility plan retains its
previous row-selection behavior.

Plans consume detached results, including after Workspace close. Changing a
plan reuses those facts. Consumers requesting additional names or newly
appended participants use the existing resident locator and its population
receipt contract, not a separate visibility cache.

## Adoption and gates

[The overall adoption map](reverse-type-locator-adoption.md) remains the
production path. Issue #7142 is a focused step-6 prerequisite after #7104:
carry public-surface facts through L1, implement shared L2 selection, and
project it through Sections/JSON/Markout. The user explicitly chose
**shared-first**; CLI #6844/#7082 and Browser #6851 consume this shared plan
in their own adoption slices. CLI `--where` bindings and `-Q` disclosure are
not shipped or advertised by this prerequisite. Compatibility lookup
retirement remains #6850.

Release gates in the `WorkspaceContextLoaderTests.Visibility*.cs` partials cover
presets/overrides, the comparison truth table, conjunction and unavailable
facts, input admission, row windows, occurrence preservation, resident reuse,
JSON, and Markout. The Slow
`TypeLocator_VisibilityPinnedReferencePack_PreservesDefinitionAndForwarderEvidence`
gate admits the real reference populations and applies four plans after close.
No Workspace lifecycle contract or model changes are required by detached
selection.

## Shared-consumer demo

One resident result for Object, IsExternalInit, ExecutionEngineException, and
Span from the two reference assemblies has seven coordinate/observation choices
from two inventory reads. After Workspace close, the same detached result
produces:

| Plan | Known matches | Known exclusions | Undecidable | Complete |
| --- | --- | --- | --- | --- |
| Default | 2 | 2 | 3 | No |
| All | 7 | 0 | 0 | Yes |
| Default + `Obsolete=true` | 1 | 3 | 3 | No |
| All + `Obsolete=unknown` | 3 | 4 | 0 | Yes |

For a C# consumer, the shared call site is:

```csharp
var raw = await locator.ExecuteAsync(requests, includeAll: true);
var visibility = new TypeDeclarationVisibilityPlan(
    TypeDeclarationVisibilityPreset.Default,
    [new(TypeDeclarationVisibilityFacet.Obsolete,
        RowQueryOperator.Equals, TypeDeclarationVisibilityValue.True)]);
var selected = TypeDeclarationLocatorSection.Project(raw,
    new(RowSelectionIntent<string>.Empty, visibility));
string json = TypeDeclarationLocatorSectionJson.Serialize(selected);
```

A TypeScript consumer reads that typed result rather than applying its own
visibility policy. For the deprecated plan, candidate-vector sizes remain
`[0, 0, 1, 0]`; undecidable forwarders remain in each answer's
`visibility.unknown_candidates`, with the complete `candidate` and `facets`.
For All plus unknown, the ordinary selected vectors become `[1, 0, 1, 1]`
and visibility evaluation is complete. The C# and TypeScript demo uses these
exact shapes; this is shared library evidence, not shipped CLI/Browser binding.

The corresponding TypeScript transport consumer can inspect both vectors:

```typescript
type Choice = {
    declaration_kind: "Definition" | "Forwarder";
    is_public_surface: boolean;
};
type Answer = {
    candidates: Choice[];
    visibility: {
        unknown_candidates: { candidate: Choice; facets: string[] }[];
    };
};
const result: { answers: Answer[] } = JSON.parse(json);
const sizes = result.answers.map(answer => answer.candidates.length);
const gaps = result.answers.flatMap(answer =>
    answer.visibility.unknown_candidates);
```

The abbreviated `Choice` view leaves the full coordinate and observation on
the transport object; an interactive consumer uses those owner-issued values
when presenting or choosing an occurrence.
