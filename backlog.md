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

## Unified `depends` command (types, libraries, packages)

A single `depends` command that walks dependency graphs upward across all
three content kinds:

### Type dependencies

Walk the inheritance and interface implementation graph upward from a type.

```text
dotnet-inspect depends IFloatingPointIeee754 --platform

├─ IFloatingPoint<TSelf>
│  ├─ INumber<TSelf>
│  │  ├─ INumberBase<TSelf>
│  │  │  ├─ IAdditionOperators<TSelf, TSelf, TSelf>
│  │  │  ├─ ISubtractionOperators<TSelf, TSelf, TSelf>
│  │  │  ├─ IMultiplyOperators<TSelf, TSelf, TSelf>
│  │  │  ├─ IDivisionOperators<TSelf, TSelf, TSelf>
│  │  │  ...
│  │  ├─ IComparable<TSelf>
│  │  └─ IComparisonOperators<TSelf, TSelf, bool>
│  └─ ISignedNumber<TSelf>
├─ IExponentialFunctions<TSelf>
├─ IHyperbolicFunctions<TSelf>
├─ ILogarithmicFunctions<TSelf>
├─ ITrigonometricFunctions<TSelf>
└─ IRootFunctions<TSelf>
```

This is the inverse of `implements` (which walks *down* — "who implements X?").
`depends` walks *up* — "what does X depend on?". The tree shows the full DAG,
de-duplicating nodes at their shallowest introduction (same strategy as
`library --dependencies`).

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
- For types: interfaces implement other interfaces; classes extend base classes
  and implement interfaces. Concrete types like `Int128` would show the full
  generic math interface hierarchy resolved with concrete type arguments.

## Package search and prefix-based scoping

Today `--package` requires exact package names. Two related features:

### NuGet package search

A `package search` (or `package find`) subcommand that searches NuGet for
packages by keyword, similar to `dotnet package search` or the NuGet search
API. This provides discoverability without leaving the tool.

```text
dotnet-inspect package search "Azure.AI"
dotnet-inspect package search "AWSSDK" --take 20
```

### Prefix-based package scope (`--package-prefix`)

A `--package-prefix` flag on `find`, `extensions`, and `implements` that
searches all packages matching a NuGet prefix. This would use the NuGet
search API to discover packages, then search across all of them.

```text
dotnet-inspect find "Chat*" --package-prefix Azure.AI
dotnet-inspect find "Converse*" --package-prefix AWSSDK
dotnet-inspect extensions IChatClient --package-prefix Microsoft.Extensions.AI
```

This removes the need to know exact package names when exploring a vendor's
ecosystem. Neither Azure nor AWS ship metapackages that pull in service-level
SDKs, so prefix search is the practical alternative.
