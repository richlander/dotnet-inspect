# Demo Queries — Curated Picks

`dotnet-inspect` exposes a lot of functionality across many commands. This document
curates 16 high-impact invocations that exercise the breadth of the tool and produce
visually compelling output. Each entry explains **what** it shows and **why** it is
interesting.

These queries power the `demo` command (`dotnet-inspect demo`).

## 1. Shape: INumber\<TSelf> — generic math interface

```bash
dotnet-inspect type "System.Numerics.INumber<TSelf>"
```

Shows the full type shape diagram for the `INumber<TSelf>` generic math interface.
The self-referential constraint (`TSelf : INumber<TSelf>`), 23 implemented interfaces
with varying type parameter combinations (`IAdditionOperators<TSelf, TSelf, TSelf>`,
`IComparisonOperators<TSelf, TSelf, bool>`), and operator methods make this the
ultimate showcase for the `type` command's shape view on an interface.

## 2. Shape: Int128 — generic math concrete type

```bash
dotnet-inspect type Int128 --platform System.Runtime
```

The concrete counterpart to demo #1. `Int128` is a struct implementing 31 generic
math interfaces, with 125 methods including arithmetic operators, checked operators,
and implicit/explicit conversion operators. The interface list shows the full
generic math hierarchy resolved with concrete types
(`IShiftOperators<Int128, int, Int128>` — three different types in one interface).

## 3. Shape: ValueTuple — Create staircase

```bash
dotnet-inspect type ValueTuple --platform System.Runtime
```

The `ValueTuple` factory class has 9 `Create` overloads that grow from zero to
eight type parameters, producing a visual staircase in the shape output.
The 8-parameter version wraps into `ValueTuple<T1,...,T7,ValueTuple<T8>>` —
showing how the runtime handles arbitrary-length tuples via nesting.

## 4. Members: JsonSerializer

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json
```

Lists all public members of `JsonSerializer` — one of the most-used types in
modern .NET. Shows overload grouping, return types, and parameter signatures.
Good intro to the `member` command at member granularity.

## 5. Code: OptionsFactory.Create — source, lowered C#, and IL

```bash
dotnet-inspect member OptionsFactory --package Microsoft.Extensions.Options -m Create
```

Shows the member detail page for `OptionsFactory<TOptions>.Create` — the
method that wires up the options pattern. The output includes four sections:
original C# source (via SourceLink), lowered C# (decompiled from IL with
goto-based control flow), raw IL disassembly, and annotated IL with
pre-execution stack state at each instruction. A showcase of the tool's
decompilation pipeline on a real, well-known method.

## 6. Depends: IFloatingPointIeee754 interface hierarchy

```bash
dotnet-inspect depends "IFloatingPointIeee754<TSelf>"
```

Walks the interface dependency DAG upward from `IFloatingPointIeee754<TSelf>`,
showing the full generic math hierarchy as a tree. Fans out into
`IFloatingPoint`, `INumber`, `INumberBase`, and then all the operator and
function interfaces (`IExponentialFunctions`, `ITrigonometricFunctions`, etc.).
The tree de-duplicates nodes at their shallowest introduction, revealing the
diamond inheritance pattern in the generic math design.

## 7. Diff: System.CommandLine breaking changes

```bash
dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 -v:q
```

Shows 134 breaking changes, 81 additive changes across 83 types between the
beta and the stable release of System.CommandLine. Dramatic API churn that
makes a compelling case for the `diff` command during migrations.

## 8. Diff: System.Text.Json evolution — diffstat view

```bash
dotnet-inspect diff System.Text.Json@8.0.0..10.0.3 --oneline
```

A git-style diffstat showing the evolution of System.Text.Json across two major
versions. Each line shows a type with `+` for added, `~` for changed, and `✗`
for breaking. The compact summary format makes it easy to scan large API diffs
at a glance — 78 additive changes and 2 breaking across 23 types.

## 9. Extensions for IServiceCollection

```bash
dotnet-inspect extensions IServiceCollection
```

Finds 120+ extension methods targeting `IServiceCollection` across both the
runtime platform and ASP.NET Core. This is the canonical "extension method
explosion" in .NET — every middleware and service registration shows up here.
Demonstrates cross-scope search with no explicit `--package` flag.

## 10. Find: Chat\* types

```bash
dotnet-inspect find "Chat*"
```

Searches the default scope (platform + curated packages) for types matching
`Chat*`. Finds AI-related types from `Microsoft.Extensions.AI` — a timely
result that shows the tool keeps pace with the latest .NET ecosystem additions.

## 11. Find: Chat\*/Converse\*/Message\* across OpenAI, Azure, AWS, Anthropic

```bash
dotnet-inspect find "Chat*,Converse*,Message*" --package OpenAI --package Azure.AI.OpenAI --package AWSSDK.BedrockRuntime --package Anthropic
```

Pairs with demo #10 to show scope expansion. The default scope finds AI types
in curated packages; this demo adds four vendor SDK packages and searches
across all of them. The multi-glob catches each vendor's naming convention
(OpenAI/Azure use "Chat", AWS uses "Converse", Anthropic uses "Message").
Results span 118 types across four packages.

## 12. Find: Chat\* across Azure AI packages (prefix search)

```bash
dotnet-inspect find "Chat*" --package-prefix Azure.AI
```

Combines the `find` command with `--package-prefix` to search all packages
whose NuGet ID starts with "Azure.AI". The prefix is resolved via the NuGet
search API, discovering 14 packages and downloading each to search for
types matching `Chat*`. This is a powerful combo — prefix-based scoping
removes the need to know exact package names when exploring a vendor's
SDK ecosystem.

## 13. Implements Stream

```bash
dotnet-inspect implements Stream
```

Finds every concrete type that extends `Stream` across all platform frameworks.
Results include `FileStream`, `MemoryStream`, `CryptoStream`, `NetworkStream`,
`QuicStream`, and ASP.NET Core buffered streams. A good showcase of the
`implements` command's ability to search across multiple frameworks at once.

## 14. Library: dependency tree

```bash
dotnet-inspect library Microsoft.Extensions.AI.OpenAI --dependencies
```

Renders a visual dependency tree for the OpenAI integration library, showing
transitive dependencies like `System.ClientModel`, `System.Text.Json`, and
`System.Text.RegularExpressions`. The ASCII tree format makes it easy to
understand the full dependency graph at a glance.

## 15. Package: System.Text.Json@8.0.0 vulnerabilities

```bash
dotnet-inspect package System.Text.Json@8.0.0 -s Vulnerabilities
```

Shows known security vulnerabilities for an older version of System.Text.Json.
The `-s Vulnerabilities` section filter zeroes in on the security data. A
practical demo of the tool's value for security auditing.

## 16. Package search: Azure AI ecosystem

```bash
dotnet-inspect package search "Azure.AI"
```

Searches NuGet for packages matching "Azure.AI" and displays them in a
formatted table with version, download counts, and descriptions. Discovers
14 Azure AI packages (OpenAI, FormRecognizer, DocumentIntelligence,
Translation, Vision, ContentSafety, etc.) without needing to know their
exact names. A showcase of the `package search` subcommand for NuGet
package discovery.

## 17. Select discovery: what can I select?

```bash
dotnet-inspect package System.Text.Json -S
```

Invokes `-S` (select) with no arguments, triggering discovery mode. Lists
every selectable name for the package output — field names like Version,
License, Authors and column names like TFM and Property — each annotated
with its kind. This is the starting point for any select-based query:
ask the tool what's available before narrowing down.

## 18. Select columns: just the type names

```bash
dotnet-inspect type --package System.Text.Json -S Type
```

Uses `-S Type` to strip the Members column from every table in the type
listing, leaving just type names grouped by kind (classes, structs, enums,
interfaces). The one-liner summary fields (Library, Types, Methods, etc.)
are also removed since they don't match the select list. Compare with
`type --package System.Text.Json` to see the full output — the contrast
makes the column projection immediately obvious.

## 19. Select columns: types and changes only

```bash
dotnet-inspect diff System.Text.Json@8.0.0..10.0.3 --oneline -S Type,Change
```

Combines `--oneline` (compact table format) with `-S Type,Change` to
strip the diff table down to just two columns: the type name and the
change indicator (+, ~, x). Removes the verbose member-count columns,
leaving a clean scan of what was added, changed, or broken across two
major versions.
