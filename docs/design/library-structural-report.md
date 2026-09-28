# Library metrics report

## Status and authority

Focused Research design for
[#7987](https://github.com/richlander/dotnet-inspect/issues/7987) and the
Type structural-leverage extension in
[#8819](https://github.com/richlander/dotnet-inspect/issues/8819).
Its Analysis coverage prerequisite is tracked by
[#7989](https://github.com/richlander/dotnet-inspect/issues/7989), and its
Type-relationship producers are tracked by the extension adoption plan. The
report consumes each input as owner-issued evidence.

The **Library Metrics** report is the single normative owner for a
descriptive structural population report over one exact compiled library.
Its claim is:

> Given one exact library's complete Analysis-issued implementation-profile
> population, preserve the population receipt, coverage qualifications, and
> deterministic descriptive distributions of selected compiled-IL structure.

The Type structural-leverage extension adds this claim:

> Given one exact library's owner-issued signature and body Type relationships,
> preserve their separate qualifications and use Graph-issued directed
> distinct-neighbor degree to identify sea-level foundations, mountain-peak
> orchestrators, and their combined structural role.

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

The Type structural-leverage extension answers two additional questions:

- Which Types are used by the most distinct peer Types in declaration
  signatures?
- Which Types use the most distinct peer Types in their implementation bodies?

It also labels connected Types as foundations, hubs, or orchestrators from the
incoming share of their combined signature-and-body peer relationships. The
rankings remain separate because the prototype evidence in #8732 found that
signature relationships answer the first question best while body
relationships answer the second best.

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

Type structural leverage additionally consumes two complete, separately
qualified relationship populations:

- Metadata owns signature relationships from base Types, implemented
  interfaces, generic constraints, and member signatures.
- Analysis owns body relationships from resolved Type-bearing IL operands and
  the logical declared-Type owner of each physical body.

Each producer issues exact Library identity, admitted source and target Type
definition identities, completeness, and visible failures. Research neither
rescans metadata nor decodes IL to reconstruct a missing population. A
relationship population from another Library generation cannot compose with
this report.

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
  type count, call-site volume, and qualified type identity;
- complete Type structural-leverage rows with separate sea-level and
  mountain-peak positions, Graph-issued peer counts, a combined structural
  role, and the relationship-population and Graph-work receipts that qualify
  them;
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
ownership. A selected relationship endpoint with no complete physical profile
is retained as a zero-body summary so every relationship endpoint resolves to
one typed node without inventing body evidence.

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

### Type structural leverage

The extension consumes two directed relationship kinds over exact Type
definition identities in the selected Library:

- **Signature use:** `A -> B` means an admitted declaration owned by Type `A`
  refers to Type `B` through its base Type, implemented interfaces, generic
  constraints, or a field, property, event, parameter, or return Type.
- **Body use:** `A -> B` means an admitted physical body logically owned by
  Type `A` contains a resolved Type-bearing IL operand whose definition is
  Type `B`.

Constructed generic Types lower to their owner-issued generic definition.
Analysis-issued logical ownership folds compiler-created nested implementation
Types into their declared Type owner; Research does not infer that owner from a
generated name. An otherwise unfurled nested Type remains its own exact
definition. Both endpoints must belong to the selected Library generation.
External Types are outside this Library-scoped population. Accessibility does
not alter topology, canonical-row retention, or either default order: public
API foundations and internal implementation orchestrators are both product
evidence.

A relationship from a Type to itself is not a peer relationship and contributes
to no degree. Parallel occurrences and a pair present under both relationship
kinds remain evidence, but each selected Graph degree counts the opposite Type
once. Research admits one immutable Graph document containing both typed
relationship kinds and requests three directed distinct-neighbor views:

1. signature incoming degree for the **sea-level** ranking;
2. body outgoing degree for the **mountain-peak** ranking; and
3. incoming and outgoing degree over the union of both kinds for structural
   role.

The role denominator is combined incoming plus combined outgoing degree. A
connected Type is a **foundation** when its incoming share is at least `0.7`,
an **orchestrator** when that share is at most `0.3`, and a **hub** otherwise.
An isolated Type has no role and enters neither ranking.

The report retains one canonical leverage row per connected Type. Each row
contains exact Type identity, signature incoming degree, body outgoing degree,
combined incoming and outgoing degree, and role. Research also publishes the
complete sea-level and mountain-peak orders. Each order sorts descending by
its named degree, then by exact metadata Type identity; a displayed name never
breaks a tie.

Ranking eligibility is separate from structural population. Universal base
Types and Types whose owner-issued metadata classification is enum, attribute,
exception, or delegate remain in the graph as peer evidence but do not enter
either ordered ranking. Removing them from topology would silently change the
degree and role of otherwise eligible Types. Helper-looking names such as
`SR` or `ThrowHelper` are not an identity or classification contract and do
not justify exclusion. A future owner-issued implementation-helper
characteristic may extend ranking eligibility without changing Graph degree.

Ranking eligibility is local to these two degree-ordered views. It is not a
low-value classification and cannot suppress the canonical leverage row or
transfer to another query. A low-degree or ranking-ineligible Type may still
be a call-graph bridge, an async blocking boundary, the only unsafe or native
operation carrier, a reflection or serialization activation point, or a
meaningful exception, attribute, delegate, or protocol state. Those questions
retain their own relationship populations and owners.

Signature and body completion remain independent. A complete signature
population cannot qualify body degree, and complete body evidence cannot
qualify signature degree. An available report may retain healthy rows when one
population is incomplete, but every affected ranking and role remains visibly
qualified by the producer receipt and failures. Missing or failed evidence
does not become a zero-degree success.

Graph owns relationship selection, direction, selected adjacency,
distinct-neighbor counting, self-loop treatment, deterministic structural
results, and its work receipts. Research owns which producer-issued
relationships enter each plan, ranking eligibility, the two orders, role
meaning, and qualification in the Library report. Neither host recomputes a
degree, rank, role, or exclusion.

## Interpretation boundary

The report can say that a measure has a given value, that an exact number of
complete physical bodies contribute to a percentile, and that named bodies
attain the maximum. It cannot say that a high value is bad, that a percentile
is unusual across another library, that an extreme body is a performance
hotspot, or that an API needs refactoring.

Likewise, the report can say that a Type has a given directed distinct-peer
degree, appears at a position in one of the two Library-local orders, or has a
role under the stated union threshold. It cannot call a Type important,
unimportant, safe, unsafe, blocking, or non-blocking from that evidence.
Degree is not reachability, centrality, execution frequency, implementation
risk, or runtime behavior. Ranking omission is specific to the named view and
does not hide the Type from other analyses.

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
same-execution call graph, signature-use relationships, body-use relationships,
and their population coverage receipts from
[#7989](https://github.com/richlander/dotnet-inspect/issues/7989). Analysis
and Metadata define how their respective inputs are constructed and qualified;
Graph defines how the admitted closed document is structurally evaluated;
Research defines how the report preserves that evidence and derives
report-local distributions, type summaries, leverage rows, rankings, roles,
and the bounded relationship projection. No host rebuilds coverage,
completeness, topology, or a statistic from display text.

This closed-document composition separates Graph optimization from QuerySpace
optimization. QuerySpace and its providers can reduce which domain facts must
be acquired and admitted; Graph answers the requested structural question over
the admitted population with only the necessary topology and traversal state.
The first leverage implementation still acquires complete producer-owned
relationship populations before Graph executes. Its typed Graph plans make a
later provider-backed demand path possible but do not themselves claim
acquisition pushdown. In short: QuerySpace gets Graph the smallest relevant
domain population; Graph answers the structural question with the smallest
necessary topology and state.

The resource-free Research document is the structured rendering input. The CLI
adoption owns the exact-name-only `Library Metrics` section and its `Markout`
lowering. That section renders population receipt rows, distribution rows,
maximum evidence, async disposition, sea-level rows, mountain-peak rows, and
diagnostic rows without changing their Research-owned meaning. The two
rankings remain separately named and ordered; the CLI does not blend them into
one score. Markout's existing Markdown, table, TSV, JSONL, and projected-JSON
lowerings remain format mechanics; numeric measures, role, ranking
disposition, and coverage states stay typed until that boundary.

Browser/Wasm deliberately bypasses Markout for its interactive Library-detail
view. Its host-specific lowering serializes the same typed document through
the existing managed boundary. The Browser DTO carries a module-local exact
metadata type key separately from human display text, so same-name types with
different generic arities remain distinct through relationship layout. It
renders a `Complexity Explorer` treemap
from type summaries plus a `Relationship Crossing` view from the bounded
relationship projection, without recomputing any report fact. Area represents
instruction volume, treemap color represents average normal-flow complexity, and
relationship stroke width represents retained call-site count. Complexity
Explorer omits zero-body relationship-only summaries because they carry no
implementation volume. Relationship Crossing renders every endpoint and edge
in Research's bounded relationship projection; the Browser performs no second
topology selection.

The existing Metrics lens adds a `Type Leverage` view rather than new
persistent workspace chrome. It presents separate sea-level and mountain-peak
lists in their Research-issued order. Each visible row discloses its named
degree and structural role and activates the exact metadata Type key. A
presentation limit may take a prefix of each issued order, but Browser does not
sort, merge, exclude, or recompute the rows. Relationship Crossing may decorate
an already selected exact Type with its report-issued role; it does not infer
role from the bounded call projection.

A treemap cell discloses its type summary on pointer hover or keyboard focus
and activates the exact metadata type key to continue the settled
Library-to-Type-to-Member journey. The synthetic `Other types` aggregate
discloses its combined summary but is not a Type navigation target. These
visuals are structural evidence; they do not add a quality score, Compare
surface, complete graph, or a second report model.

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

Issue #8732 records the SRM-only structural-leverage prototype over real
Library assets. On System.Text.Json 10.0.0, the signature population measured
in 1.1 ms and the body population in 3.5 ms (median of five warm CoreCLR runs).
Its leading signature-incoming Types included
`JsonSerializerOptions` (135 distinct peers), `Utf8JsonReader` (92), and
`Utf8JsonWriter` (84); its leading body-outgoing Types included
`JsonMetadataServices` (82), `DefaultJsonTypeInfoResolver` (41), and
`JsonSerializer` (37). Those values motivate the two different relationship
populations and are not yet product-output evidence.

The .NET 11 RC1 System.Private.CoreLib Library is the scale and noise-pathology
asset. Its 1,418 public Types produced the prototype signature population in
16.7 ms and body population in 30.2 ms. Enums, universal bases, and
implementation helpers demonstrated why topology membership, canonical row
retention, and default ranking eligibility must remain separate decisions.
Those timings are reproducible design evidence rather than a performance
contract; the owner-sized implementation slices record their own production
measurements.

The durable contract fixture includes one logical async method with multiple
physical profiles, using the existing `ImplementationProfileSample.AnalyzeAsync`
scenario. Its gate proves that the report preserves both evidence bodies and
does not collapse them into one source-owned profile.

## Validation gates

The original report implementation belongs in the Release
`ILInspector.Research.Tests` executable. It demonstrates:

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

The structural-leverage extension distributes gates with their owner and then
repeats the product contract through Research and each host:

- Graph fixtures prove that parallel edges count one peer, self-edges count no
  peer, selected incoming and outgoing adjacency remain separate, union degree
  does not double-count a peer present under both relationship kinds, exact
  identities with the same display name remain distinct, and work receipts
  account for the selected topology.
- Metadata fixtures prove signature relationship identity, generic-definition
  folding, Library scope, and visible completion. Analysis fixtures prove body
  Type-operand identity, logical declared-Type ownership of compiler-created
  bodies, Library scope, and independent visible completion.
- Research fixtures prove the two named orders, exact-identity tie-breaking,
  union-derived role thresholds, all-accessibility participation, canonical
  retention of ranking-ineligible Types, row-only exclusion from each default
  order, and visible qualification when either producer population is
  incomplete.
- CLI fixtures prove that the explicit `Library Metrics` section lowers both
  issued orders and their qualifications through Markout without introducing
  them into default `-v:m` output.
- Browser/Wasm fixtures prove that the existing Metrics lens consumes the same
  exact Type keys and issued order, limits only by taking a disclosed prefix,
  and never reconstructs degree, role, or ranking from the bounded call
  projection.
- System.Text.Json 10.0.0 proves the production package path and useful
  separation of foundations and orchestrators. System.Private.CoreLib proves
  bounded scale, deterministic results, and that ranking omission never
  removes a Type or relationship from canonical evidence.

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
   remains the detail surface. Exact singleton `--json` emits the complete
   Research document directly, while `--envelope` wraps identical Content with
   Share and diagnostics; Markout remains the Markdown/table/TSV/JSONL
   projection path.
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

Type structural leverage extends that established path through an owner-sized
stack. Each slice has one normative owner and lands a usable typed contract:

1. Graph publishes closed-document selected incoming/outgoing adjacency,
   directed distinct-neighbor degree, union selection, and work receipts.
2. Metadata publishes the complete qualified Library signature-use
   relationship population and exact Type-definition identities.
3. Analysis publishes the complete qualified Library body-use relationship
   population with logical declared-Type ownership.
4. Research composes both populations through Graph, publishes canonical
   leverage rows, independent orders, roles, eligibility, and qualification,
   and retires its superseded private undirected `HashSet` degree calculation
   wherever the new evidence serves the same question.
5. The CLI adds the two named `Library Metrics` row groups through Markout
   without changing the command's explicit-only disclosure.
6. Browser/Wasm extends the existing Metrics lens through the settled managed
   facade and shared Research document.
7. A later provider-backed composition may push Graph demand into Metadata and
   Analysis acquisition. It is a separately evidenced QuerySpace optimization,
   not a condition of the closed-document stack.

The stack composition map records the contracts joining adjacent slices; it
does not broaden this Research design into normative Graph, Metadata, Analysis,
CLI, or Browser internals.
