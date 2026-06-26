---
name: dotnet-inspect-performance
version: 0.1.0
description: Whole-assembly call-graph leverage ranking and performance triage for .NET libraries (experimental).
---

# dotnet-inspect: performance analysis and triage

Use this skill to find the members worth optimizing or hardening first in a .NET
assembly, and to triage them against actionable rewrite shapes. This analysis is
experimental; section names and signal sets may change between releases.

```bash
dnx dotnet-inspect -y -- <command>
```

## Rank by leverage first

`Top Leverage` ranks members by call-graph leverage: direct callers, `Root
Reach` (distinct entry points that transitively reach a member), fanout, depth,
and loop calls. Start here on a whole library, then narrow to a type.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Top Leverage"
dnx dotnet-inspect -y -- type MyType --library MyLib.dll --all -S "Top Leverage"
```

Ranking rows carry a copyable `Stable` selector, `Visibility`, and `Selector`.
Add `--all` to include non-public members.

## Triage against rewrite shapes

`Performance Triage` re-ranks the same leverage against actionable rewrite
shapes — small non-escaping arrays, temporary or span-to-array copies, capturing
delegates, stateless instance methods — so hot, fixable members surface first.

```bash
dnx dotnet-inspect -y -- library MyLib.dll -S "Performance Triage"
```

Target IL-visible costs (allocations: box, newarr, delegate newobj,
ToArray/ToList/Concat), not JIT-handled concerns (isinst/castclass folding,
devirtualization, bounds-check elimination, null-check folding).

## Drill a candidate

`Call Graph` is a bounded outbound tree; `Caller Graph` is a bounded reverse
tree to entry points. Project per-node cost with `--fields` (alloc, copy,
unsafe, reflection, throw/exception, catch/finally).

```bash
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Call Graph,Facts"
dnx dotnet-inspect -y -- member MyType Method:1 --library MyLib.dll -S "Caller Graph" --fields "Throw,Catch,Finally"
```
