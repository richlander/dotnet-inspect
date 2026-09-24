# Library metrics report

## Status and authority

Focused Research design for [#7987](https://github.com/richlander/dotnet-inspect/issues/7987).
Its Analysis coverage prerequisite is tracked by
[#7989](https://github.com/richlander/dotnet-inspect/issues/7989) and is
consumed as an issued receipt by this report.

The **Library Metrics** report is the single normative owner for a
descriptive structural population report over one exact compiled library.
Its claim is:

> Given one exact library's complete Analysis-issued implementation-profile
> population, preserve the population receipt, coverage qualifications, and
> deterministic descriptive distributions of selected compiled-IL structure.

The report is compiled implementation evidence. It is not authored-source C#
complexity, decompiled-source evidence, a quality score, a defect prediction,
or a comparison between releases or packages.

Metric labels name the command grain, not a source-language or binary
distinction: this report aggregates compiled IL evidence, while `Type Metrics`
and `Member Metrics` expose its selected body rows at narrower command scopes.

Analysis retains per-body metrics, method identities, body-scope execution,
and diagnostics. Research owns the report's population meaning, aggregation,
completeness interpretation, and portable document. Queries and hosts retain
selection, acquisition, execution, and presentation. This owner neither
redefines Analysis metrics nor chooses a threshold, weight, rank, or remedy.

## User question

> For this exact library release, what compiled method-body population did we
> measure, how complete is that evidence, and how is each selected structural
> measure distributed?

The report answers this question for one release only. A change report requires
cross-version correspondence; a corpus report requires a separately selected
multi-library population. Neither is inferred from one report.

## Imported evidence and population

The report consumes one
`LibraryImplementationProfileAnalysisResult` from the existing
`LibraryBodyAnalysisService` execution. Relationship composition accepts the
owning `LibraryBodyAnalysisExecution`, rather than independently supplied
focused results, and uses that execution's `LibraryCallGraphAnalysisResult`
only for the bounded relationship projection described below. Its
`LibraryBodyAnalysisReceipt` establishes the exact module identity, source
label, requested feature set, full-scope state, and Analysis diagnostics.
The report must retain that receipt rather than reconstructing library identity
from a path or a displayed name.

`MethodImplementationProfile` remains the sole source of every metric. Its
`EvidenceMethod` identifies the physical managed method body that supplied the
measure. Its `Method` is the logical declared-source association supplied by
Analysis. They are intentionally distinct: one logical method may associate
with several physical evidence bodies, such as a method and a compiler-created
companion body. The report must not merge those bodies, average them, or
silently choose one.

The report also consumes an Analysis-issued implementation-profile population
coverage receipt. The [Analysis coverage prerequisite](https://github.com/richlander/dotnet-inspect/issues/7989)
owns that receipt's construction, identity, scope, accounting, and failure
semantics. Research carries it unchanged and never infers its categories from
missing profiles or presentation rows.

One report requires `ImplementationProfiles` to have been requested and
the Analysis coverage receipt to establish a full method-evidence scope. A
scoped execution may be useful for another feature, but it does not establish
a whole-library population. Research returns a typed unavailable outcome for
either condition, preserving the Analysis-issued receipt and reason rather
than returning an empty report.

An unscoped execution that has no managed bodies is available and produces an
empty report with a zero physical-body denominator. Failure to decode or
measure an individual body does not turn that condition into a zero-count
success: the body remains in coverage and the report names the resulting
qualification.

## Report document

The Research result is a typed `LibraryStructuralReportResult`. Its
`Available` outcome carries one resource-free
`LibraryStructuralReportDocument`; its `Unavailable` outcome preserves the
Analysis receipt, coverage receipt, reason, and message. A later completed
host-neutral boundary carries the document through an
`InspectionEnvelope<LibraryStructuralReportDocument>`. The document contains:

- the exact Analysis receipt and a report methodology version;
- a `LibraryStructuralPopulationReceipt` that preserves the Analysis coverage
  receipt, exact counts of distinct physical evidence bodies and logical
  owners, complete and incomplete profile counts, and deterministic
  incomplete-reason counts;
- one distribution for every selected numeric measure, with its own complete
  physical-body denominator;
- deterministic per-declaring-type summaries over complete physical profiles;
- a bounded set of cross-type relationships selected by distinct neighboring
  type count, call-site volume, and qualified type identity; and
- bounded extreme-body evidence for each distribution; and
- the original Analysis diagnostics and profile incompleteness evidence.

An `Available` document is valid even when its numeric population is empty or
qualified. A typed unavailable outcome is reserved for missing requested
profiles or a non-whole-library scope. Acquisition and Analysis execution
failures remain owned by their existing query/host outcomes; they must not be
translated into an available empty document.

The document is ordered deterministically. A profile's physical evidence token
is its primary identity and final tie-breaker. Neither source display text nor
the profile's relative input order establishes identity or affects a
statistic.

### Completeness

Every numeric distribution includes only profiles whose `IsComplete` is true.
Its denominator is therefore the count of complete physical evidence bodies,
not declared methods, logical owners, all profiles, or an unreported subset.
The population receipt preserves Analysis coverage and diagnostics so that a
narrow denominator cannot masquerade as library-wide completeness.

A physical evidence identity may occur once in the profile collection. A
duplicate is an invalid owner input and cannot issue a document. The Report
implementation must fail visibly rather than coalescing identities or counting
an arbitrary copy. A profile collection whose physical evidence identities do
not match the Analysis-issued `ProfiledEvidenceBodies` receipt is the same
class of invalid owner input. Analysis owns validation of its separate
coverage receipt.

### Measures

The first report includes the compiled structural measures already used by the
local structural-change vector:

1. instruction count;
2. normal-flow cyclomatic complexity;
3. loop count;
4. aggregate exception-region count (`catch + filter + finally + fault`);
5. direct-call count; and
6. allocation count.

Async state-machine presence is a Boolean population disposition, not a
numeric distribution. It reports complete-body counts for present and absent
without treating `true` as a larger complexity value. Basic blocks, branches,
locals, throws, unsafe evidence, reflection calls, and overload relationships
remain available in the underlying profile but are out of this first report.
Adding another measure changes the report methodology and requires a focused
owner update.

Normal-flow cyclomatic complexity retains Analysis's definition:

```text
1 + conditional branches - switches + switch targets
```

It describes ordinary compiled IL control flow with one entry and a virtual
exit. Exception dispatch and cleanup do not enter that measure; exception
regions are separately reported.

Each numeric distribution contains minimum, nearest-rank p50, p90, p95, p99,
and maximum. For an ordered complete-body population of size `n`, percentile
`p` is the value at one-based position `ceil(p * n)`. An empty population has
no extrema or percentile values. The document never substitutes zero or
`null`-looking display text for an unavailable statistic.

Each distribution also retains up to five bodies attaining its maximum,
ordered by physical evidence identity, and an exact additional-tie count. A
row contains the metric value plus both the physical evidence and logical-owner
identities. These rows are evidence for a stated maximum, not an outlier,
severity, priority, or defect label.

### Type and relationship evidence

`TypeSummaries` groups complete physical profiles by the Analysis-issued
`Method.DeclaringType`. Each row retains the type identity, body count, and
summed instruction, normal-flow complexity, loop, direct-call, and allocation
counts. It is a population summary over physical evidence; it does not merge
compiler-generated bodies into one logical owner or infer authored-source
ownership.

When call-graph evidence is supplied, `EntangledRelationships` retains
cross-type direct-call evidence whose caller body is complete, whose callee
definition token resolves to an inspected declared method (including abstract
and extern declarations), and whose source and target types differ. Only
invocation kinds (`call`, `callvirt`, and `newobj`) are
admitted; loading a method address with `ldftn` or `ldvirtftn` is not a call
relationship. Relationships are aggregated by source type, target type, and
call-site count. The report selects at most
`MaximumEntangledTypeCount` types by distinct neighboring-type degree, then
call-site volume, then qualified metadata type identity (including generic
arity), and retains the relationships whose endpoints are both selected. The
result is a bounded relationship
projection, not a complete call graph and not a measure of bad design,
severity, or refactoring priority. Without call-graph evidence, the report
retains an empty relationship projection while preserving the available type
summaries. Whole-library dependency communities require a separate Research
contract over the complete admitted graph and are tracked by
[#8406](https://github.com/richlander/dotnet-inspect/issues/8406).

## Interpretation boundary

The report can say that a measure has a given value, that an exact number of
complete physical bodies contribute to a percentile, and that named bodies
attain the maximum. It cannot say that a high value is bad, that a percentile
is unusual across another library, that an extreme body is a performance
hotspot, or that an API needs refactoring.

Local implementation-diff percentiles and structural cohorts remain
request-local comparison context. They have different denominators and must
not appear in this document. Conversely, a library-report percentile must not
be projected back onto a member-to-member diff.

The report intentionally does not impose compiler-generated-method filtering.
Physical body inclusion is the evidence contract; the receipt exposes the
logical-owner/physical-body distinction that a consumer needs to interpret
compiler-created companions. A later user-selected authored-method lens would
need its own explicit Analysis/Metadata population owner.

## Composition and rendering

The report composes owner-issued implementation profiles, the optional
same-execution call graph, and the population coverage receipt from
[#7989](https://github.com/richlander/dotnet-inspect/issues/7989). Analysis
defines how these inputs are constructed and qualified; Research defines how
the report preserves them and derives report-local distributions, type
summaries, and the bounded relationship projection. No host rebuilds
coverage, completeness, or a statistic from display text.

The resource-free Research document is the structured rendering input. The CLI
adoption owns the exact-name-only `Library Metrics` section and its `Markout`
lowering. That section renders population receipt rows, distribution rows,
maximum evidence, async disposition, and diagnostic rows without changing their
Research-owned meaning. Markout's existing Markdown, table, TSV, JSONL, and
projected-JSON lowerings remain format mechanics; numeric measures and
coverage states stay typed until that boundary.

Browser/Wasm deliberately bypasses Markout for its interactive Library-detail
view. Its host-specific lowering serializes the same typed document through
the existing managed boundary. The Browser DTO carries a module-local exact
metadata type key separately from human display text, so same-name types with
different generic arities remain distinct through relationship layout. It
renders a `Complexity Explorer` treemap
from type summaries plus a `Relationship Crossing` view from the bounded
relationship projection, without recomputing any report fact. Area represents
instruction volume, treemap color represents average normal-flow complexity, and
relationship stroke width represents retained call-site count. These visuals
render every endpoint and edge in Research's bounded relationship projection;
the Browser performs no second topology selection. A treemap cell discloses its
type summary on pointer hover or keyboard focus and activates the exact
metadata type key to continue the settled Library-to-Type-to-Member journey.
The synthetic `Other types` aggregate discloses its combined summary but is not
a Type navigation target. These visuals are structural evidence; they do not
add a quality score, Compare surface, complete graph, or a second report model.

## Real-library probe

The motivating asset is
[Markout 0.35.2](https://www.nuget.org/packages/Markout/0.35.2), inspected
through the ordinary package-to-library path:

```text
dotnet run --project src/DotnetInspect.Cli -c Release -- \
  library --package Markout@0.35.2 -S "Member Metrics" --jsonl
```

The Release probe emitted 1,345 complete profile rows for 1,345 distinct
physical evidence bodies and 1,309 distinct logical method owners. Its
normal-flow complexity distribution was minimum 1, p50 1, p90 4, p95 8, p99
16, and maximum 33. The 36-body difference between physical evidence and
logical owners demonstrates why the report denominator must be physical and
why the logical-owner count belongs in the receipt rather than replacing it.

The highest observed complexity was 33 for
`Markout.MarkoutProjection.GlobMatch(string, string, System.StringComparison)`.
That observation motivates traceable maximum evidence; it is not a quality
claim about Markout or that method.

The durable contract fixture includes one logical async method with multiple
physical profiles, using the existing `ImplementationProfileSample.AnalyzeAsync`
scenario. Its gate proves that the report preserves both evidence bodies and
does not collapse them into one source-owned profile.

## Validation gates

The implementation belongs in the Release
`ILInspector.Research.Tests` executable. It must demonstrate:

- `LibraryStructuralReport_RejectsScopedProfilePopulation`: A profile result
  from scoped method evidence is unavailable and retains its receipt.
- `LibraryStructuralReport_PreservesIssuedBodyCoverage`: The Analysis-issued
  coverage receipt survives unchanged beside report-local complete, incomplete,
  physical, and logical-owner counts.
- `LibraryStructuralReport_ExcludesIncompleteProfilesFromStatistics`:
  Incomplete evidence remains visible in the receipt but contributes to no
  numeric denominator or percentile.
- `LibraryStructuralReport_PreservesMultipleEvidenceBodiesPerLogicalOwner`:
  One logical async source with multiple physical bodies retains both bodies
  and the correct denominators.
- `LibraryStructuralReport_ProjectsTypeAndEntangledRelationshipEvidence`:
  Complete physical profiles produce deterministic type summaries, while the
  same execution's resolved call evidence produces a bounded, ordered
  cross-type relationship projection.
- `LibraryStructuralReport_UsesDeterministicNearestRankAndMaximumTies`: The
  fixed metric fixture proves percentile positions, exact maxima, deterministic
  ordering, and the additional-tie count.
- `LibraryStructuralReport_RejectsDuplicateOrUnaccountedEvidenceIdentity`:
  Invalid owner input cannot issue a plausible report.

The probe command is reproducible design evidence, not a CI gate. Fixture
tests establish the deterministic contract; an eventual pinned package corpus
test exercises the normal acquisition path without making package availability
a PR-fast dependency.

## Adoption plan

This is a five-slice plan from existing Analysis evidence to both production
hosts:

1. Analysis publishes the profile-coverage receipt tracked by #7989.
2. Research publishes the document and typed unavailable outcome.
3. A Research-backed L1 query carries that completed document without rendering
   it. Implemented as `LibraryMetricsQuery`.
4. The CLI adopts an explicit `Library Metrics` section. It is exact-name-only
   and outside default `-v:m` output; the existing `Member Metrics` inventory
   remains the detail surface.
5. Browser/Wasm adopts the same document through its settled Library detail
   path. Its managed Analysis facade runs the host-neutral
   `AssemblyContextLibraryMetricsQuery` over the exact implementation
   participant, and its explicit `Metrics` lens presents the `Complexity
   Explorer` and `Relationship Crossing` views while preserving Type/Member
   drill-down rather than adding method rows to Compare.

The CLI path has four steps and the Browser/Wasm path has five steps; the first
three are shared. The completed CLI adoption establishes the `Library Metrics`
section spelling and Markout row-group renderer. Browser deliberately lowers
the same typed document into its interactive summary instead of introducing a
second Research model or using Markout for the Library-detail view.
