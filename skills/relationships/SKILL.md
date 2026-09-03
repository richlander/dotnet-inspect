---
name: dotnet-inspect-relationships
version: 0.1.0
description: Map how code connects — implementors and subclasses, extension methods, dependency graphs, reverse callers, and ecosystem integrations. Many outputs are graph-shaped (add --mermaid).
---

# dotnet-inspect: relationships and dependency graphs

Use this skill to map how code connects: what implements or extends a type, what
it depends on, and who calls it. Dependency views already render as trees; add
`--mermaid` for a standalone diagram or `--markdown --mermaid` to embed one.
Member Call Graphs instead default to Markdown edge tables and offer an
explicit `--tree` path view.

```bash
dnx dotnet-inspect -y -- <command>
```

Scope any of these commands the same way: `--project path/to.csproj` (restored
project references), `--package Foo` (repeatable), `--library path.dll`,
`--platform` (all in-box frameworks), `--extensions` or `--aspnetcore` (curated
Microsoft.* sets), and `--tfm net9.0`. For `implements` and `extensions`, use
`--package-prefix Azure.AI` to search up to 500 packages under a NuGet ID
prefix; the command warns when that bound is reached. `depends` does not accept
`--package-prefix`.

`--project` reads existing restored assets; restore/build first if dependencies
changed.

## What implements or extends it?

`implements Interface` finds concrete implementors and subclasses;
`extensions Type` finds extension methods. Add `--reachable` (with `--depth N`)
to include extensions on types reachable through properties and methods.

```bash
dnx dotnet-inspect -y -- implements IDisposable --platform
dnx dotnet-inspect -y -- extensions HttpClient --platform --reachable
dnx dotnet-inspect -y -- implements ILogger --package-prefix Microsoft.Extensions
dnx dotnet-inspect -y -- implements IEquatable --project ./src/App/App.csproj -v:q
dnx dotnet-inspect -y -- extensions string --project ./src/App/App.csproj -v:n
```

## What does it depend on?

`depends Type` walks dependency graphs upward — type hierarchy, library
references, or package dependencies, depending on scope. Add `--mermaid` for a
diagram.

```bash
dnx dotnet-inspect -y -- depends JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- depends MyType --library MyLib.dll --mermaid
dnx dotnet-inspect -y -- depends Command --project ./src/App/App.csproj -v:q
```

## Who calls it? (reverse edges)

`member Type -m Method:1 -S Calls` lists what a method calls; `-S Callers` lists
the call sites that reach it. With an explicit source, widen the caller search
with `--bin`, `--project`, or `--caller-package`. With no explicit source, the
first `--project` is the source context; repeated `--project` values after it
remain caller scopes.

```bash
dnx dotnet-inspect -y -- member Type -m Method:1 -S Calls
dnx dotnet-inspect -y -- member string -m IndexOf~147d84bbd7 -S Callers --caller-package System.Text.Json@9.0.0 --tfm net9.0
```

`Call Graph` is the bounded bidirectional view centered on one member: inbound
callers toward entry points plus outbound calls. Its default Markdown view is
an edge table. Select `--tree` for a standalone path-oriented view,
`--mermaid` for a standalone diagram, or `--markdown --mermaid` for a diagram
inside the Markdown document. For scripts, `--tsv` and `--jsonl` expose the
same ordered edges. Machine fields `from` and `to` are always present;
`from_group`, `to_group`, and `label` appear only when the whole graph uses
them. A row window does not change that schema. `--count` and `--rows` address
edge rows consistently across these views.

For a type-level dependency summary, `Called Types` groups direct calls by
target type, assembly, members, and call kinds.

```bash
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph"
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --tree
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --mermaid
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --markdown --mermaid
dnx dotnet-inspect -y -- member Type -m Method:1 -S "Call Graph" --jsonl
dnx dotnet-inspect -y -- type Type --library MyLib.dll -S "Called Types"
```

## What does it integrate with? (ecosystem)

`graph integrations` compares an explicit package set inside one
binding-consistent target. Repeat `--package name[@version]`, provide the shared
`--tfm`, and add `--relationship <id>` only when the default Integration family
should be narrowed. This is an induced set, not a traversal: it has no direction
or depth. Markdown is an edge table by default; `--tree`, `--mermaid`, `--json`,
`--jsonl`, `--count`, and `--rows` project the same logical relationships.
Missing `api.extension` or `integration.observed` endpoints whose assemblies are
absent from the explicit package set remain outside the induced graph; add the
owning package to admit those relationships. A missing
`integration.opportunity` target and other binding failures -- unavailable,
ambiguous, rejected, or selected outside the active context -- remain visible
and produce a nonzero exit.

```bash
dnx dotnet-inspect -y -- graph integrations \
  --package Microsoft.Extensions.DependencyInjection.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging.Abstractions@10.0.0 \
  --package Microsoft.Extensions.Logging@10.0.0 \
  --package Microsoft.Extensions.Http@10.0.0 \
  --tfm net10.0 \
  --relationship integration.observed
```

`-S @Integrations` on `library` or `package --library` rolls up the ecosystem
frameworks a library plugs into — DI, hosting, ASP.NET Core, AI, OpenTelemetry,
configuration, logging, and more — plus `Integration Opportunities` and
language/runtime integration signals like C# union types.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --library -S @Integrations
dnx dotnet-inspect -y -- library MyLibrary.dll -S "Union Types" --tsv
dnx dotnet-inspect -y -- library --platform System.Text.Json -S "Union Types" --tsv
```

Use `Union Types` when looking for C# union adoption in libraries. It reports
types annotated with `System.Runtime.CompilerServices.UnionAttribute`, whether
they implement `IUnion`, and constructor-derived case types. Current platform
libraries may expose the runtime infrastructure before any production library
declares union types, so an empty table is still a useful signal.
