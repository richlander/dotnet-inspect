# Integrations

The `Integrations` library section is a roll-up of ecosystem support discovered
from assembly metadata. It answers:

```text
Which .NET ecosystem integration surfaces can a caller use from this library?
```

It is intentionally different from `Signals`. `Signals` is an evidence report.
`Integrations` is a usability index: it points to APIs that are useful currency
for wiring the library into common .NET application systems.

## User model

Start with the roll-up:

```bash
dotnet-inspect package Microsoft.Extensions.AI --library -S Integrations
```

Then select a focused section:

```bash
dotnet-inspect package Microsoft.Extensions.AI --library -S "Dependency Injection"
dotnet-inspect package Microsoft.Extensions.AI --library -S OpenTelemetry
```

The roll-up reports one row per detected integration:

| Column | Meaning |
| ------ | ------- |
| Integration | Ecosystem area, such as Logging or OpenTelemetry. |
| APIs | Count of actionable APIs, support types, and telemetry controls in the focused section. |

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
4. The roll-up includes categories with at least one actionable type or starter
   API.
5. Focused sections sort rows by `Kind`, then by the displayed `Type` or `API`.

The model is deliberately curated. It should avoid claiming complete support
from weak signals, and it should prefer stable, low-noise examples over exhaustive
metadata inventory.

## Relationship to sections and categories

`Integrations` is both a roll-up section and part of the `@Integrations`
section category. Categories are section-selection macros, not a new filtering
axis. They expand to command-local section sets and then normal section
renderability still applies.

Use:

```bash
-S Integrations
-S "<focused integration>"
-S @Integrations
```

This keeps integrations aligned with section backpressure and schema discovery.
