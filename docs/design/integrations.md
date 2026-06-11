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
| Examples | Count of actionable API examples in the focused section. |
| Next | Section selector for the focused detail view. |

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
| Dependency Injection | `IServiceCollection`, service registration extension types, builder types. |
| Logging | `ILogger`, `ILogger<T>`, `LoggerMessageAttribute`, logging extension types. |
| OpenTelemetry | `ActivitySource`, `Meter`, `DiagnosticSource`, OpenTelemetry provider/exporter types. |
| Options | `IOptions<T>`, `IOptionsMonitor<T>`, configure/validate options types. |
| Hosting | `IHostedService`, `BackgroundService`, host builder types. |
| Health Checks | `IHealthCheck`, health check builder/service types. |
| HTTP Client | `IHttpClientFactory`, `IHttpClientBuilder`, HTTP client builder extension types. |

Assembly references are not integration currency. They belong in references or
signals, not focused integration rows. A direct assembly reference only says the
library was compiled against another assembly; it does not tell the user what
API to use.

## Detail section shape

Focused sections render examples as types:

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

If every row in a focused section has the same kind, hide `Kind` and render only
`Type`. This keeps the common case compact while preserving useful distinctions
for integrations such as OpenTelemetry.

## Detection and ranking

Detection reads metadata only:

1. Type references and type definitions are scanned for curated integration
   namespaces and well-known types.
2. The roll-up includes categories with at least one actionable type.
3. Focused sections sort high-value types first, then alphabetically.

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
