# Backlog

## Unified `depends` command — library and package modes

Type dependency mode is implemented. The remaining modes are:

### Library dependencies

Subsumes `library --dependencies`. Walk assembly references recursively.

```text
dotnet-inspect depends --library Microsoft.Extensions.AI.OpenAI
```

### Package dependencies

Subsumes `package --dependencies`. Walk NuGet package dependency graph.

```text
dotnet-inspect depends --package System.Text.Json
dotnet-inspect depends --package System.Text.Json --tfm net9.0
```

### Design notes

- One command, auto-detected scope: bare name → type (default), `--library` →
  assembly references, `--package` → NuGet dependencies.
- Same tree rendering as `library --dependencies` today.
- Type mode is done; library and package modes are pending.

## Use Markout MarkoutField for demo list

The `demo list` output currently formats the numbered list manually. Once
Markout adds `MarkoutField` (a structured labeled list type), the demo list
should use it instead. This would give us bold rendering on TTY, proper
alignment, and a reusable pattern for other categorized lists.

```csharp
record MarkoutField(string Label, string Description, string? Detail = null);

// Label = "Insight", Description = "What does the generic math hierarchy look like?"
// Detail = "dotnet-inspect api System.Runtime \"INumber<TSelf>\" --shape"
```
