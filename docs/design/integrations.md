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

## Group-scoped query

`AssemblyContextIntegrationsQuery` is the first typed query that runs across an
entire assembly context group. It scans each participant sequentially in group
order and returns both ecosystem and OpenTelemetry evidence with the
participant's opaque identity and resolution provenance. It does not deduplicate
signals across assemblies: companion assemblies may expose different useful
currency, and preserving the producing assembly lets later composition decide
how to group or present it.

Image acquisition rejection remains explicit beside available participant
results, so a budget-limited group cannot look like a complete group with fewer
integrations. Late malformed-metadata mapping is implemented but not yet
independently gated. The query reuses the workspace's immutable snapshots and does not reopen paths
or streams.
`AssemblyContextIntegrationsQueryTests.RegistryRun_ScansEveryParticipantInOrderAndReusesSnapshots`
and
`AssemblyContextIntegrationsQueryTests.Execute_CarriesAcquisitionFailureBesideLaterResults`
gate participant ordering, snapshot reuse, and general partial acquisition.
`AssemblyContextIntegrationsQueryTests.Execute_ReportsBudgetExhaustionAsIncompleteEntry`
gates the budget-limited case.

Package `--all-libraries` uses this query whenever its selected sections demand
Integrations. It creates one binding-consistent group per target framework, so
`--tfm all` never combines different framework universes. Every root receives
its own `AssemblyDependencyResolver`, and
`SourceRelativeAssemblyGroupBindingPolicy` composes those resolvers behind the
one shared policy version required by the group.

The host correlates query entries to libraries by acquisition registration and
projects their evidence or typed failure into the existing Finding properties.
For each participant in group order, the query and asynchronous library pipeline
consume one retained snapshot before releasing it and advancing. This preserves
one complete binding universe without retaining the cumulative bytes of every
package assembly. Metadata, body, Union Type, and Switch scanners borrow that
same image rather than reopening the package path.

The legacy Integrations scanner treats populated properties as complete. The
dependent integration-opportunity scanner composes available group results
without repeating metadata scanning and emits no gaps when its Integrations
prerequisite failed. Direct `library` and package `--library` remain
single-assembly controls.
`PackageIntegrationsWorkspaceTests.Create_PartitionsTfmsAndRetainsParticipantGeneration`
gates grouping, correlation, provenance, and retained-generation reuse.
`PackageIntegrationsWorkspaceTests.UseAssemblyAsync_ReleasesParticipantBeforeAdvancing`
gates participant-at-a-time retention.
`PackageIntegrationsWorkspaceTests.OpportunityOnlyDemand_RequiresGroupedIntegrations`
and
`PackageIntegrationsWorkspaceTests.IntegrationFailure_SuppressesOpportunities`
gate prerequisite activation and failure-safe opportunity composition.
`PackageCommand_AllLibraries_BlankNuspecIdentityFallsBack` gates canonical
package provenance fallback for malformed blank nuspec identity fields.
`PackageIntegrationsWorkspaceTests.ApplyAssemblyIntegrationsResult_PreventsLegacyRescan`
gates projection and duplicate-scan avoidance. Existing
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
