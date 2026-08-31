# Integrations

The Integration owner describes ecosystem support discovered from assembly
metadata. Its existing focused library sections answer:

```text
Which .NET ecosystem integration surfaces can a caller use from this library?
```

The target Workspace Census answers a second, open-ended question:

```text
Which configured Integration concepts and candidates are visible across this
finite workspace universe?
```

It is intentionally different from `Signals`. `Signals` is an evidence report.
Integration sections form a usability index: they point to APIs that are useful
currency for wiring the library into common .NET application systems.

Each focused integration section is named with an `Integration:` prefix (for
example `Integration: Logging`, `Integration: OpenTelemetry`) so alphabetical
section ordering clusters the whole family together.

Tracking: [#3629](https://github.com/richlander/dotnet-inspect/issues/3629).

## Authority

The Integration owner defines:

- the configured Integration concept descriptors;
- Integration evidence and candidate identity;
- candidate admission and universe disposition;
- the Integration Census result; and
- the producer-owned rows and lowerings supplied to Section, matrix, and graph
  consumers.

It consumes request plans, finite universe descriptions, acquired subject and
provenance identities, graph relationship descriptors, and output contracts
issued by adjacent owners. It does not construct a workspace, validate a
generic analysis request, define graph induction, resolve `find` scope, select
Sections, or render output.

## User model

Discover the family, then select a focused section:

```bash
dotnet-inspect package Microsoft.Extensions.AI --library -D @Integrations
dotnet-inspect package Microsoft.Extensions.AI --library -S "Integration: Dependency Injection"
dotnet-inspect package Microsoft.Extensions.AI --library -S "Integration: OpenTelemetry"
```

Select the whole category, or add `--count` to see per-integration API counts:

```bash
dotnet-inspect package Microsoft.Extensions.AI --library -S @Integrations
dotnet-inspect package Microsoft.Extensions.AI --library --count -S @Integrations
```

`Library Info` also includes an `Integrations` field. That field counts detected
integration categories, not example rows. It is computed from cheap metadata
presence flags during normal library inspection.

## Currency, not raw evidence

Focused integration sections should show things the user can act on: types and
APIs they can search, call, configure, or wire into an application. These are the
"currency" of an integration.

Examples:

| Integration | Currency examples |
| ----------- | ----------------- |
| AI | `IChatClient`, `IEmbeddingGenerator<TInput,TEmbedding>`, `AITool`, modality client interfaces, package-owned builder/registration/adapter APIs. |
| ASP.NET Core | `Use*` middleware APIs, `Map*` endpoint APIs, Data Protection builder APIs, ASP.NET Core option types. |
| Aspire | `AddRedis(...)`, `RedisResource`, resource-specific `Add*` APIs returning `IResourceBuilder<T>`. |
| Authentication | `AddAuthentication(...)`, `AddJwtBearer(...)`, `UseAuthentication(...)`, validation/auth-state/authorization builder APIs, auth/authorization option and builder types. |
| Configuration | `AddJsonFile(...)`, `AddSystemsManager(...)`, `Bind(...)`, `GetValue(...)`, `IConfigurationBuilder` source registration APIs, configuration provider/source/options types. |
| Dependency Injection | Package-owned `Add*` service registration APIs, `Scan(...)`/`Decorate(...)` APIs, and DI builder types. |
| Logging | `ILogger`, `ILogger<T>`, `LoggerMessageAttribute`, provider registration APIs such as `AddAWSProvider(...)`. |
| OpenTelemetry | `ActivitySource`, `Meter`, `DiagnosticSource`, OpenTelemetry provider/exporter types, Serilog OTLP sink APIs, `DisableTracing`/`DisableMetrics` telemetry controls. |
| OpenAPI | `AddOpenApi(...)`, `MapOpenApi(...)`, `AddSwaggerGen(...)`, `UseSwaggerUI(...)`, `EnableAnnotations(...)`, OpenAPI/Swagger option and annotation types. |
| Options | `IOptions<T>`, `IOptionsMonitor<T>`, configure/validate options APIs and types. |
| Hosting | `IHostedService`, `BackgroundService`, `Add*HostedService(...)`, host builder types. |
| Health Checks | `IHealthCheck`, `UseHealthChecks(...)`, health check builder/service types. |
| HTTP Client | `IHttpClientFactory`, `IHttpClientBuilder`, HTTP client builder extension types. |

Assembly references are not integration currency. They belong in references or
signals, not focused integration rows. A direct assembly reference only says the
library was compiled against another assembly; it does not tell the user what
API to use.

## Detail section shape

Focused sections render examples as types when every row is a type:

```markdown
| Type |
| ---- |
| `Microsoft.Extensions.DependencyInjection.IServiceCollection` |
| `Microsoft.Extensions.DependencyInjection.ChatClientBuilderServiceCollectionExtensions` |
```

When an integration has multiple kinds of currency, keep the `Kind` column:

```markdown
| Kind | Type |
| ---- | ---- |
| Tracing | `System.Diagnostics.ActivitySource` |
| Metrics | `System.Diagnostics.Metrics.Meter` |
```

When the useful currency includes member-level entry points or a mix of member
and type shapes, use `API` instead of `Type`:

```markdown
| Kind | API |
| ---- | --- |
| Hosting | `Microsoft.Extensions.Hosting.AspireOpenAIExtensions.AddOpenAIClient(...)` |
| Chat | `Microsoft.Extensions.Hosting.AspireOpenAIClientBuilderChatClientExtensions.AddChatClient(...)` |
| Configuration | `Aspire.OpenAI.OpenAISettings` |
```

If every row in a focused section has the same kind, hide `Kind` and render only
`Type` or `API`. This keeps the common case compact while preserving useful
distinctions for integrations such as OpenTelemetry and AI.

## Detection and ranking

Detection reads metadata only:

1. Public package-owned type definitions and curated public starter extension
   methods are scanned for integration currency.
2. External "famous" types from referenced assemblies are not enough to create a
   focused row; rows should identify what to use from the inspected package.
3. Public package-owned telemetry control APIs such as `DisableTracing` and
   `DisableMetrics` are OpenTelemetry currency because they reveal emitted
   telemetry kinds and how callers configure them.
4. The `@Integrations` category lists every focused section with at least one
   actionable type or starter API.
5. Focused sections sort rows by `Kind`, then by the displayed `Type` or `API`.

The model is deliberately curated. It should avoid claiming complete support
from weak signals, and it should prefer stable, low-noise examples over exhaustive
metadata inventory.

### Scanner implementation boundaries

`EcosystemIntegrationScanner` is the public Metadata facade.
`EcosystemIntegrationProjection` owns the SRM traversal, guarded signature
decode, evidence buckets, and row ordering.
`EcosystemIntegrationClassifier` owns the pure type-name and starter-method
classification policy, while `EcosystemIntegrationPresenceBuilder` projects
signals and broader public-type evidence into the legacy presence flags. The
classifier owns no traversal or output state, and the presence path reuses the
same projection rather than implementing a second scanner.

Integration and extension rows retain their existing presentation equality,
but actionable API rows also carry the `MemberAnchor`, structured declaring
type, and named receiver and return signature types derived during that same
guarded traversal. Type rows retain their exact
`MetadataTypeDefinitionName`. Opportunities similarly retain a structured
source definition and, where policy names an exact candidate, a structured
assembly/type target. These values are composition currency; consumers must not
recover graph endpoints from `Name`, `API`, `LookFor`, or other display text.
One compact API presentation row may retain multiple structured overload
observations; presentation deduplication does not discard their distinct member
anchors or signature endpoints.

`EcosystemIntegrationScannerTests.Scan_ProjectsExactOrderedPublicCurrencyAndPresence`
gates public-method filtering, signal kind and shape, row order, and parity
between direct and precomputed presence paths.

## Group-scoped queries

`AssemblyContextIntegrationsQuery` scans an entire assembly context group. It
visits each participant sequentially in group order and returns both ecosystem
and OpenTelemetry evidence with the participant's opaque identity and
resolution provenance. It does not deduplicate signals across assemblies:
companion assemblies may expose different useful currency, and preserving the
producing assembly lets later composition decide how to group or present it.

Image acquisition rejection remains explicit beside available participant
results, so a budget-limited group cannot look like a complete group with fewer
integrations. Late malformed-metadata mapping and preflight malformed-managed
metadata isolation are gated by the package command tests named below. The
query reuses the workspace's immutable snapshots and does not reopen paths or
streams.
`AssemblyContextIntegrationsQueryTests.RegistryRun_ScansEveryParticipantInOrderAndReusesSnapshots`
and
`AssemblyContextIntegrationsQueryTests.Execute_CarriesAcquisitionFailureBesideLaterResults`
gate participant ordering, snapshot reuse, and general partial acquisition.
`AssemblyContextIntegrationsQueryTests.Execute_ReportsBudgetExhaustionAsIncompleteEntry`
gates the budget-limited case.

The library CLI and package `--all-libraries` host execute this query when a
focused detected-integration section is selected. `Integration: Opportunities`
binds to `AssemblyContextIntegrationOpportunitiesQuery`, which declares the
Integrations query as a typed prerequisite. The dependent query composes the
existing-integration set from the prerequisite result, scans the same immutable
participant snapshot for missing registration surfaces, and preserves
available, rejected, and failed participant outcomes. Its declared local cost
is network-free, while the registry exposes the unbounded transitive cost of its
Integrations prerequisite.

The section catalog binds each member of the family to its owning query
definition by object identity and owns a separate group-query registry because
the queries consume an `AssemblyContextGroup`, not a single-library scanner
context.

The command creates one group for the selected assembly set, projects each typed
entry into the corresponding `LibraryInspection`, and retains the workspace's
authoritative immutable image for the rest of that library inspection. A path
retarget after query execution therefore cannot mix one assembly's integration
evidence with another assembly's metadata or opportunity evidence.
`AssemblyContextIntegrationsRunner_LendsTheQueriedSnapshotToLibraryInspection`
gates that shared-image boundary.

`AssemblyContextIntegrationsQueryTests.Execute_ComposesOpportunitiesFromTypedIntegrations`
gates typed prerequisite composition and suppression of integrations already
present.
`AssemblyContextIntegrationsQueryTests.RegistryRun_OpportunityQueryUsesOneImmutableSnapshot`
gates reuse of the acquired image across both queries.
`AssemblyIntegrationOpportunitiesFailure_ProjectsToItsSection` gates the
section-specific structured failure surface. Independently inducing a late
opportunity metadata-decode failure remains unverified. Cancellation-aware
group execution and optional concurrency remain later slices.

`InspectionGraphIntegrationsQuery` composes the group-scoped extension,
Integrations, opportunity, and reference producers over one complete loaded
workspace context. It resolves structured signature scopes only through each
participant's frozen binding policy, verifies the selected participant defines
the exact structured type, and joins package ownership only by acquisition
registration. The resulting `api.extension`, `integration.observed`,
`metadata.reference`, and `integration.opportunity` occurrences retain their
native evidence and semantic direction.

Before projecting opportunities, the composer reconciles co-dependent
assemblies: when one exact adapter member both extends a source SDK type and
returns the requested integration type, that observed adapter fulfills the raw
per-assembly opportunity for that exact acquired source type. It does not
suppress a same-spelled type from another acquisition. The locked OpenAI and
Bedrock adapters therefore suppress their local MEAI gaps while Azure OpenAI
retains its explicit `IChatClient` opportunity. Other opportunity policies
without structured graph targets remain ordinary opportunity rows and are not
invented as graph relationships.

Repeated producer observations collapse by each relationship's declared
occurrence identity before document construction. Successfully bound
metadata-reference rows normalize by semantic ECMA assembly identity, while
failed rows retain each exact metadata spelling. Missing out-of-context
references remain outside the selected graph. Explicit induced sets apply the
same rule to missing extension and Integration endpoints: `BindingMissing`
details cannot prove that an absent endpoint belongs to the requested subject
closure, so projection removes those details before deciding which failure
targets survive. Other details on the same target remain visible. Unavailable,
ambiguous, rejected, or selected-outside-context bindings remain visible as
failures.
Named signature scopes use the same semantic assembly-identity equivalence for
occurrence identity, so case and neutral-culture spelling variants do not
fabricate distinct extension or Integration observations.
Multiple producer failures for one graph subject aggregate into one targeted
failure with typed per-producer details, preserving the document's
descriptor/target uniqueness contract without discarding evidence. Reference
binding details retain the exact metadata reference identity, including when
multiple references fail with the same binding outcome.

`InspectionGraphIntegrationsQueryTests.Execute_ProjectsLockedIChatClientEvidenceAcrossPackageGroups`
gates the locked topology and the absence of a fabricated call;
`PackageAndTypeModesShareSemanticIntegrationOccurrences` gates the shared
dual-lens receipts; and
`Execute_DoesNotJoinAmbiguousMatchingAssemblyIdentities` gates the close
acquisition-identity case.
`Execute_ExplicitInducedSetOmitsOutOfContextBindingMissing`,
`Execute_ExplicitInducedSetRetainsActionableMixedFailureDetail`, and
`Execute_ExplicitInducedSetRetainsUnavailableSelectedBinding` gate the explicit
failure-boundary rule.

Package `--all-libraries` creates one binding-consistent group per package asset
directory, preserving non-`net*` framework and runtime contexts, so `--tfm all`
never combines different binding universes. Every root receives its own
`AssemblyDependencyResolver`, and
`SourceRelativeAssemblyGroupBindingPolicy` composes those resolvers behind the
one shared policy version required by the group. For each participant in group
order, the query and asynchronous library pipeline consume one retained
snapshot before releasing it and advancing. This preserves one complete
binding universe without retaining the cumulative bytes of every package
assembly.

The package host correlates query entries to libraries by acquisition
registration and projects their evidence or typed failure into existing
Finding properties. Remote provenance uses the coordinate resolved by package
acquisition, not package-controlled nuspec fields. A local archive uses a
trimmed, valid nuspec coordinate when available and otherwise carries
local-archive provenance.

When Opportunities is selected, the package host executes the typed
Integrations prerequisite and dependent Opportunities query inside the same
participant callback before releasing the retained image. A prerequisite
rejection or failure becomes the corresponding typed Opportunities outcome;
a compatibility-skipped participant emits no gap rows. Ecosystem and
OpenTelemetry evidence form one grouped prerequisite outcome, so malformed
participant metadata fails that grouped unit. Grouped failures leave successful
rows renderable, emit one warning per affected library and reason, and make the
command incomplete with a nonzero exit code. Direct `library` and package
`--library` remain single-assembly controls.

`PackageIntegrationsWorkspaceTests.Create_PartitionsTfmsAndRetainsParticipantGeneration`
and `Create_PartitionsNonNetFrameworkFolders` gate framework partitioning.
`Create_PartitionsSameFrameworkAcrossAssetContexts` gates package asset context
partitioning. Together they gate correlation, provenance, and
retained-generation reuse.
`PackageIntegrationsWorkspaceTests.UseAssemblyAsync_ReleasesParticipantBeforeAdvancing`
gates participant-at-a-time retention.
`PackageIntegrationsWorkspaceTests.OpportunityDemand_UsesTheStreamingParticipantSnapshot`
gates typed dependent-query execution before release.
`PackageIntegrationsWorkspaceTests.UnreadablePreflight_DoesNotFallBackToPathInspection`
gates terminal failure for unreadable managed participants.
`PackageIntegrationsWorkspaceTests.OpportunityOnlyDemand_RequiresGroupedIntegrations`
and
`PackageIntegrationsWorkspaceTests.IntegrationRejection_SuppressesOpportunities`
gate prerequisite activation and failure-safe opportunity composition.
`PackageIntegrationsWorkspaceTests.LocalAcquisition_UsesOnlyValidNuspecCoordinates`
and `RemoteAcquisition_UsesResolvedCoordinate` gate acquisition-owned
provenance. `GroupedIntegrationsFailure_IsVisibleAndDeduplicated` gates
diagnostic composition and the shared nonzero completion status used after
Markdown, count, tabular, or JSON output.
`PackageCommand_AllLibraries_GroupedFailureSurvivesHostFailureAcrossOutputPaths`
gates that status independently of later host inspection across Markdown,
JSON, count, and tabular output.
`PackageCommand_AllLibraries_BlankAssemblyNameDoesNotAbortHealthyParticipants`
and
`InspectionAcquisitionPlanTests.PathFactories_BlankAssemblyName_ReturnNoDescriptor`
gate malformed participant isolation.
`PackageCommand_AllLibraries_MetadataOverflowPreservesHealthyOutput` gates
preflight decoder-failure isolation.
`PackageCommand_AllLibraries_MalformedMetadataPreflightIsIncompleteAcrossOutputPaths`
gates visible incomplete status for malformed managed metadata across Markdown,
JSON, count, and tabular output.
`PackageIntegrationsWorkspaceTests.ApplyAssemblyIntegrationsEntry_PopulatesFindings`
and `GroupedEvidence_SuppliesIntegrationPresence` gate projection and
duplicate-scan avoidance.
`AssemblyContextIntegrationsQueryTests.Execute_CarriesBroadPresenceBeyondEvidenceRows`
gates preservation of presence flags that are broader than rendered evidence
rows.
`AssemblyContextIntegrationsQueryTests.Execute_OpenTelemetryEvidenceDoesNotBroadenLegacyPresence`
gates the close negative where an evidence row does not satisfy the legacy
OpenTelemetry support predicate. Existing
`PackageCommand_AllLibraries_*` tests gate rendering compatibility.

Cancellation-aware execution, optional concurrent execution, and migration of
other command paths remain later slices.

## Workspace Census target

**Status:** incremental implementation. The configured concept catalog,
producer-policy catalog, Integration analysis descriptor, Census request
declarations, and their named capability gates are implemented by
`IntegrationConceptCatalog` and `IntegrationAnalysisCatalog`. The existing
Library-targeted sections and Integration graph now retain those exact concept
descriptors while preserving their compatibility labels and output.

The projection-neutral core model is also implemented:
`IntegrationCensusSnapshot` validates the declared source-participant roster,
selected Type set, binding-context set, terminal source and producer receipts,
coalesced candidates, candidate-attempt address product, dispositions, and
suppression proofs. Census execution, inventory, graph correspondence, matrix
projection, and their remaining gates are still target design.

The Census is one Integration analysis over one finite universe. It is not a
loop that runs the existing Library-targeted question once per participant.

### Request binding

The Integration analysis descriptor consumes the request topology from
[Analysis surfaces and universes](analysis-surfaces-and-universes.md):

| Request field | Integration Census binding |
| --- | --- |
| Analysis | One Integration analysis descriptor with a finite configured concept catalog |
| Report surface | One owner-issued Workspace identity in a report-domain-only target role |
| Universe | One finite owner-issued population of acquired Type evidence with participant outcomes and provenance |
| Mode | `Census` |
| Projection | Candidate rows, sparse library-by-concept matrix, or Integration graph |

The descriptor declares these combinations before producer execution.
Existing Targeted Library behavior and graph-supported Member and Type anchors
remain separate; this target does not redefine those bindings. Widening the
Census universe never changes its Workspace report surface, and selecting a
graph never changes Census into a graph-specific question mode.

### Configured Integration concepts

One build exposes a finite `IntegrationConceptDescriptor` catalog. Each
descriptor has a stable ID, display label, and the producer
policies and relationship descriptors that may supply its evidence. The
catalog, rather than observations or registered Section names, is the source
for structural capability introspection.

`IntegrationConceptCatalog` owns the exact descriptors and their
`IntegrationProducerPolicyDescriptor` bindings in Metadata.
`IntegrationAnalysisCatalog` binds those exact values to query prerequisites,
graph relationship descriptors, typed universe requirements, and the generic
`AnalysisDescriptor`. Scanner and opportunity evidence retain the exact
concept and producer-policy descriptors; focused-section and graph consumers
bind to the same concept identity.

The current `EcosystemIntegrationNames` values remain compatibility labels for
existing Finding, JSON, selector, and display contracts. Labels are
presentation and never become candidate identity. A compatibility record
constructed with an unknown label has no configured descriptor; product
composition that requires configured Integration identity rejects that state
instead of minting identity from the label. Its positional label and retained
descriptor stay synchronized across record cloning, while descriptor access is
non-positional and does not expose the cyclic catalog through default JSON
serialization.

Structural discovery lists every configured concept even when the selected
universe yields no candidate for it. Request capability separately validates
the Workspace surface, Census mode, finite Type-evidence universe, and selected
projection. Neither operation scans metadata or probes Section effectiveness.

### Universe capability requirements

The implemented Integration analysis descriptor issues a closed, ordered set of typed
universe requirements. Each requirement has a stable identity, names the
configured Integration concepts that depend on it, and declares one provider
capability. The Workspace Census requires:

- finite selected-Type population membership with owner-issued Type identity;
- ordered source participants with typed outcomes and authoritative provenance;
- one structured Integration evidence capability requirement for each producer
  policy attached to a configured concept;
- owner-issued stable, comparable binding-context identity and deterministic
  context order for every context in which source evidence is evaluated;
- structured peer-reference binding within each declared binding context;
- exact peer resolution, including terminal forwarding outcomes, over one
  finite owner-issued binding/comparison domain; and
- retained completeness limits plus rejected, unavailable, and failed members.

Producer-policy requirements remain distinct even when several policies emit
the same evidence kind. Each requirement names exactly the concepts that policy
can inform, so a provider that supplies `integration.observed` but not
`integration.opportunity` evidence satisfies only the corresponding
requirements. Binding capability likewise does not imply binding-context
identity: the latter must be owner-issued, stable and comparable for attempt
addressing, and ordered for deterministic Census and matrix projection.

The universe provider declares which of those capabilities its description
supplies without scanning the population. Generic request-capability validation
compares those declarations with the Integration-issued requirements before
producer execution. A typed unsatisfied-universe rejection identifies every
unmet requirement and its affected concept descriptors, so capability
introspection can explain which Integration questions the supplied universe
cannot answer. A validated plan retains the exact satisfied requirement
identities and catalog revision; it does not replace them with a generic
`TypeEvidenceAvailable` flag.

Passing request-capability validation proves that the provider can perform the
required operations over its declared finite boundary. It does not promise
that every peer later discovered by producer execution is present or healthy.
A particular unavailable, ambiguous, rejected, malformed, or unresolved peer
is an execution outcome that makes the affected Census attempt incomplete; it
does not retroactively change structural or request capability. Conversely, a
provider that cannot perform exact peer resolution or report completeness is
rejected before execution rather than allowed to manufacture `Out` or empty
results.

The core snapshot constructor receives the provider's ordered source
participants, selected Types, and binding contexts as owner-issued model input.
The generic universe description intentionally retains those owners and
capabilities rather than duplicating their participant collections. Snapshot
compatibility therefore requires the same owner-issued report-surface and
universe objects, not merely independently constructed values with similar
display or boundary data.
[Analysis universe realization](analysis-universe-realization.md) owns the
future execution-time handoff that binds those exact requirements to
Workspace-backed, capability-owner-issued rosters, context incidence,
resolution access, and lifetimes. The current core snapshot still constructs
the full candidate-by-binding-context product and accepts no owner-issued
incidence input. [#5319](https://github.com/richlander/dotnet-inspect/issues/5319)
owns that Integration adoption before the Census executor can consume the
handoff. The eventual consumer receives no mutable Workspace state and no
context identity inferred from a group object or binding-policy version.

### Producer-policy attempt accounting

Passing capability validation does not prove that each required producer policy
executed. The producer derives an expected
`IntegrationProducerPolicyAttemptAddress` set from the validated plan before
execution. The expected set is the Cartesian product of every required
source-participant identity and every producer-policy requirement retained by
that plan. One address combines one identity from each set; no runtime
applicability predicate may remove an address. A policy that finds no applicable
source evidence returns an empty `Completed` result. Producer evidence is
issued before peer binding, so binding context is not part of this address;
completed evidence is evaluated later in every declared binding context.

Every expected address has exactly one terminal
`IntegrationProducerPolicyAttempt`:

| Outcome | Meaning |
| --- | --- |
| `Completed` | The policy returned an ordered structured-evidence set, which may be empty. |
| `Unavailable` | A typed participant or policy prerequisite prevented execution and makes the affected domain incomplete. |
| `Failed` | Policy execution failed with a typed cause and makes the affected domain incomplete. |

Missing, duplicate, and extraneous producer-policy attempts reject Census
construction. Only `Completed` evidence contributes to the candidate frontier.
An empty `Completed` result is positive execution evidence; a missing receipt,
`Unavailable`, or `Failed` result cannot manufacture a zero concept count,
empty cell, `Out`, or absence claim.

When multiple completed policies emit equal candidate coordinates, the Census
creates one `IntegrationCandidateIdentity` and retains ordered correspondence
to every issuing producer-policy attempt. Producer-policy identity is evidence
provenance, not candidate or candidate-attempt identity. The coalesced candidate
is evaluated once in each binding context, while distinct binding contexts
continue to produce distinct candidate-attempt addresses.

### Evidence-visible frontier

The Census begins with structured Integration evidence from source Types
admitted by the selected universe. It considers:

- `integration.observed` observations with the same source currency plus one
  Integration concept descriptor and named peer Type; and
- `integration.opportunity` observations with one structured source Type,
  concept descriptor, and exact policy-issued target.

Every Census candidate therefore has one configured Integration concept.
`api.extension` and `metadata.reference` observations may inform binding,
fulfillment, and graph context, but they are not independent Census candidates.
Display-only signal rows, legacy presence flags, and rendered graph failures
cannot create candidates.

The implemented candidate constructor closes the evidence arms:
`integration.observed` requires a structured named-Type peer, while
`integration.opportunity` requires a structured Type source and the exact
policy-issued target. A completed producer receipt is accepted only when the
candidate belongs to its addressed participant and relationship/concept policy,
and its evidence-bearing source Type is present in the selected universe.

Every candidate retains correspondence to the completed producer-policy
attempts that supplied its structured evidence. The frontier is the coalesced
candidate-identity set from those receipts, not one candidate per evidence row
or policy execution.

The frontier is intentionally not a global catalog. A candidate remains visible
while its evidence-bearing source is admitted even when its peer is outside the
selected universe. Removing the only admitted source evidence removes the
candidate; a stale completed receipt for an unselected source Type rejects
snapshot construction rather than retaining the candidate. The Census does not
invent a theoretical row merely because the concept catalog knows that an
Integration kind exists.

### Candidate identity

`IntegrationCandidateIdentity` is issued before peer binding and universe
admission. It combines:

- the Integration relationship descriptor;
- the required Integration concept descriptor;
- one Integration-owned `IntegrationCandidateSourceIdentity`; and
- the structured named peer reference or policy-issued target.

Disposition, resolved peer subject, graph-local ids, labels, and rendered names
do not participate. The same evidence therefore keeps one candidate identity
when a finite universe variant adds or removes its peer.

Equality compares the relationship and concept descriptor identities, the
candidate-source identity, and the peer's exact metadata Type name plus
structured scope. Assembly-reference scopes use semantic ECMA
assembly-identity equivalence; current-assembly, intrinsic-core-library, and
module-reference scopes remain distinct, and module names compare ordinally.
The acquisition path, selected-universe membership, resolved peer registration,
and parent package reached during binding cannot split or merge candidates.

`IntegrationCandidateSourceIdentity` is distinct from graph occurrence
identity. When the acquisition owner supplies any portable
`RealizedMemberCoordinate`, its portable form retains that coordinate, semantic
assembly identity, and structured member or Type identity. This includes
package, platform, and digest-bearing embedded coordinates. For a source with
no portable coordinate, its workspace form retains the acquisition
registration plus the same structured source identity and is stable only inside
that workspace generation. The producer never correlates two local artifacts
from assembly spelling alone.

Current graph occurrence identity remains registration-scoped and continues to
own deduplication inside one graph document. A Census candidate retains explicit
correspondence to that receipt when projected as a graph, but graph occurrence
equality neither defines nor overrides candidate equality.

The focused add/remove gate uses multiple finite universe descriptions over one
retained superset workspace. Cross-generation correlation is asserted only for
portable candidate identities; a new registration by itself is not proof of
the same source.

### Candidate attempt accounting

The producer derives one ordered `IntegrationCandidateAttempt` for every
candidate evaluation address in the pre-binding frontier. The address combines
the portable or workspace-scoped `IntegrationCandidateIdentity` with the
universe provider's owner-issued binding-context identity. Each attempt has
exactly one outcome:

| Outcome | Meaning |
| --- | --- |
| `Classified` | Peer resolution and Integration policy completed; carries one `In` or `Out` candidate. |
| `Suppressed` | Typed Integration policy proved that another retained observation in the same binding context fulfills or supersedes this candidate. |
| `Failed` | Binding, validation, or candidate policy could not produce a trustworthy classification. |

The expected attempt-address set derives from the coalesced structured producer
evidence and every context in which that source evidence is evaluated, before
peer admission, suppression, or graph projection. Missing, duplicate, and
extraneous attempts reject Census construction. A fulfilled raw opportunity is
the required suppression canary: it remains accounted for by address but does
not become an inventory row, graph failure, or incomplete outcome.

One candidate identity may therefore correspond to multiple attempts. The same
portable source and peer can bind in two contexts and correctly produce
different outcomes without splitting the candidate identity or collapsing the
contexts. Attempt identity, disposition, and binding context do not become
candidate identity.

`Suppressed` is a completed policy decision with a closed Integration-owned
reason and correspondence to the fulfilling observation. The observation and
its successful resolution must use the attempt address's exact binding-context
identity; evidence from another context cannot suppress this attempt. The
suppression receipt also retains the exact acquired source Type and resolved
target path used by the fulfillment policy. The source must match the
opportunity source, the path must retain the opportunity's exact policy-issued
lookup, and its terminal must match the classified observation's terminal
target. The observation's candidate source remains the adapter member or Type
that supplied observed evidence; it is not required to equal the SDK source
Type retained by the fulfillment proof. A classified `In` or `Out` observation
may fulfill the opportunity because both are successful exact resolution
outcomes in the same binding context. `Failed` retains its typed cause and
makes the affected Census incomplete. Only `Classified` attempts contribute
candidate inventory.

### Candidate disposition

Every successfully classified candidate has one closed disposition:

| Disposition | Meaning |
| --- | --- |
| `In` | The source is admitted, the exact peer resolves to an admitted universe Type, and normal Integration admission accepts the candidate. |
| `Out` | The exact peer resolves and validates in the universe provider's healthy binding/comparison domain, is outside the selected finite population, and normal Integration policy otherwise accepts the candidate. |

`Out` is a statement about this requested universe, not global absence,
request capability, Finding inspection, graph failure, or package
compatibility. Its typed reason is `PeerOutsideUniverse`; explanatory text is
not classification.

`Out` is exposed only by the candidate inventory and projections, such as the
sparse matrix, that explicitly retain universe disposition. It never becomes a
Finding, focused Library Integration row, graph node, graph edge, graph
occurrence, or graph failure.

The binding/comparison domain may be a retained superset of the selected Type
population, but it is owner-issued finite input rather than implicit
acquisition or widening. Classification consumes the terminal owner-issued
type-resolution outcome. A successful forwarding chain is exact resolution:
its terminal definition's selected-universe membership determines `In` or
`Out`, and its forwarding hops remain evidence. Exact terminal resolution in a
healthy domain is the positive proof that distinguishes `Out` from an unknown
peer.

The core model requires each successful resolution to retain the exact
candidate peer lookup that the binding owner consumed. It rejects a path that
changes the candidate Type name or repeats an exact Type identity, but it does
not reconstruct assembly selection from the lookup: version unification,
wildcards, platform roll-forward, and other candidate-selection policy remain
owned by Metadata binding. The terminal may belong to a different assembly
after forwarding, and its selected-universe membership determines disposition.
Module-reference lookup cannot currently produce a classified Census attempt
because the resolution owner reports that scope as unsupported.
Intrinsic-core-library resolution remains an owner-issued binding result
because structural core library authentication belongs to Metadata acquisition
rather than the Census model.

An unacquired, unavailable, ambiguous, rejected, malformed, or
selected-but-missing binding cannot become `Out`. A forwarding cycle, rejected
hop, missing terminal definition, or other chain that does not resolve
successfully has the same failure boundary; forwarding itself is not failure.
The corresponding typed producer failure remains visible and makes the
affected Census attempt incomplete. `OpportunityTargetMissing` and other
actionable opportunity failures likewise remain failures. Candidate inventory
does not replace or downgrade them.

`In` does not promise one graph edge for every row. Normal relationship
deduplication may combine multiple physical candidates into one logical edge,
and the candidate retains its occurrence and admitted-edge correspondence.

### Census result and completeness

`IntegrationCensusSnapshot` is the immutable, projection-neutral producer
result. It contains:

- the exact analysis, report-surface, universe, and Census-mode inputs retained
  from a validated plan, plus the configured concept catalog revision;
- the descriptor-issued producer requirements used to validate those shared
  inputs;
- one ordered attempt for every required source participant;
- one ordered attempt for every expected source-participant and producer-policy
  requirement address;
- one ordered attempt for every pre-binding candidate evaluation address;
- the classified candidates, policy suppressions, and typed failures;
- exact universe completeness and rejected-member inputs; and
- admitted relationship identity sufficient for later projection.

One `IntegrationCensusProjectionResult` retains the exact five-field validated
request, including its one projection descriptor, and one compatible Census
snapshot plus the requested payload. Rows, matrix, and graph are independently
validated requests. They may reuse one snapshot only when their analysis,
surface, universe, mode, descriptor requirements, and catalog revision are
identical. Analysis, surface, and universe compatibility use the same
owner-issued object instances; projection is intentionally excluded from
snapshot compatibility. Reuse never treats one projection's validation as
authorization for another.

The existing `AssemblyIntegrationsEntry.Available`, `Rejected`, and `Failed`
topology is the starting point for participant attempts. A Census is complete
only when every required source participant is healthy, every expected
producer-policy attempt is `Completed`, and every required candidate attempt is
`Classified` or `Suppressed`. Available rows may survive beside failures, but
incomplete execution cannot manufacture zero concept counts, empty cells,
`Out` rows, or absence claims for the failed domain.

A complete Census with no candidates is a successful empty Integration result.
It does not use Finding `Absent`. `In` and `Out` remain Integration-owned
universe dispositions and never alias Finding `Present`, `Absent`, `Missing`,
or `Failed`.

### Candidate row projection

`Integration Inventory` is the named row Section. One row is one classified
candidate attempt and retains:

- candidate identity;
- binding-context attempt identity;
- issuing producer-policy attempt correspondence;
- concept and relationship descriptor identities;
- typed source member or Type identity;
- source assembly and authoritative package or platform provenance when
  available;
- typed peer lookup currency;
- terminal resolved peer definition, authoritative provenance, and ordered
  forwarding hops for every successfully resolved `In` or `Out` attempt;
- `In` or `Out` plus the typed `Out` reason; and
- admitted relationship identity when `In`.

The Section is `Verbose`, `NetworkFree`, and `ExplicitOnly`. It does not enter
the existing Library `@Integrations` catalog, so selecting that category keeps
its current focused-section meaning and bounded output. A workspace host may
assign the Section to an authored workspace category under the
[Section model](section-model.md) contract.
Those declarations describe the Integration-produced row set;
[Section model](section-model.md) remains authoritative for discovery,
selection, effectiveness, category expansion, count, and empty-section
behavior. [Output shapes](output-shapes.md) remains authoritative for column
projection, row filtering and windows, structured output, and rendering.
Ordinary Integration graph projection does not make hundreds of inventory rows
part of a default medium-verbosity document.

### Sparse matrix projection

The matrix is a lossless grouping of the same candidate rows by owner-issued
source participant within its binding context and by Integration concept
descriptor. A repeated Library in two contexts produces two typed matrix rows
rather than one merged display row. A non-empty cell retains the ordered
attempt and candidate identities and separate `In` and `Out` counts. The
browser renders that typed projection; it does not rescan metadata or infer
support from labels.

Zero is displayable only when the source-participant attempt is healthy, every
producer-policy attempt for that participant whose requirement names the cell's
concept is `Completed`, and every candidate attempt addressed to that
participant's source evidence, binding context, and concept is complete. A
producer-policy `Unavailable` or `Failed` receipt makes its
participant/concept domain incomplete across every binding context because the
receipt is context-free. A `Failed` candidate attempt makes only its
participant/context/concept cell incomplete. Neither failure contaminates
another participant or concept, and a candidate failure does not contaminate
another binding context. An incomplete cell is explicit and is never rendered
as zero or omitted as if no Integration were observed. Matrix ordering derives
from workspace participant and context order plus concept-catalog order, not
discovery timing. The ordering gate uses discovery order that deliberately
differs from all three declared orders and includes one participant repeated
across binding contexts.

### Graph projection

The graph adapter consumes an independently validated graph request plus a
compatible Census snapshot. It admits `In` candidate-attempt occurrences
through the existing relationship and induced-set contracts and retains
candidate-to-attempt plus attempt-to-occurrence and attempt-to-edge
correspondence. `Out` attempts produce no graph edge, failure, node, or
synthetic occurrence; they remain addressable through `Integration Inventory`.

The graph document continues to own logical edge rows, graph-local ids,
grouping, characteristics, limits, failures, and rendering. The Census does not
reinterpret graph seed admission or induced-set closure. In particular,
candidate inventory is produced before
`InspectionGraphInducedSetProjection` removes non-admitted occurrences; it is
never reconstructed from the projection's surviving edges or filtered
`BindingMissing` details.

### Peer lookup and parent provenance

`IntegrationPeerLookup` is the candidate's handoff currency. It retains the
exact `MetadataTypeDefinitionName`, the complete structured
`MetadataTypeReferenceScope` that named it, and any authoritative acquisition
provenance already known. Current-assembly, intrinsic-core-library,
assembly-reference, and module-reference scope arms remain typed and distinct.
The lookup also projects the Type full-name grammar accepted as a `find`
pattern without parsing a display label.

When acquisition provenance identifies the parent package or platform
coordinate, the row includes that coordinate directly. A consumer should not
force rediscovery of already known ownership. When the parent is unknown, the
typed lookup plus an owner-issued search scope is the only permitted handoff;
assembly-name-to-package guessing is forbidden.

For a forwarded peer, parent handoff uses the terminal resolved definition's
authoritative provenance, not the forwarding facade. The original peer lookup
and ordered forwarding hops remain beside that terminal result so the
classification and navigation path are auditable.

The Integration owner does not make `find` search an unbounded package feed.
If the current search owner cannot accept the typed lookup and a finite scope
that can discover an unknown parent, that gap is the separately owned
[#4979](https://github.com/richlander/dotnet-inspect/issues/4979)
prerequisite. A network-bound enrichment may consume its result only through
explicit host authorization; base Census execution and the inventory Section
remain network-free.

### Demo contract

The canonical demo uses one retained superset workspace and three finite Type
universe descriptions:

1. The first universe produces at least one `In` and one `Out` candidate.
2. The row Section and sparse matrix show the same candidate identities and
   disposition counts.
3. The `Out` peer full name is passed to `find` unchanged with its finite scope,
   or its authoritative parent coordinate is used directly.
4. Adding the peer's parent to the selected universe moves the same candidate
   from `Out` to `In` and admits its graph occurrence.
5. Removing that parent moves the candidate back to `Out` without a graph
   failure.
6. Removing the sole evidence-bearing source makes the candidate disappear
   instead of creating negative evidence.

The WASM app renders the shared row or matrix projection and contains no
Integration detection or disposition policy.

### Delivery

Implementation should land as focused slices:

1. configured concept and producer-policy catalog plus generic request
   capability declarations (implemented);
2. candidate identity, producer-policy and candidate attempts, disposition,
   and the projection-neutral Census snapshot (implemented core model);
3. `Integration Inventory` row Section and structured row output;
4. graph correspondence from `In` candidates without changing graph semantics;
5. sparse matrix projection and WASM demo; and
6. separately owned #4979 `find` prerequisite or optional enrichment for
   discovering an unknown parent.

Each slice must preserve current focused Library sections and explicit
Integration graph behavior until its replacement path has parity gates.

### Close negative cases

| Case | Required result |
| --- | --- |
| Configured concept with no observations | Structurally discoverable concept; no invented candidate |
| Complete healthy universe with no candidates | Successful empty Census |
| Source admitted, peer resolves in healthy superset but is unselected | `Out(PeerOutsideUniverse)` |
| Source admitted, peer is unacquired | Typed failure; never `Out` |
| Exact peer admitted and candidate accepted | `In` with occurrence correspondence |
| Raw extension observation has no Integration concept | Supporting evidence; no candidate |
| Raw opportunity fulfilled by an observed adapter | Accounted `Suppressed` attempt |
| Fulfilling adapter exists only in another binding context | Current-context attempt is not suppressed |
| Universe provider lacks exact peer-resolution capability | Typed unsatisfied-universe rejection before execution |
| Provider supplies peer binding but no stable binding-context identity | Typed unsatisfied-universe rejection before execution |
| Provider supplies observed but not opportunity evidence | Rejection names the unmet policy requirement and affected concepts |
| Advertised producer policy omits its execution receipt | Census construction rejects the missing attempt; no zero or `Out` |
| Two policies emit equal candidate coordinates | One candidate identity retains both policy correspondences and has one attempt per context |
| Policy fails for a participant/concept evaluated in two contexts | Both context rows are incomplete; unrelated cells may show zero |
| Candidate attempt fails in one binding context | Only its cell is incomplete; the same participant/concept in another context may show zero |
| Capable provider cannot resolve one discovered peer | Failed incomplete attempt; request capability remains unchanged |
| Peer assembly unavailable or ambiguous | Typed failure; never `Out` |
| Selected peer assembly lacks the exact Type | Typed failure; never `Out` |
| Forwarder resolves to selected terminal Type | `In` with forwarding evidence |
| Forwarder resolves to healthy unselected terminal Type | `Out` with forwarding evidence |
| Forwarded `Out` terminal parent is known | Terminal parent coordinate is the handoff |
| Forwarder cycle or failed terminal resolution | Typed failure; never `Out` |
| Source participant rejected or malformed | Incomplete attempt beside healthy rows |
| Parent package provenance known | Authoritative coordinate rendered directly |
| Parent provenance unknown | Typed lookup retained; no assembly-name guess |
| Embedded source reacquired with the same digest-bearing coordinate | Same portable source identity |
| Same candidate under wider universe | Same identity; disposition may change |
| Same portable candidate evaluated in two binding contexts | Two addressed attempts; outcomes remain distinct |
| Sole source evidence removed | Candidate disappears |
| Multiple candidates collapse to one edge | Distinct candidate rows share edge correspondence |
| Discovery order differs from participant, context, and catalog order | Matrix follows the three declared orders |
| Rows and matrix requested independently | Exact plans share one compatible snapshot and candidate counts |
| Graph requested independently | Exact plan projects only `In` candidate occurrences |

### Workspace Census verification

The catalog and request-capability slice is verified by:

- `IntegrationCapability_ListsConfiguredUnobservedConcepts`
- `IntegrationCapability_DoesNotExecuteProducersOrProbeSections`
- `IntegrationCapability_RejectsUnsupportedCensusRequestBeforeExecution`
- `IntegrationCapability_DeclaresTypedUniverseRequirementsByConcept`
- `IntegrationCapability_UnsatisfiedUniverseNamesRequirementsAndConcepts`
- `IntegrationCapability_ValidatedUniverseRetainsExactRequirementIdentities`
- `IntegrationCapability_RequiresStableOrderedBindingContextIdentity`
- `IntegrationCapability_PartialProducerPolicyEvidenceNamesAffectedConcepts`
- `IntegrationCapability_EveryDeclaredUniverseRequirementHasPositiveAndNegativeCoverage`

Existing behavior retaining the same descriptor identity is additionally gated
by
`EcosystemIntegrationScannerTests.Scan_ProjectsExactOrderedPublicCurrencyAndPresence`,
`AssemblyContextIntegrationsQueryTests.Execute_ComposesOpportunitiesFromTypedIntegrations`,
`InspectionGraphIntegrationsQueryTests.Execute_ProjectsLockedIChatClientEvidenceAcrossPackageGroups`,
and `SectionPipelineTests.IntegrationSections_BindToGroupQueriesByIdentity`.

The projection-neutral core-model slice is verified by:

- `IntegrationCatalog_RevisionMirrorsDeclarationShapeAndPolicyMapping`
- `IntegrationCandidate_IdentityDoesNotContainDispositionOrGraphLocalIds`
- `IntegrationCandidate_EquivalentAssemblyReferenceScopesShareIdentity`
- `IntegrationCandidate_DifferentRelationshipConceptSourceTypeOrScopeSplitIdentity`
- `IntegrationCandidate_DistinctScopeKindsSplitIdentity`
- `IntegrationCandidate_ModuleScopeNamesCompareOrdinally`
- `IntegrationCandidate_PolicyTargetAssemblyNameComparesOrdinalIgnoreCase`
- `IntegrationCandidate_PortableSourceIdentityMatchesStructurallyEquivalentCoordinates`
- `IntegrationCandidate_WorkspaceIdentityIsolatedByAcquisitionRegistration`
- `IntegrationCandidate_MemberSourceRejectsDeclaringTypeAnchorDisagreement`
- `IntegrationCandidate_RawExtensionRelationshipIsNotACandidate`
- `IntegrationCandidate_CrossedRelationshipArmsAreRejected`
- `IntegrationCensus_ParticipantReceiptsExactlyCoverDeclaredParticipants`
- `IntegrationCensus_RejectedOrFailedParticipantMakesCensusIncomplete`
- `IntegrationCensus_ProducerReceiptsCoverParticipantByRetainedPolicyProduct`
- `IntegrationCensus_ProducerCompletedEvidenceRejectsMismatches`
- `IntegrationCensus_UnavailableOrFailedProducerYieldsNoCandidatesAndIncompleteness`
- `IntegrationCensus_DuplicateEvidenceCoalescesRetainingProducerCorrespondence`
- `IntegrationCensus_CanonicalizesShuffledReceiptProducts`
- `IntegrationCensus_CandidateAttemptsCoverCoalescedCandidatesByContext`
- `IntegrationCensus_SemanticContextProductUsesHashBackedAddressing`
- `IntegrationCensus_EmptyHealthyUniverseIsCompleteAndSuccessful`
- `IntegrationCensus_ClassifiedInRequiresSelectedTerminalPeer`
- `IntegrationCensus_ClassifiedOutRequiresUnselectedTerminalPeer`
- `IntegrationCensus_ClassificationRequiresTerminalPeerMatchingCandidate`
- `IntegrationCensus_ForwardedClassificationRetainsResolutionPath`
- `IntegrationCensus_ResolutionRejectsMismatchedLookupForwardingHopAndCycle`
- `IntegrationCensus_ResolutionRetainsLookupAcrossBindingPolicyVersionSelection`
- `IntegrationCensus_FailedCandidateHasNoDispositionAndIsIncomplete`
- `IntegrationCensus_SameCandidateAcrossContextsProducesDistinctAttempts`
- `IntegrationCensus_AddingOrRemovingSelectedPeerPreservesIdentityWhileFlippingDisposition`
- `IntegrationCensus_RemovingSelectedSourceMembershipRejectsStaleCandidate`
- `IntegrationCensus_SuppressionRequiresSameContextClassifiedObservedOfSameConcept`
- `IntegrationCensus_SuppressionRejectsSelfAndMissingFulfiller`
- `IntegrationCensus_SuppressionRejectsCrossContextFulfiller`
- `IntegrationCensus_SuppressionRejectsOpportunityFulfillingOpportunity`
- `IntegrationCensus_SuppressionRejectsWrongConceptFulfiller`
- `IntegrationCensus_SuppressionRejectsUnclassifiedFulfiller`
- `IntegrationCensus_SuppressionRejectsWrongProofSourceOrTarget`
- `IntegrationCensus_SnapshotCompatibilityIgnoresProjectionButRequiresSharedInputs`

The remaining target implementation is unverified until these named gates
land:

- `IntegrationCapability_CandidateFailureDoesNotChangeRequestCapability`
- `IntegrationCandidate_SourceIdentityIsIndependentOfGraphOccurrenceIdentity`
- `IntegrationCandidate_EqualEvidenceAcrossPoliciesCoalescesAndRetainsCorrespondence`
- `IntegrationCandidate_UnacquiredPeerCannotBeOut`
- `IntegrationCandidate_UnavailableAmbiguousOrMissingSelectedPeerIsFailure`
- `IntegrationCandidate_UnresolvedForwardingIsFailure`
- `IntegrationCandidate_RemovingSoleSourceRemovesCandidate`
- `IntegrationInventory_RowsRetainTypedSourcePeerAndProvenance`
- `IntegrationInventory_PeerLookupRetainsEveryTypeReferenceScopeArm`
- `IntegrationInventory_ForwardedInAndOutRetainTerminalDefinitionProvenanceAndHops`
- `IntegrationInventory_ForwardedOutUsesTerminalParentForHandoff`
- `IntegrationInventory_KnownParentUsesAuthoritativeCoordinate`
- `IntegrationInventory_UnknownParentNeverGuessesFromAssemblyName`
- `IntegrationInventory_FindPatternUsesTypeLookupGrammarUnchanged`
- `IntegrationInventory_IsExplicitNetworkFreeVerboseSection`
- `IntegrationInventory_DoesNotWidenLibraryIntegrationsCategory`
- `IntegrationMatrix_RetainsCandidateIdentityAndDispositionCounts`
- `IntegrationMatrix_RepeatedLibraryAcrossContextsRemainsDistinct`
- `IntegrationMatrix_IncompleteLibraryDoesNotRenderAsZero`
- `IntegrationMatrix_PolicyFailureDoesNotContaminateUnrelatedCells`
- `IntegrationMatrix_ProducerPolicyFailureIncompletesEveryBindingContextForItsConcept`
- `IntegrationMatrix_CandidateFailureDoesNotContaminateOtherBindingContexts`
- `IntegrationMatrix_OrdersByDeclaredParticipantContextAndConceptOrder`
- `IntegrationGraph_OnlyInCandidatesContributeOccurrences`
- `IntegrationGraph_OutCandidatesAreNeitherEdgesNorFailures`
- `IntegrationGraph_CandidateInventoryPrecedesInducedSetProjection`
- `IntegrationGraph_RetainsCandidateAttemptOccurrenceAndEdgeCorrespondence`
- `IntegrationGraph_MultipleCandidatesMayCorrespondToOneLogicalEdge`
- `IntegrationProjection_EachResponseRetainsItsExactValidatedRequest`
- `IntegrationProjection_ReuseRequiresCompatibleCensusSnapshot`
- `IntegrationProjection_RowsMatrixAndGraphShareOneAnalysisAndSnapshot`
- `IntegrationWasmDemo_RendersSharedProjectionWithoutDetectionPolicy`

The configured concept set, universe-requirement set and per-requirement
concept mapping, candidate-identity component set, peer-scope arm set, and
close-negative case sets should derive from their declarations so missing and
stale entries fail together. Every declared universe requirement has one
provider-satisfies positive case and one provider-omits negative case; adding a
requirement without those cases must fail
`IntegrationCapability_EveryDeclaredUniverseRequirementHasPositiveAndNegativeCoverage`.
The add/remove demo is the non-vacuity gate for candidate identity: removing
candidate identity or deriving it after admission must make the gate fail.

### Workspace Census non-claims

- No global catalog of theoretical integrations.
- No automatic acquisition or implicit widening of the selected universe.
- No network work during base Census or inventory production.
- No package ownership inferred from assembly names.
- No conversion of actionable graph or binding failures into `Out`.
- No change to Finding inspection or correlation states.
- No redefinition of analysis-request validation, workspace composition,
  graph induction, Section mechanics, output formatting, or `find` search
  scope.
- No promise that adding one package makes an otherwise incompatible candidate
  admissible.
- No portable identity claim for local source evidence without an
  acquisition-owner portable coordinate.

## Relationship to sections and categories

The focused `Integration:` sections are members of the `@Integrations` section
category. Categories are section-selection macros, not a new filtering
axis. They expand to command-local section sets and then normal section
renderability still applies.

Use:

```bash
-S @Integrations
-S "Integration: <focused integration>"
--count -S @Integrations
```

This keeps integrations aligned with section backpressure and schema discovery.
