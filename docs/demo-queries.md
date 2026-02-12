# Demo Queries — Curated Picks

`dotnet-inspect` exposes a lot of functionality across many commands. This document
curates 14 high-impact invocations that exercise the breadth of the tool and produce
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

## 10. Find: Chat\*/Converse\*/Message\* across OpenAI, Azure, AWS, Anthropic

```bash
dotnet-inspect find "Chat*,Converse*,Message*" --package OpenAI --package Azure.AI.OpenAI --package AWSSDK.BedrockRuntime --package Anthropic
```

Pairs with demo #6 to show scope expansion. The default scope finds AI types
in curated packages; this demo adds four vendor SDK packages and searches
across all of them. The multi-glob catches each vendor's naming convention
(OpenAI/Azure use "Chat", AWS uses "Converse", Anthropic uses "Message").
Results span 118 types across four packages.

## 11. Depends: IFloatingPointIeee754 interface hierarchy

```bash
dotnet-inspect depends "IFloatingPointIeee754<TSelf>"
```

Walks the interface dependency DAG upward from `IFloatingPointIeee754<TSelf>`,
showing the full generic math hierarchy as a tree. Fans out into
`IFloatingPoint`, `INumber`, `INumberBase`, and then all the operator and
function interfaces (`IExponentialFunctions`, `ITrigonometricFunctions`, etc.).
The tree de-duplicates nodes at their shallowest introduction, revealing the
diamond inheritance pattern in the generic math design.

## 12. Code: OptionsFactory.Create — source, lowered C#, and IL

```bash
dotnet-inspect api --package Microsoft.Extensions.Options OptionsFactory Create
```

Shows the member detail page for `OptionsFactory<TOptions>.Create` — the
method that wires up the options pattern. The output includes four sections:
original C# source (via SourceLink), lowered C# (decompiled from IL with
goto-based control flow), raw IL disassembly, and annotated IL with
pre-execution stack state at each instruction. A showcase of the tool's
decompilation pipeline on a real, well-known method.

## 13. Package search: Azure AI ecosystem

```bash
dotnet-inspect package search "Azure.AI"
```

Searches NuGet for packages matching "Azure.AI" and displays them in a
formatted table with version, download counts, and descriptions. Discovers
14 Azure AI packages (OpenAI, FormRecognizer, DocumentIntelligence,
Translation, Vision, ContentSafety, etc.) without needing to know their
exact names. A showcase of the `package search` subcommand for NuGet
package discovery.

## 14. Find: Chat\* across Azure AI packages (prefix search)

```bash
dotnet-inspect find "Chat*" --package-prefix Azure.AI
```

Combines the `find` command with `--package-prefix` to search all packages
whose NuGet ID starts with "Azure.AI". The prefix is resolved via the NuGet
search API, discovering 14 packages and downloading each to search for
types matching `Chat*`. This is a powerful combo — prefix-based scoping
removes the need to know exact package names when exploring a vendor's
SDK ecosystem.
