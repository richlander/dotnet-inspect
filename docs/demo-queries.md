# Demo Queries — Curated Picks

`dotnet-inspect` exposes a lot of functionality across many commands. This document
curates 12 high-impact invocations that exercise the breadth of the tool and produce
visually compelling output. Each entry explains **what** it shows and **why** it is
interesting.

These queries power the `demo` command (`dotnet-inspect demo`).

## 1. Shape: INumber\<TSelf> — generic math interface

```bash
dotnet-inspect api System.Runtime "INumber<TSelf>" --shape
```

Shows the full type shape diagram for the `INumber<TSelf>` generic math interface.
The self-referential constraint (`TSelf : INumber<TSelf>`), 23 implemented interfaces
with varying type parameter combinations (`IAdditionOperators<TSelf, TSelf, TSelf>`,
`IComparisonOperators<TSelf, TSelf, bool>`), and operator methods make this the
ultimate showcase for `--shape` on an interface.

## 2. Extensions for IServiceCollection

```bash
dotnet-inspect extensions IServiceCollection
```

Finds 120+ extension methods targeting `IServiceCollection` across both the
runtime platform and ASP.NET Core. This is the canonical "extension method
explosion" in .NET — every middleware and service registration shows up here.
Demonstrates cross-scope search with no explicit `--package` flag.

## 3. Implements Stream

```bash
dotnet-inspect implements Stream
```

Finds every concrete type that extends `Stream` across all platform frameworks.
Results include `FileStream`, `MemoryStream`, `CryptoStream`, `NetworkStream`,
`QuicStream`, and ASP.NET Core buffered streams. A good showcase of the
`implements` command's ability to search across multiple frameworks at once.

## 4. Diff: System.CommandLine breaking changes

```bash
dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q
```

Shows 134 breaking changes, 81 additive changes across 83 types between the
beta and the stable release of System.CommandLine. Dramatic API churn that
makes a compelling case for the `diff` command during migrations.

## 5. API: JsonSerializer members

```bash
dotnet-inspect api System.Text.Json JsonSerializer
```

Lists all public members of `JsonSerializer` — one of the most-used types in
modern .NET. Shows overload grouping, return types, and parameter signatures.
Good intro to the `api` command at member granularity.

## 6. Find: Chat\* types

```bash
dotnet-inspect find "Chat*"
```

Searches the default scope (platform + curated packages) for types matching
`Chat*`. Finds AI-related types from `Microsoft.Extensions.AI` — a timely
result that shows the tool keeps pace with the latest .NET ecosystem additions.

## 7. Package: System.Text.Json@8.0.0 vulnerabilities

```bash
dotnet-inspect package System.Text.Json@8.0.0 -s Vulnerabilities
```

Shows known security vulnerabilities for an older version of System.Text.Json.
The `-s Vulnerabilities` section filter zeroes in on the security data. A
practical demo of the tool's value for security auditing.

## 8. Library: dependency tree

```bash
dotnet-inspect library Microsoft.Extensions.AI.OpenAI --dependencies
```

Renders a visual dependency tree for the OpenAI integration library, showing
transitive dependencies like `System.ClientModel`, `System.Text.Json`, and
`System.Text.RegularExpressions`. The ASCII tree format makes it easy to
understand the full dependency graph at a glance.

## 9. Shape: Int128 — generic math concrete type

```bash
dotnet-inspect api System.Runtime Int128 --shape
```

The concrete counterpart to demo #1. `Int128` is a struct implementing 31 generic
math interfaces, with 125 methods including arithmetic operators, checked operators,
and implicit/explicit conversion operators. The interface list shows the full
generic math hierarchy resolved with concrete types
(`IShiftOperators<Int128, int, Int128>` — three different types in one interface).

## 10. Find: Chat\*/Converse\* across OpenAI, Azure, and AWS

```bash
dotnet-inspect find "Chat*,Converse*" --package OpenAI --package Azure.AI.OpenAI --package AWSSDK.BedrockRuntime
```

Pairs with demo #6 to show scope expansion. The default scope finds AI types
in curated packages; this demo adds three vendor SDK packages and searches
across all of them. The multi-glob `Chat*,Converse*` catches both naming
conventions (OpenAI/Azure call it "Chat", AWS calls it "Converse"). Results
span 67+ types across three packages, showing how `--package` flags compose.

## 11. Extensions for HttpClient

```bash
dotnet-inspect extensions HttpClient
```

Finds extension methods targeting `HttpClient` across the default scope.
Everyone knows `HttpClient`; seeing what extends it is immediately relatable
and often surprising (JSON helpers, logging, DI integration).

## 12. Diff: System.Text.Json breaking changes (8.0→10.0)

```bash
dotnet-inspect diff --package System.Text.Json@8.0.0..10.0.3 --breaking
```

Shows only the breaking changes between two major versions of System.Text.Json.
The `--breaking` filter focuses on what matters most for migration planning.
A real-world scenario that many .NET developers face.
