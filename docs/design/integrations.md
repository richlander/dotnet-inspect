# Integrations

The `@Integrations` library category is a set of focused sections describing
ecosystem support discovered from assembly metadata. It answers:

```text
Which .NET ecosystem integration surfaces can a caller use from this library?
```

It is intentionally different from `Signals`. `Signals` is an evidence report.
Integration sections form a usability index: they point to APIs that are useful
currency for wiring the library into common .NET application systems.

Each focused integration section is named with an `Integration:` prefix (for
example `Integration: Logging`, `Integration: OpenTelemetry`) so alphabetical
section ordering clusters the whole family together.

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
occurrence identity before document construction. Metadata-reference rows first
normalize by semantic ECMA assembly identity. Missing out-of-context references
remain outside the selected graph, while unavailable, ambiguous, rejected, or
selected-outside-context bindings remain visible as failures.
Multiple producer failures for one graph subject aggregate into one targeted
failure with typed per-producer details, preserving the document's
descriptor/target uniqueness contract without discarding evidence. Reference
binding details retain the exact metadata reference identity, including when
multiple references fail with the same binding outcome.

`InspectionGraphIntegrationsQueryTests.Execute_ProjectsLockedIChatClientEvidenceAcrossPackageGroups`
gates the locked topology and the absence of a fabricated call;
`PackageAndTypeReadingsShareTheSameIntegrationOccurrences` gates the shared
dual-lens receipts; and
`Execute_DoesNotJoinAmbiguousMatchingAssemblyIdentities` gates the close
acquisition-identity case.

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
