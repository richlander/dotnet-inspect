---
name: dotnet-inspect-relationships
version: 0.1.0
description: Map how code connects — implementors and subclasses, extension methods, dependency graphs, reverse callers, and ecosystem integrations. Many outputs are graph-shaped (add --mermaid).
---

# dotnet-inspect: relationships and dependency graphs

Use this skill to map how code connects: what implements or extends a type, what
it depends on, and who calls it. Many of these outputs are graph-shaped — add
`--mermaid` for a diagram (or `--mermaid --markdown` to embed one).

```bash
dnx dotnet-inspect -y -- <command>
```

Scope any of these commands the same way: `--package Foo` (repeatable),
`--library path.dll`, `--platform` (all in-box frameworks), `--extensions` or
`--aspnetcore` (curated Microsoft.* sets), `--package-prefix Azure.AI` (every
package under a NuGet ID prefix), and `--tfm net9.0`.

## What implements or extends it?

`implements Interface` finds concrete implementors and subclasses;
`extensions Type` finds extension methods. Add `--reachable` (with `--depth N`)
to include extensions on types reachable through properties and methods.

```bash
dnx dotnet-inspect -y -- implements IDisposable --platform
dnx dotnet-inspect -y -- extensions HttpClient --platform --reachable
dnx dotnet-inspect -y -- implements ILogger --package-prefix Microsoft.Extensions
```

## What does it depend on?

`depends Type` walks dependency graphs upward — type hierarchy, library
references, or package dependencies, depending on scope. Add `--mermaid` for a
diagram.

```bash
dnx dotnet-inspect -y -- depends JsonSerializer --package System.Text.Json
dnx dotnet-inspect -y -- depends MyType --library MyLib.dll --mermaid
```

## Who calls it? (reverse edges)

`member Type Method:1 -S Calls` lists what a method calls; `-S Callers` lists the
call sites that reach it. Widen the caller search with `--bin`, `--project`, or
`--caller-package`.

```bash
dnx dotnet-inspect -y -- member Type Method:1 -S Calls
dnx dotnet-inspect -y -- member string IndexOf:7 -S Callers --caller-package System.Text.Json@9.0.0 --tfm net9.0
```

## What does it integrate with? (ecosystem)

`-S @Integrations` on `library` or `package --library` rolls up the ecosystem
frameworks a library plugs into — DI, hosting, ASP.NET Core, AI, OpenTelemetry,
configuration, logging, and more — plus `Integration Opportunities`.

```bash
dnx dotnet-inspect -y -- package Microsoft.Extensions.AI --library -S @Integrations
```
