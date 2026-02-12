# Backlog

## Operator overload sugar in API output

The `api` and `--shape` output currently displays operator overloads using their
raw IL method names (e.g., `op_Addition`, `op_Equality`, `op_Implicit`). These
should be rendered using C# operator syntax instead:

| Raw name | Desired display |
| -------- | --------------- |
| `op_Addition` | `operator +` |
| `op_Subtraction` | `operator -` |
| `op_Multiply` | `operator *` |
| `op_Division` | `operator /` |
| `op_Equality` | `operator ==` |
| `op_Inequality` | `operator !=` |
| `op_LessThan` | `operator <` |
| `op_GreaterThan` | `operator >` |
| `op_Implicit` | `implicit operator` |
| `op_Explicit` | `explicit operator` |
| `op_CheckedAddition` | `checked operator +` |

Note: The decompiler's `CSharpEmitter` already has operator sugar for decompiled
method bodies. This backlog item is about the API surface output (type member
tables and `--shape` tree), which uses a different code path.

Visible in e.g. `dotnet-inspect api System.Runtime Int128 --shape` where `op_*`
methods dominate the method list.

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
